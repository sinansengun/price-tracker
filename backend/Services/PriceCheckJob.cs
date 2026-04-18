using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
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
    ILogger<PriceCheckJob> logger)
{
    private static readonly HttpClient FcmHttp = new();

    public async Task CheckAllProductsAsync()
    {
        var products = await db.Products.ToListAsync();
        logger.LogInformation("Checking prices for {Count} products", products.Count);

        foreach (var product in products)
        {
            await CheckProductAsync(product.Id);
        }
    }

    public async Task CheckProductAsync(int productId)
    {
        var product = await db.Products.FindAsync(productId);
        if (product == null) return;

        var result = await scraper.ScrapeAsync(product.Url);
        if (result == null) return;

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

        if (previousPrice.HasValue && result.Price < previousPrice.Value)
        {
            logger.LogInformation(
                "Price dropped for '{Name}': {OldPrice} → {NewPrice}",
                product.Name, previousPrice.Value, result.Price);

            await SendPriceDropNotificationsAsync(product, previousPrice.Value, result.Price);
        }
        else if (previousPrice.HasValue && previousPrice.Value != result.Price)
        {
            logger.LogInformation(
                "Price changed for '{Name}': {OldPrice} → {NewPrice}",
                product.Name, previousPrice.Value, result.Price);
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
        foreach (var up in userProducts)
        {
            try
            {
                var message = new Message
                {
                    Token = up.User.FcmToken,
                    Notification = new Notification
                    {
                        Title = "Fiyat Düştü! 🎉",
                        Body = $"{product.Name}: {oldPrice:F2}₺ → {newPrice:F2}₺"
                    },
                    Data = new Dictionary<string, string>
                    {
                        ["productId"] = product.Id.ToString(),
                        ["userProductId"] = up.Id.ToString()
                    }
                };
                var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                results.Add(new { userId = up.UserId, status = "sent", response });
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
                }
                else
                {
                    results.Add(new { userId = up.UserId, status = "failed", error = ex.Message });
                }
            }
        }
        return new { matchedUsers = userProducts.Count, results };
    }

    private async Task SendPriceDropNotificationsAsync(Product product, decimal oldPrice, decimal newPrice)
    {
        if (FirebaseApp.DefaultInstance == null) return;

        var userProducts = await db.UserProducts
            .Include(up => up.User)
            .Where(up => up.ProductId == product.Id && up.User.FcmToken != null && up.User.FcmToken != "")
            .ToListAsync();

        foreach (var up in userProducts)
        {
            try
            {
                var message = new Message
                {
                    Token = up.User.FcmToken,
                    Notification = new Notification
                    {
                        Title = "Fiyat Düştü! 🎉",
                        Body = $"{product.Name}: {oldPrice:F2}₺ → {newPrice:F2}₺"
                    },
                    Data = new Dictionary<string, string>
                    {
                        ["productId"] = product.Id.ToString(),
                        ["userProductId"] = up.Id.ToString()
                    }
                };
                await FirebaseMessaging.DefaultInstance.SendAsync(message);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM bildirimi gönderilemedi: userId={UserId}", up.UserId);
                if (ex is FirebaseMessagingException fex &&
                    fex.Message.Contains("missing required authentication credential", StringComparison.OrdinalIgnoreCase))
                {
                    var fallback = await SendViaHttpV1Async(up.User.FcmToken!, product, oldPrice, newPrice, up.Id);
                    if (!fallback.success)
                        logger.LogWarning("FCM fallback da başarısız oldu: userId={UserId}, error={Error}", up.UserId, fallback.error);
                }
            }
        }
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