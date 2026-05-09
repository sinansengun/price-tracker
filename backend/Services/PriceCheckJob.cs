using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Hangfire.Console;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using Sentry;

namespace PriceTracker.Services;

public class PriceCheckJob(
    AppDbContext db,
    ScraperService scraper,
    FirebaseRemoteConfigService remoteConfigService,
    ILogger<PriceCheckJob> logger)
{
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

        var checkRunId = Guid.NewGuid().ToString("N");
        using var productLogScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["product_id"] = productId,
            ["check_run_id"] = checkRunId,
            ["product_url"] = product.Url
        });

        var result = await scraper.ScrapeAsync(product.Url, productId, checkRunId);
        if (result == null)
        {
            context?.WriteLine($"Scrape failed: productId={productId} url={product.Url}");
            CaptureScrapeFailure(
                productId,
                product.Url,
                checkRunId,
                "no_result",
                "Scrape returned no result.");
            return;
        }

        var checkedAt = DateTime.UtcNow;
        var lastWeekStart = checkedAt.AddDays(-7);
        var previousPrice = product.CurrentPrice;
        var lastWeekMinPrice = await db.PriceHistories
            .Where(h => h.ProductId == product.Id && h.CheckedAt >= lastWeekStart)
            .Select(h => (decimal?)h.Price)
            .MinAsync();

        product.CurrentPrice = result.Price;
        product.InitialPrice ??= result.Price;
        product.LastCheckedAt = checkedAt;

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
            CheckedAt = checkedAt
        });

        await db.SaveChangesAsync();

        context?.WriteLine($"Saved price for '{product.Name}': {(previousPrice?.ToString("F2") ?? "-" )} -> {result.Price:F2}");

        if (previousPrice.HasValue && result.Price < previousPrice.Value)
        {
            var minDropPercent = await remoteConfigService.GetMinPriceDropPercentAsync();
            var referencePrice = lastWeekMinPrice ?? previousPrice.Value;

            if (result.Price >= referencePrice)
            {
                logger.LogInformation(
                    "Price dropped but no notification for '{Name}': current={CurrentPrice}, last7dMin={LastWeekMinPrice}",
                    product.Name,
                    result.Price,
                    referencePrice);
                context?.WriteLine(
                    $"Price dropped but no notification (not below last 7d min): current={result.Price:F2}, last7dMin={referencePrice:F2}");
                return;
            }

            var dropPercent = referencePrice <= 0
                ? 0
                : ((referencePrice - result.Price) / referencePrice) * 100;

            if (dropPercent < minDropPercent)
            {
                logger.LogInformation(
                    "Price drop below threshold for '{Name}' (last 7d min): drop={DropPercent:F2}%, threshold={ThresholdPercent:F2}%",
                    product.Name,
                    dropPercent,
                    minDropPercent);
                context?.WriteLine(
                    $"Price dropped but no notification (below threshold, last 7d min): drop={dropPercent:F2}%, threshold={minDropPercent:F2}%");
                return;
            }

            logger.LogInformation(
                "Price dropped for '{Name}' (last 7d min): {OldPrice} → {NewPrice} (drop={DropPercent:F2}%, threshold={ThresholdPercent:F2}%)",
                product.Name,
                referencePrice,
                result.Price,
                dropPercent,
                minDropPercent);
            context?.WriteLine(
                $"Price dropped for '{product.Name}' (last 7d min): {referencePrice:F2} -> {result.Price:F2} (drop={dropPercent:F2}%, threshold={minDropPercent:F2}%)");

            await SendPriceDropNotificationsAsync(product, referencePrice, result.Price, context);
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
        var (title, body) = BuildPriceDropNotificationContent(product.Name, oldPrice, newPrice);
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
                    Data = BuildNotificationData(product.Id, up.Id)
                };
                var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                results.Add(new { userId = up.UserId, status = "sent", response });
                historyEntries.Add(BuildNotificationHistory(up, product.Id, oldPrice, newPrice, title, body, true, null));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM bildirimi gönderilemedi: userId={UserId}", up.UserId);
                results.Add(new { userId = up.UserId, status = "failed", error = ex.Message });
                historyEntries.Add(BuildNotificationHistory(up, product.Id, oldPrice, newPrice, title, body, false, ex.Message));
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
        var (title, body) = BuildPriceDropNotificationContent(product.Name, oldPrice, newPrice);

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
                    Data = BuildNotificationData(product.Id, up.Id)
                };
                await FirebaseMessaging.DefaultInstance.SendAsync(message);
                context?.WriteLine($"Notification sent: userId={up.UserId}");
                historyEntries.Add(BuildNotificationHistory(up, product.Id, oldPrice, newPrice, title, body, true, null));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM bildirimi gönderilemedi: userId={UserId}", up.UserId);
                context?.WriteLine($"Notification failed: userId={up.UserId}, error={ex.Message}");
                historyEntries.Add(BuildNotificationHistory(up, product.Id, oldPrice, newPrice, title, body, false, ex.Message));
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

    private static Dictionary<string, string> BuildNotificationData(int productId, int userProductId)
    {
        return new Dictionary<string, string>
        {
            ["productId"] = productId.ToString(),
            ["userProductId"] = userProductId.ToString(),
            ["deepLink"] = $"pricetracker://product/{userProductId}",
            ["action"] = "PRICE_ALERT"
        };
    }

    private static (string title, string body) BuildPriceDropNotificationContent(string? productName, decimal oldPrice, decimal newPrice)
    {
        var shortName = TruncateProductName(productName, 30);
        var dropPercent = CalculateDropPercent(oldPrice, newPrice);
        var dropPercentText = dropPercent.ToString("0.#");

        return ("Fiyat Düştü! 🎉", $"{shortName} için en ucuz fiyat geçen haftaya göre %{dropPercentText} düştü. Hemen Price Tracker'da gözat.");
    }

    private static string TruncateProductName(string? productName, int maxLength)
    {
        var name = string.IsNullOrWhiteSpace(productName) ? "Bilinmeyen Ürün" : productName.Trim();

        if (maxLength <= 3 || name.Length <= maxLength)
        {
            return name;
        }

        return $"{name[..(maxLength - 3)]}...";
    }

    private static decimal CalculateDropPercent(decimal oldPrice, decimal newPrice)
    {
        if (oldPrice <= 0 || newPrice >= oldPrice)
        {
            return 0;
        }

        return ((oldPrice - newPrice) / oldPrice) * 100m;
    }

    private static void CaptureScrapeFailure(
        int productId,
        string productUrl,
        string checkRunId,
        string reason,
        string message)
    {
        if (!SentrySdk.IsEnabled)
        {
            return;
        }

        using (SentrySdk.PushScope())
        {
            SentrySdk.ConfigureScope(scope =>
            {
                scope.Level = SentryLevel.Warning;
                scope.SetTag("feature", "scrape");
                scope.SetTag("product_id", productId.ToString());
                scope.SetTag("productId", productId.ToString());
                scope.SetTag("product_url", productUrl);
                scope.SetTag("check_run_id", checkRunId);
                scope.SetTag("reason", reason);
                scope.SetTag("logger", "scrape");
            });

            SentrySdk.CaptureMessage(message, SentryLevel.Warning);
        }
    }
}