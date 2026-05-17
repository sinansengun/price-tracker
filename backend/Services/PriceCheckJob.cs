using System.Globalization;
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

        if (!result.HasPrice)
        {
            product.CurrentPrice = null;
            product.PriceStatus = NormalizePriceStatus(result.PriceStatus);
            product.LastCheckedAt = checkedAt;

            UpdateProductMetadata(product, result);

            await db.SaveChangesAsync();

            context?.WriteLine(
                $"Price unavailable for '{product.Name}': status={product.PriceStatus}, previous={(previousPrice?.ToString("F2") ?? "-")} -> -");
            return;
        }

        var lastWeekMinPrice = await db.PriceHistories
            .Where(h => h.ProductId == product.Id && h.CheckedAt >= lastWeekStart)
            .Select(h => (decimal?)h.Price)
            .MinAsync();

        product.CurrentPrice = result.Price;
        product.InitialPrice ??= result.Price;
        product.PriceStatus = ProductPriceStatus.Available;
        product.LastCheckedAt = checkedAt;

        UpdateProductMetadata(product, result);

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

            var triggeredAlerts = await DetermineTriggeredAlertsAsync(
                product,
                previousPrice.Value,
                result.Price,
                referencePrice,
                minDropPercent,
                context);

            if (triggeredAlerts.Count == 0)
            {
                logger.LogInformation(
                    "Price dropped for '{Name}' but no user alert thresholds were met: previous={PreviousPrice}, reference={ReferencePrice}, current={CurrentPrice}",
                    product.Name,
                    previousPrice.Value,
                    referencePrice,
                    result.Price);
                context?.WriteLine(
                    $"Price dropped but no alert thresholds were met: previous={previousPrice.Value:F2}, reference={referencePrice:F2}, current={result.Price:F2}");
                return;
            }

            logger.LogInformation(
                "Price dropped for '{Name}' and triggered {AlertCount} alert(s): previous={PreviousPrice}, reference={ReferencePrice}, current={CurrentPrice}",
                product.Name,
                triggeredAlerts.Count,
                previousPrice.Value,
                referencePrice,
                result.Price);
            context?.WriteLine(
                $"Triggered {triggeredAlerts.Count} alert(s) for '{product.Name}': previous={previousPrice.Value:F2}, reference={referencePrice:F2}, current={result.Price:F2}");

            await SendTriggeredAlertNotificationsAsync(product, triggeredAlerts, context);
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

    private async Task<List<TriggeredAlert>> DetermineTriggeredAlertsAsync(
        Product product,
        decimal previousPrice,
        decimal currentPrice,
        decimal referencePrice,
        decimal automaticThresholdPercent,
        PerformContext? context)
    {
        var userProducts = await db.UserProducts
            .Include(up => up.User)
            .Where(up => up.ProductId == product.Id && up.User.FcmToken != null && up.User.FcmToken != "")
            .ToListAsync();

        if (userProducts.Count == 0)
        {
            context?.WriteLine($"No notification recipients with FCM token for productId={product.Id}");
            return [];
        }

        var alerts = new List<TriggeredAlert>();

        foreach (var userProduct in userProducts)
        {
            var alertMode = NormalizeAlertMode(userProduct.AlertMode);

            switch (alertMode)
            {
                case UserProductAlertMode.Automatic:
                    if (!ShouldTriggerReferenceDropAlert(referencePrice, currentPrice, automaticThresholdPercent, out var automaticDropPercent))
                    {
                        continue;
                    }

                    var automaticContent = BuildPriceDropNotificationContent(product.Name, referencePrice, currentPrice);
                    alerts.Add(new TriggeredAlert(userProduct, automaticContent.title, automaticContent.body, referencePrice, currentPrice));
                    context?.WriteLine(
                        $"Alert matched: userProductId={userProduct.Id}, mode=automatic, drop={automaticDropPercent:F2}%, threshold={automaticThresholdPercent:F2}%");
                    break;

                case UserProductAlertMode.Percentage:
                    if (!userProduct.DiscountThresholdPercent.HasValue)
                    {
                        continue;
                    }

                    if (!ShouldTriggerReferenceDropAlert(
                            referencePrice,
                            currentPrice,
                            userProduct.DiscountThresholdPercent.Value,
                            out var percentageDropPercent))
                    {
                        continue;
                    }

                    var percentageContent = BuildPercentageAlertNotificationContent(
                        product.Name,
                        percentageDropPercent,
                        userProduct.DiscountThresholdPercent.Value,
                        currentPrice);
                    alerts.Add(new TriggeredAlert(userProduct, percentageContent.title, percentageContent.body, referencePrice, currentPrice));
                    context?.WriteLine(
                        $"Alert matched: userProductId={userProduct.Id}, mode=percentage, drop={percentageDropPercent:F2}%, threshold={userProduct.DiscountThresholdPercent.Value:F2}%");
                    break;

                case UserProductAlertMode.TargetPrice:
                    if (!userProduct.TargetPrice.HasValue)
                    {
                        continue;
                    }

                    var targetPrice = userProduct.TargetPrice.Value;
                    if (previousPrice <= targetPrice || currentPrice > targetPrice)
                    {
                        continue;
                    }

                    var targetContent = BuildTargetPriceNotificationContent(product.Name, targetPrice, currentPrice);
                    alerts.Add(new TriggeredAlert(userProduct, targetContent.title, targetContent.body, targetPrice, currentPrice));
                    context?.WriteLine(
                        $"Alert matched: userProductId={userProduct.Id}, mode=target_price, target={targetPrice:F2}, current={currentPrice:F2}");
                    break;
            }
        }

        return alerts;
    }

    private static void UpdateProductMetadata(Product product, ScrapeResult result)
    {
        if (string.IsNullOrEmpty(product.Name) || product.Name == "Bilinmeyen Ürün")
            product.Name = result.Name;

        if (string.IsNullOrEmpty(product.ImageUrl))
            product.ImageUrl = result.ImageUrl;

        if (string.IsNullOrEmpty(product.Store))
            product.Store = result.Store;
    }

    private static string NormalizePriceStatus(string? priceStatus)
    {
        return priceStatus switch
        {
            ProductPriceStatus.OutOfStock => ProductPriceStatus.OutOfStock,
            ProductPriceStatus.PriceNotFound => ProductPriceStatus.PriceNotFound,
            _ => ProductPriceStatus.PriceNotFound
        };
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
        foreach (var up in userProducts)
        {
            var previewAlert = BuildPreviewAlert(up, product.Name, oldPrice, newPrice);

            try
            {
                var message = new Message
                {
                    Token = up.User.FcmToken,
                    Notification = new Notification
                    {
                        Title = previewAlert.Title,
                        Body = previewAlert.Body
                    },
                    Data = BuildNotificationData(product.Id, up.Id)
                };
                var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                results.Add(new { userId = up.UserId, status = "sent", response });
                historyEntries.Add(BuildNotificationHistory(
                    up,
                    product.Id,
                    previewAlert.ReferencePrice,
                    previewAlert.CurrentPrice,
                    previewAlert.Title,
                    previewAlert.Body,
                    true,
                    null));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM bildirimi gönderilemedi: userId={UserId}", up.UserId);
                results.Add(new { userId = up.UserId, status = "failed", error = ex.Message });
                historyEntries.Add(BuildNotificationHistory(
                    up,
                    product.Id,
                    previewAlert.ReferencePrice,
                    previewAlert.CurrentPrice,
                    previewAlert.Title,
                    previewAlert.Body,
                    false,
                    ex.Message));
            }
        }

        if (historyEntries.Count > 0)
        {
            db.NotificationHistories.AddRange(historyEntries);
            await db.SaveChangesAsync();
        }

        return new { matchedUsers = userProducts.Count, results };
    }

    private async Task SendTriggeredAlertNotificationsAsync(
        Product product,
        IReadOnlyCollection<TriggeredAlert> alerts,
        PerformContext? context = null)
    {
        if (FirebaseApp.DefaultInstance == null || alerts.Count == 0) return;

        context?.WriteLine($"Sending notifications: productId={product.Id}, recipients={alerts.Count}");
        var historyEntries = new List<NotificationHistory>();

        foreach (var alert in alerts)
        {
            var up = alert.UserProduct;

            try
            {
                var message = new Message
                {
                    Token = up.User.FcmToken,
                    Notification = new Notification
                    {
                        Title = alert.Title,
                        Body = alert.Body
                    },
                    Data = BuildNotificationData(product.Id, up.Id)
                };
                await FirebaseMessaging.DefaultInstance.SendAsync(message);
                context?.WriteLine($"Notification sent: userId={up.UserId}");
                historyEntries.Add(BuildNotificationHistory(
                    up,
                    product.Id,
                    alert.ReferencePrice,
                    alert.CurrentPrice,
                    alert.Title,
                    alert.Body,
                    true,
                    null));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM bildirimi gönderilemedi: userId={UserId}", up.UserId);
                context?.WriteLine($"Notification failed: userId={up.UserId}, error={ex.Message}");
                historyEntries.Add(BuildNotificationHistory(
                    up,
                    product.Id,
                    alert.ReferencePrice,
                    alert.CurrentPrice,
                    alert.Title,
                    alert.Body,
                    false,
                    ex.Message));
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

        return (
            "Otomatik Alarm: Fiyat Düştü",
            $"{shortName} son 7 günün en düşük fiyatına göre %{dropPercentText} daha ucuz. Güncel fiyat {FormatPrice(newPrice)}.");
    }

    private static (string title, string body) BuildPercentageAlertNotificationContent(
        string? productName,
        decimal dropPercent,
        decimal thresholdPercent,
        decimal currentPrice)
    {
        var shortName = TruncateProductName(productName, 30);
        var dropPercentText = dropPercent.ToString("0.#");
        var thresholdPercentText = thresholdPercent.ToString("0.#");

        return (
            "Yüzde Alarmı Tetiklendi",
            $"{shortName} son 7 günün en düşük fiyatına göre %{dropPercentText} düştü ve %{thresholdPercentText} eşiğini geçti. Güncel fiyat {FormatPrice(currentPrice)}.");
    }

    private static (string title, string body) BuildTargetPriceNotificationContent(
        string? productName,
        decimal targetPrice,
        decimal newPrice)
    {
        var shortName = TruncateProductName(productName, 30);
        var targetPriceText = FormatPrice(targetPrice);
        var newPriceText = FormatPrice(newPrice);

        return (
            "Hedef Fiyat Alarmı Tetiklendi",
            $"{shortName} {targetPriceText} hedefinin altına indi. Güncel fiyat {newPriceText}.");
    }

    private static TriggeredAlert BuildPreviewAlert(
        UserProduct userProduct,
        string? productName,
        decimal referencePrice,
        decimal currentPrice)
    {
        return NormalizeAlertMode(userProduct.AlertMode) switch
        {
            UserProductAlertMode.Percentage when userProduct.DiscountThresholdPercent.HasValue =>
                new TriggeredAlert(
                    userProduct,
                    BuildPercentageAlertNotificationContent(
                        productName,
                        CalculateDropPercent(referencePrice, currentPrice),
                        userProduct.DiscountThresholdPercent.Value,
                        currentPrice).title,
                    BuildPercentageAlertNotificationContent(
                        productName,
                        CalculateDropPercent(referencePrice, currentPrice),
                        userProduct.DiscountThresholdPercent.Value,
                        currentPrice).body,
                    referencePrice,
                    currentPrice),

            UserProductAlertMode.TargetPrice when userProduct.TargetPrice.HasValue =>
                new TriggeredAlert(
                    userProduct,
                    BuildTargetPriceNotificationContent(productName, userProduct.TargetPrice.Value, currentPrice).title,
                    BuildTargetPriceNotificationContent(productName, userProduct.TargetPrice.Value, currentPrice).body,
                    userProduct.TargetPrice.Value,
                    currentPrice),

            _ => new TriggeredAlert(
                userProduct,
                BuildPriceDropNotificationContent(productName, referencePrice, currentPrice).title,
                BuildPriceDropNotificationContent(productName, referencePrice, currentPrice).body,
                referencePrice,
                currentPrice)
        };
    }

    private static string FormatPrice(decimal price)
    {
        return $"{price.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL";
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

    private static bool ShouldTriggerReferenceDropAlert(
        decimal referencePrice,
        decimal currentPrice,
        decimal thresholdPercent,
        out decimal dropPercent)
    {
        dropPercent = CalculateDropPercent(referencePrice, currentPrice);

        return referencePrice > 0
            && currentPrice < referencePrice
            && dropPercent >= thresholdPercent;
    }

    private static string NormalizeAlertMode(string? alertMode)
    {
        var normalized = alertMode?.Trim().ToLowerInvariant() ?? string.Empty;
        return UserProductAlertMode.IsValid(normalized)
            ? normalized
            : UserProductAlertMode.Automatic;
    }

    private sealed record TriggeredAlert(
        UserProduct UserProduct,
        string Title,
        string Body,
        decimal ReferencePrice,
        decimal CurrentPrice);

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