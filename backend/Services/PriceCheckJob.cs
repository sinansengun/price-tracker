using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Hangfire.Console;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PriceTracker.Services;

public class PriceCheckJob(
    AppDbContext db,
    ScraperService scraper,
    FirebaseRemoteConfigService remoteConfigService,
    ILogger<PriceCheckJob> logger)
{
    private static readonly HttpClient FcmHttp = new();

    public async Task CheckAllProductsAsync(PerformContext? context = null)
    {
        using var contextScope = HangfireConsoleContextAccessor.Push(context);

        var products = await db.Products.ToListAsync();
        logger.LogInformation("Checking prices for {Count} products", products.Count);
        context?.WriteLine($"Checking prices for {products.Count} products");

        foreach (var product in products)
        {
            await CheckProductAsync(product.Id, context);
        }

        context?.WriteLine("Price check run completed.");
    }

    public async Task CheckProductAsync(int productId, PerformContext? context = null)
    {
        using var contextScope = HangfireConsoleContextAccessor.Push(context);

        context?.WriteLine($"Checking productId={productId}");

        var product = await db.Products.FindAsync(productId);
        if (product == null)
        {
            context?.WriteLine($"Product not found: productId={productId}");
            return;
        }

        var result = await scraper.ScrapeAsync(product.Url);
        if (result == null)
        {
            context?.WriteLine($"Scrape failed: productId={productId} url={product.Url}");
            return;
        }

        var previousPrice = product.CurrentPrice;
        product.CurrentPrice = result.Price;
        product.InitialPrice ??= result.Price;
        product.LastCheckedAt = DateTime.UtcNow;

        if (string.IsNullOrEmpty(product.Name) || product.Name == "Bilinmeyen Ürün")
            product.Name = result.Name;

        if (string.IsNullOrEmpty(product.ImageUrl))
            product.ImageUrl = result.ImageUrl;

        if (string.IsNullOrEmpty(product.Store))
            product.Store = result.Store;

        db.PriceHistories.Add(new PriceHistory
        {
            ProductId = product.Id,
            Price = result.Price,
            CheckedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        context?.WriteLine($"Saved price for '{product.Name}': {(previousPrice?.ToString("F2") ?? "-" )} -> {result.Price:F2}");

        if (previousPrice.HasValue && result.Price < previousPrice.Value)
        {
            var minDropPercent = await remoteConfigService.GetMinPriceDropPercentAsync();
            var dropPercent = previousPrice.Value <= 0
                ? 0
                : ((previousPrice.Value - result.Price) / previousPrice.Value) * 100;

            if (dropPercent < minDropPercent)
            {
                logger.LogInformation(
                    "Price drop below threshold for '{Name}': drop={DropPercent:F2}%, threshold={ThresholdPercent:F2}%",
                    product.Name,
                    dropPercent,
                    minDropPercent);
                context?.WriteLine(
                    $"Price dropped but no notification (below threshold): drop={dropPercent:F2}%, threshold={minDropPercent:F2}%");
                return;
            }

            logger.LogInformation(
                "Price dropped for '{Name}': {OldPrice} → {NewPrice} (drop={DropPercent:F2}%, threshold={ThresholdPercent:F2}%)",
                product.Name,
                previousPrice.Value,
                result.Price,
                dropPercent,
                minDropPercent);
            context?.WriteLine(
                $"Price dropped for '{product.Name}': {previousPrice.Value:F2} -> {result.Price:F2} (drop={dropPercent:F2}%, threshold={minDropPercent:F2}%)");

            await SendPriceDropNotificationsAsync(product, previousPrice.Value, result.Price, context);
        }
        else if (previousPrice.HasValue && previousPrice.Value != result.Price)
        {
            logger.LogInformation(
                "Price changed for '{Name}': {OldPrice} → {NewPrice}",
                product.Name, previousPrice.Value, result.Price);
            context?.WriteLine($"Price changed for '{product.Name}': {previousPrice.Value:F2} -> {result.Price:F2}");
        }
        else
        {
            context?.WriteLine($"No price change for '{product.Name}' ({result.Price:F2})");
        }
    }

    public async Task<object> SendTestNotificationAsync(Product product, decimal oldPrice, decimal newPrice)
    {
        if (FirebaseApp.DefaultInstance == null) return new { error = "FirebaseApp not initialized" };

        var userProducts = await db.UserProducts
            .Include(up => up.User)
            .Where(up => up.ProductId == product.Id && up.User.FcmToken != null && up.User.FcmToken != "")
            .ToListAsync();

        if (userProducts.Count == 0) return new { error = "No users with FCM token for this product", productId = product.Id };

        var results = new List<object>();
        var historyEntries = new List<NotificationHistory>();
        var title = "Fiyat Düştü! 🎉";
        var body = $"{product.Name}: {oldPrice:F2}₺ → {newPrice:F2}₺";
        foreach (var up in userProducts)
        {
            try
            {
                var message = new Message
                {
                    Token = up.User.FcmToken,
                    Notification = new Notification
                    {
                        Title = title,
                        Body = body
                    },
                    Data = new Dictionary<string, string>
                    {
                        ["productId"] = product.Id.ToString(),
                        ["userProductId"] = up.Id.ToString()
                    }
                };
                var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                results.Add(new { userId = up.UserId, status = "sent", response });
                historyEntries.Add(BuildNotificationHistory(up, product.Id, oldPrice, newPrice, title, body, true, null));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM bildirimi gönderilemedi: userId={UserId}", up.UserId);
                // FirebaseAdmin SDK bazı ortamlarda auth header'ı düşürüp UNAUTHENTICATED dönebiliyor.
                // Bu durumda FCM HTTP v1'e direkt fallback deniyoruz.
                if (ex is FirebaseMessagingException fex &&
                    fex.Message.Contains("missing required authentication credential", StringComparison.OrdinalIgnoreCase))
                {
                    var fallback = await SendViaHttpV1Async(up.User.FcmToken!, product, oldPrice, newPrice, up.Id);
                    results.Add(new
                    {
                        userId = up.UserId,
                        status = fallback.success ? "sent-fallback" : "failed",
                        response = fallback.response,
                        error = fallback.success ? null : fallback.error
                    });
                    historyEntries.Add(BuildNotificationHistory(
                        up,
                        product.Id,
                        oldPrice,
                        newPrice,
                        title,
                        body,
                        fallback.success,
                        fallback.success ? null : (fallback.error ?? fallback.response)));
                }
                else
                {
                    results.Add(new { userId = up.UserId, status = "failed", error = ex.Message });
                    historyEntries.Add(BuildNotificationHistory(up, product.Id, oldPrice, newPrice, title, body, false, ex.Message));
                }
            }
        }

        if (historyEntries.Count > 0)
        {
            db.NotificationHistories.AddRange(historyEntries);
            await db.SaveChangesAsync();
        }

        return new { matchedUsers = userProducts.Count, results };
    }

    private async Task SendPriceDropNotificationsAsync(Product product, decimal oldPrice, decimal newPrice, PerformContext? context = null)
    {
        if (FirebaseApp.DefaultInstance == null) return;

        var userProducts = await db.UserProducts
            .Include(up => up.User)
            .Where(up => up.ProductId == product.Id && up.User.FcmToken != null && up.User.FcmToken != "")
            .ToListAsync();

        context?.WriteLine($"Sending notifications: productId={product.Id}, recipients={userProducts.Count}");
        var historyEntries = new List<NotificationHistory>();
        var title = "Fiyat Düştü! 🎉";
        var body = $"{product.Name}: {oldPrice:F2}₺ → {newPrice:F2}₺";

        foreach (var up in userProducts)
        {
            try
            {
                var message = new Message
                {
                    Token = up.User.FcmToken,
                    Notification = new Notification
                    {
                        Title = title,
                        Body = body
                    },
                    Data = new Dictionary<string, string>
                    {
                        ["productId"] = product.Id.ToString(),
                        ["userProductId"] = up.Id.ToString()
                    }
                };
                await FirebaseMessaging.DefaultInstance.SendAsync(message);
                context?.WriteLine($"Notification sent: userId={up.UserId}");
                historyEntries.Add(BuildNotificationHistory(up, product.Id, oldPrice, newPrice, title, body, true, null));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM bildirimi gönderilemedi: userId={UserId}", up.UserId);
                context?.WriteLine($"Notification failed: userId={up.UserId}, error={ex.Message}");
                if (ex is FirebaseMessagingException fex &&
                    fex.Message.Contains("missing required authentication credential", StringComparison.OrdinalIgnoreCase))
                {
                    var fallback = await SendViaHttpV1Async(up.User.FcmToken!, product, oldPrice, newPrice, up.Id);
                    if (!fallback.success)
                    {
                        logger.LogWarning("FCM fallback da başarısız oldu: userId={UserId}, error={Error}", up.UserId, fallback.error);
                        context?.WriteLine($"Fallback notification failed: userId={up.UserId}, error={fallback.error}");
                        historyEntries.Add(BuildNotificationHistory(
                            up,
                            product.Id,
                            oldPrice,
                            newPrice,
                            title,
                            body,
                            false,
                            fallback.error ?? fallback.response ?? ex.Message));
                    }
                    else
                    {
                        context?.WriteLine($"Fallback notification sent: userId={up.UserId}");
                        historyEntries.Add(BuildNotificationHistory(up, product.Id, oldPrice, newPrice, title, body, true, null));
                    }
                }
                else
                {
                    historyEntries.Add(BuildNotificationHistory(up, product.Id, oldPrice, newPrice, title, body, false, ex.Message));
                }
            }
        }

        if (historyEntries.Count > 0)
        {
            db.NotificationHistories.AddRange(historyEntries);
            await db.SaveChangesAsync();
        }
    }

    private static NotificationHistory BuildNotificationHistory(
        UserProduct up,
        int productId,
        decimal oldPrice,
        decimal newPrice,
        string title,
        string body,
        bool isSuccess,
        string? error)
    {
        return new NotificationHistory
        {
            UserId = up.UserId,
            ProductId = productId,
            UserProductId = up.Id,
            Title = title,
            Body = body,
            OldPrice = oldPrice,
            NewPrice = newPrice,
            IsSuccess = isSuccess,
            Error = string.IsNullOrWhiteSpace(error) ? null : error,
            SentAt = DateTime.UtcNow
        };
    }

    private async Task<(bool success, string? response, string? error)> SendViaHttpV1Async(
        string fcmToken,
        Product product,
        decimal oldPrice,
        decimal newPrice,
        int userProductId)
    {
        try
        {
            var app = FirebaseApp.DefaultInstance;
            if (app == null) return (false, null, "FirebaseApp not initialized");

            var projectId = app.Options.ProjectId;
            if (string.IsNullOrWhiteSpace(projectId)) return (false, null, "Firebase ProjectId is missing");

            var tokenProvider = app.Options.Credential.UnderlyingCredential as ITokenAccess;
            var accessToken = tokenProvider == null ? null : await tokenProvider.GetAccessTokenForRequestAsync();
            if (string.IsNullOrWhiteSpace(accessToken)) return (false, null, "Could not acquire access token");

            var payload = new
            {
                message = new
                {
                    token = fcmToken,
                    notification = new
                    {
                        title = "Fiyat Düştü! 🎉",
                        body = $"{product.Name}: {oldPrice:F2}₺ → {newPrice:F2}₺"
                    },
                    data = new Dictionary<string, string>
                    {
                        ["productId"] = product.Id.ToString(),
                        ["userProductId"] = userProductId.ToString()
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await FcmHttp.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return (true, body, null);

            return (false, body, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }
}