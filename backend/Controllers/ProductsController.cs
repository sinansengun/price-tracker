using System.Security.Claims;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using PriceTracker.Services;

namespace PriceTracker.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductsController(
    AppDbContext db,
    IBackgroundJobClient jobClient) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET api/products
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var products = await db.UserProducts
            .Where(up => up.UserId == UserId)
            .Include(up => up.Product).ThenInclude(p => p.PriceHistories)
            .Include(up => up.Labels)
            .OrderByDescending(up => up.AddedAt)
            .Select(up => new
            {
                up.Id,
                up.AlertMode,
                up.DiscountThresholdPercent,
                up.TargetPrice,
                up.AddedAt,
                Product = new
                {
                    up.Product.Id,
                    up.Product.Name,
                    up.Product.Url,
                    up.Product.ImageUrl,
                    up.Product.Store,
                    up.Product.InitialPrice,
                    up.Product.CurrentPrice,
                    up.Product.PriceStatus,
                    up.Product.LastCheckedAt,
                    up.Product.CreatedAt,
                    PriceHistories = up.Product.PriceHistories
                        .OrderByDescending(h => h.CheckedAt)
                        .Take(30)
                        .Select(h => new { h.Price, h.CheckedAt })
                },
                Labels = up.Labels.Select(l => new { l.Id, l.Name, l.Color })
            })
            .ToListAsync();

        return Ok(products);
    }

    // GET api/products/{id}  — id: UserProduct.Id
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var up = await db.UserProducts
            .Where(up => up.Id == id && up.UserId == UserId)
            .Include(up => up.Product).ThenInclude(p => p.PriceHistories.OrderByDescending(h => h.CheckedAt).Take(50))
            .Include(up => up.Labels)
            .Select(up => new
            {
                up.Id,
                up.AlertMode,
                up.DiscountThresholdPercent,
                up.TargetPrice,
                up.AddedAt,
                Product = new
                {
                    up.Product.Id,
                    up.Product.Name,
                    up.Product.Url,
                    up.Product.ImageUrl,
                    up.Product.Store,
                    up.Product.InitialPrice,
                    up.Product.CurrentPrice,
                    up.Product.PriceStatus,
                    up.Product.LastCheckedAt,
                    up.Product.CreatedAt,
                    PriceHistories = up.Product.PriceHistories
                        .OrderByDescending(h => h.CheckedAt)
                        .Take(50)
                        .Select(h => new { h.Price, h.CheckedAt })
                },
                Labels = up.Labels.Select(l => new { l.Id, l.Name, l.Color })
            })
            .FirstOrDefaultAsync();

        if (up == null) return NotFound();
        return Ok(up);
    }

    // POST api/products
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest request)
    {
        var url = request.Url?.Trim();
        if (string.IsNullOrEmpty(url) ||
            (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
             !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { error = "Geçersiz URL. http:// veya https:// ile başlamalıdır." });

        if (!TryBuildAlertSettings(
                request.AlertMode,
                request.DiscountThresholdPercent,
                request.TargetPrice,
                out var alertSettings,
                out var alertError))
        {
            return BadRequest(new { error = alertError });
        }

        // URL'e göre mevcut ürünü bul ya da yeni oluştur (benzersizlik burada sağlanıyor)
        var product = await db.Products.FirstOrDefaultAsync(p => p.Url == url);
        if (product == null)
        {
            product = new Product
            {
                Url = url,
                Name = request.Name ?? string.Empty
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();

            // İlk fiyat çekimi
            jobClient.Enqueue<PriceCheckJob>(j => j.CheckProductAsync(product.Id, null));
        }

        // Kullanıcı bu ürünü zaten takip ediyor mu?
        var existing = await db.UserProducts
            .FirstOrDefaultAsync(up => up.UserId == UserId && up.ProductId == product.Id);
        if (existing != null)
            return Conflict(new { error = "Bu ürünü zaten takip ediyorsunuz.", id = existing.Id });

        var userProduct = new UserProduct
        {
            UserId = UserId,
            ProductId = product.Id,
            AlertMode = alertSettings.AlertMode,
            DiscountThresholdPercent = alertSettings.DiscountThresholdPercent,
            TargetPrice = alertSettings.TargetPrice
        };
        db.UserProducts.Add(userProduct);
        await db.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetById),
            new { id = userProduct.Id },
            new
            {
                id = userProduct.Id,
                message = "Urun eklendi. Ilk fiyat kontrolu baslatildi, fiyat bilgisi kisa sure icinde gorunecek."
            });
    }

    // DELETE api/products/{id}  — id: UserProduct.Id
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var up = await db.UserProducts.FirstOrDefaultAsync(up => up.Id == id && up.UserId == UserId);
        if (up == null) return NotFound();

        db.UserProducts.Remove(up);
        await db.SaveChangesAsync();

        // Başka kullanıcı takip etmiyorsa ürünü de sil
        var trackersLeft = await db.UserProducts.AnyAsync(x => x.ProductId == up.ProductId);
        if (!trackersLeft)
        {
            var product = await db.Products.FindAsync(up.ProductId);
            if (product != null) db.Products.Remove(product);
            await db.SaveChangesAsync();
        }

        return NoContent();
    }

    // PATCH api/products/{id}/target-price
    [HttpPatch("{id:int}/target-price")]
    public async Task<IActionResult> UpdateTargetPrice(int id, [FromBody] UpdateTargetPriceRequest request)
    {
        var wrappedRequest = new UpdateAlertSettingsRequest
        {
            AlertMode = request.TargetPrice.HasValue
                ? UserProductAlertMode.TargetPrice
                : UserProductAlertMode.Automatic,
            TargetPrice = request.TargetPrice
        };

        return await UpdateAlertSettingsInternal(id, wrappedRequest);
    }

    // PATCH api/products/{id}/alert-settings
    [HttpPatch("{id:int}/alert-settings")]
    public async Task<IActionResult> UpdateAlertSettings(int id, [FromBody] UpdateAlertSettingsRequest request)
    {
        return await UpdateAlertSettingsInternal(id, request);
    }

    private async Task<IActionResult> UpdateAlertSettingsInternal(int id, UpdateAlertSettingsRequest request)
    {
        var up = await db.UserProducts.FirstOrDefaultAsync(up => up.Id == id && up.UserId == UserId);
        if (up == null) return NotFound();

        if (!TryBuildAlertSettings(
                request.AlertMode,
                request.DiscountThresholdPercent,
                request.TargetPrice,
                out var alertSettings,
                out var alertError))
        {
            return BadRequest(new { error = alertError });
        }

        ApplyAlertSettings(up, alertSettings);
        await db.SaveChangesAsync();
        return Ok(new
        {
            up.Id,
            up.AlertMode,
            up.DiscountThresholdPercent,
            up.TargetPrice
        });
    }

    // POST api/products/{id}/check
    [HttpPost("{id:int}/check")]
    public async Task<IActionResult> ManualCheck(int id)
    {
        var up = await db.UserProducts.FirstOrDefaultAsync(up => up.Id == id && up.UserId == UserId);
        if (up == null) return NotFound();

        jobClient.Enqueue<PriceCheckJob>(j => j.CheckProductAsync(up.ProductId, null));
        return Accepted(new { message = "Fiyat kontrolü başlatıldı" });
    }

    // GET api/products/{id}/history
    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> GetHistory(int id)
    {
        var up = await db.UserProducts.FirstOrDefaultAsync(up => up.Id == id && up.UserId == UserId);
        if (up == null) return NotFound();

        var history = await db.PriceHistories
            .Where(h => h.ProductId == up.ProductId)
            .OrderByDescending(h => h.CheckedAt)
            .Select(h => new { h.Price, h.CheckedAt })
            .ToListAsync();

        return Ok(history);
    }

    // POST api/products/{id}/labels/{labelId}
    [HttpPost("{id:int}/labels/{labelId:int}")]
    public async Task<IActionResult> AddLabel(int id, int labelId)
    {
        var up = await db.UserProducts
            .Include(up => up.Labels)
            .FirstOrDefaultAsync(up => up.Id == id && up.UserId == UserId);
        if (up == null) return NotFound(new { error = "Ürün bulunamadı." });

        var label = await db.Labels.FirstOrDefaultAsync(l => l.Id == labelId && l.UserId == UserId);
        if (label == null) return NotFound(new { error = "Label bulunamadı." });

        if (!up.Labels.Any(l => l.Id == labelId))
            up.Labels.Add(label);

        await db.SaveChangesAsync();
        return Ok(new { label.Id, label.Name, label.Color });
    }

    // DELETE api/products/{id}/labels/{labelId}
    [HttpDelete("{id:int}/labels/{labelId:int}")]
    public async Task<IActionResult> RemoveLabel(int id, int labelId)
    {
        var up = await db.UserProducts
            .Include(up => up.Labels)
            .FirstOrDefaultAsync(up => up.Id == id && up.UserId == UserId);
        if (up == null) return NotFound();

        var label = up.Labels.FirstOrDefault(l => l.Id == labelId);
        if (label != null)
        {
            up.Labels.Remove(label);
            await db.SaveChangesAsync();
        }

        return NoContent();
    }

    // GET api/products/{id}/debug-html
    [HttpGet("{id:int}/debug-html")]
    public async Task<IActionResult> DebugHtml(int id, [FromServices] ScraperService scraper)
    {
        var up = await db.UserProducts.FirstOrDefaultAsync(up => up.Id == id && up.UserId == UserId);
        if (up == null) return NotFound();

        var product = await db.Products.FindAsync(up.ProductId);
        if (product == null) return NotFound();

        var snippet = await scraper.GetHtmlSnippetForDebugAsync(product.Url);
        return Ok(new { snippet });
    }

    // POST api/products/{id}/test-notification  →  sadece development/test amaçlı
    [HttpPost("{id:int}/test-notification")]
    public async Task<IActionResult> TestNotification(int id, [FromServices] PriceCheckJob priceCheckJob)
    {
        var up = await db.UserProducts
            .Include(up => up.Product)
            .Include(up => up.User)
            .FirstOrDefaultAsync(up => up.Id == id && up.UserId == UserId);
        if (up == null) return NotFound(new { error = "UserProduct bulunamadı", userProductId = id, userId = UserId });

        var product = up.Product;
        var fakeOldPrice = (product.CurrentPrice ?? 100m) * 1.1m;
        var fcmToken = up.User.FcmToken;
        var result = await priceCheckJob.SendTestNotificationAsync(product, fakeOldPrice, product.CurrentPrice ?? 100m);

        return Ok(new { message = $"'{product.Name}' için test bildirimi gönderildi.", fcmTokenExists = fcmToken != null, fcmTokenPreview = fcmToken?[..Math.Min(20, fcmToken.Length)], sendResult = result });
    }

    private static void ApplyAlertSettings(UserProduct userProduct, AlertSettings settings)
    {
        userProduct.AlertMode = settings.AlertMode;
        userProduct.DiscountThresholdPercent = settings.DiscountThresholdPercent;
        userProduct.TargetPrice = settings.TargetPrice;
    }

    private static bool TryBuildAlertSettings(
        string? alertMode,
        decimal? discountThresholdPercent,
        decimal? targetPrice,
        out AlertSettings settings,
        out string error)
    {
        error = string.Empty;
        var normalizedMode = string.IsNullOrWhiteSpace(alertMode)
            ? InferAlertMode(discountThresholdPercent, targetPrice)
            : alertMode.Trim().ToLowerInvariant();

        if (!UserProductAlertMode.IsValid(normalizedMode))
        {
            settings = default!;
            error = "Geçersiz alarm modu.";
            return false;
        }

        switch (normalizedMode)
        {
            case UserProductAlertMode.Automatic:
                settings = new AlertSettings(UserProductAlertMode.Automatic, null, null);
                return true;

            case UserProductAlertMode.Percentage:
                if (!discountThresholdPercent.HasValue)
                {
                    settings = default!;
                    error = "Yüzde alarmı için indirim yüzdesi gerekli.";
                    return false;
                }

                if (discountThresholdPercent <= 0 || discountThresholdPercent > 100)
                {
                    settings = default!;
                    error = "İndirim yüzdesi 0 ile 100 arasında olmalı.";
                    return false;
                }

                settings = new AlertSettings(
                    UserProductAlertMode.Percentage,
                    decimal.Round(discountThresholdPercent.Value, 2),
                    null);
                return true;

            case UserProductAlertMode.TargetPrice:
                if (!targetPrice.HasValue)
                {
                    settings = default!;
                    error = "Hedef fiyat alarmı için fiyat gerekli.";
                    return false;
                }

                if (targetPrice <= 0)
                {
                    settings = default!;
                    error = "Hedef fiyat 0'dan büyük olmalı.";
                    return false;
                }

                settings = new AlertSettings(
                    UserProductAlertMode.TargetPrice,
                    null,
                    decimal.Round(targetPrice.Value, 2));
                return true;

            default:
                settings = default!;
                error = "Geçersiz alarm modu.";
                return false;
        }
    }

    private static string InferAlertMode(decimal? discountThresholdPercent, decimal? targetPrice)
    {
        if (targetPrice.HasValue)
        {
            return UserProductAlertMode.TargetPrice;
        }

        if (discountThresholdPercent.HasValue)
        {
            return UserProductAlertMode.Percentage;
        }

        return UserProductAlertMode.Automatic;
    }

    private sealed record AlertSettings(
        string AlertMode,
        decimal? DiscountThresholdPercent,
        decimal? TargetPrice);
}

public sealed record CreateProductRequest
{
    public string Url { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? AlertMode { get; init; }
    public decimal? DiscountThresholdPercent { get; init; }
    public decimal? TargetPrice { get; init; }
}

public record UpdateTargetPriceRequest(decimal? TargetPrice);
public sealed record UpdateAlertSettingsRequest
{
    public string? AlertMode { get; init; }
    public decimal? DiscountThresholdPercent { get; init; }
    public decimal? TargetPrice { get; init; }
}
