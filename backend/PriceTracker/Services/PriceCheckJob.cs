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

        if (previousPrice.HasValue && previousPrice.Value != result.Price)
        {
            logger.LogInformation(
                "Price changed for '{Name}': {OldPrice} → {NewPrice}",
                product.Name, previousPrice.Value, result.Price);
        }
    }
}
