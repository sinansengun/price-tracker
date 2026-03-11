using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using PriceTracker.Services;

namespace PriceTracker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(
    AppDbContext db,
    IBackgroundJobClient jobClient) : ControllerBase
{
    // GET api/products
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var products = await db.Products
            .Include(p => p.Labels)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Url,
                p.ImageUrl,
                p.Store,
                p.InitialPrice,
                p.CurrentPrice,
                p.TargetPrice,
                p.LastCheckedAt,
                p.CreatedAt,
                Labels = p.Labels.Select(l => new { l.Id, l.Name, l.Color }),
                PriceHistories = p.PriceHistories
                    .OrderBy(h => h.CheckedAt)
                    .Select(h => new { h.Price, h.CheckedAt })
            })
            .ToListAsync();

        return Ok(products);
    }

    // GET api/products/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await db.Products
            .Include(p => p.Labels)
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.Name, p.Url, p.ImageUrl, p.Store,
                p.InitialPrice, p.CurrentPrice, p.TargetPrice, p.LastCheckedAt, p.CreatedAt,
                Labels = p.Labels.Select(l => new { l.Id, l.Name, l.Color }),
                PriceHistories = p.PriceHistories
                    .OrderByDescending(h => h.CheckedAt)
                    .Take(50)
                    .Select(h => new { h.Price, h.CheckedAt })
            })
            .FirstOrDefaultAsync();

        if (product == null) return NotFound();
        return Ok(product);
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

        var product = new Product
        {
            Url = url,
            Name = request.Name ?? string.Empty,
            TargetPrice = request.TargetPrice
        };

        db.Products.Add(product);
        await db.SaveChangesAsync();

        // İlk fiyat çekimi için arka planda job başlat
        jobClient.Enqueue<PriceCheckJob>(j => j.CheckProductAsync(product.Id));

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, new { product.Id });
    }

    // DELETE api/products/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await db.Products.FindAsync(id);
        if (product == null) return NotFound();

        db.Products.Remove(product);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // PATCH api/products/{id}/target-price
    [HttpPatch("{id:int}/target-price")]
    public async Task<IActionResult> UpdateTargetPrice(int id, [FromBody] UpdateTargetPriceRequest request)
    {
        var product = await db.Products.FindAsync(id);
        if (product == null) return NotFound();

        product.TargetPrice = request.TargetPrice;
        await db.SaveChangesAsync();
        return Ok(new { product.Id, product.TargetPrice });
    }

    // POST api/products/{id}/check
    [HttpPost("{id:int}/check")]
    public async Task<IActionResult> ManualCheck(int id)
    {
        var product = await db.Products.FindAsync(id);
        if (product == null) return NotFound();

        jobClient.Enqueue<PriceCheckJob>(j => j.CheckProductAsync(id));
        return Accepted(new { message = "Fiyat kontrolü başlatıldı" });
    }

    // GET api/products/{id}/debug-html — geçici debug endpoint
    [HttpGet("{id:int}/debug-html")]
    public async Task<IActionResult> DebugHtml(int id, [FromServices] ScraperService scraper)
    {
        var product = await db.Products.FindAsync(id);
        if (product == null) return NotFound();
        var snippet = await scraper.GetHtmlSnippetForDebugAsync(product.Url);
        return Ok(new { snippet });
    }

    // GET api/products/{id}/history
    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> GetHistory(int id)
    {
        var exists = await db.Products.AnyAsync(p => p.Id == id);
        if (!exists) return NotFound();

        var history = await db.PriceHistories
            .Where(h => h.ProductId == id)
            .OrderByDescending(h => h.CheckedAt)
            .Select(h => new { h.Price, h.CheckedAt })
            .ToListAsync();

        return Ok(history);
    }

    // POST api/products/{id}/labels/{labelId}
    [HttpPost("{id:int}/labels/{labelId:int}")]
    public async Task<IActionResult> AddLabel(int id, int labelId)
    {
        var product = await db.Products.Include(p => p.Labels).FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound(new { error = "Ürün bulunamadı." });

        var label = await db.Labels.FindAsync(labelId);
        if (label == null) return NotFound(new { error = "Label bulunamadı." });

        if (!product.Labels.Any(l => l.Id == labelId))
            product.Labels.Add(label);

        await db.SaveChangesAsync();
        return Ok(new { label.Id, label.Name, label.Color });
    }

    // DELETE api/products/{id}/labels/{labelId}
    [HttpDelete("{id:int}/labels/{labelId:int}")]
    public async Task<IActionResult> RemoveLabel(int id, int labelId)
    {
        var product = await db.Products.Include(p => p.Labels).FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();

        var label = product.Labels.FirstOrDefault(l => l.Id == labelId);
        if (label != null)
        {
            product.Labels.Remove(label);
            await db.SaveChangesAsync();
        }

        return NoContent();
    }
}

public record CreateProductRequest(string Url, string? Name, decimal? TargetPrice);
public record UpdateTargetPriceRequest(decimal? TargetPrice);
