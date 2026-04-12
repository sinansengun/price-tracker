using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;

namespace PriceTracker.Services;

public class PriceCheckJob(
    AppDbContext db,
    ScraperService scraper,
    ILogger<PriceCheckJob> logger)
{
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

    private async Task SendPriceDropNotificationsAsync(Product product, decimal oldPrice, decimal newPrice)
    {
        if (FirebaseApp.DefaultInstance == null) return;

        var userProducts = await db.UserProducts
            .Include(up => up.User)
            .Where(up => up.ProductId == product.Id && up.User.FcmToken != null)
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
            }
        }
    }
}