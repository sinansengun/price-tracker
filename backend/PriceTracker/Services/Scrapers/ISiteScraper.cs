namespace PriceTracker.Services.Scrapers;

/// <summary>
/// Her site için uygulanacak scraper arayüzü.
/// </summary>
public interface ISiteScraper
{
    /// <summary>Verilen URL'nin bu scraper tarafından işlenip işlenemeyeceğini belirtir.</summary>
    bool CanHandle(string url);

    /// <summary>Ürün adı, fiyatı ve görsel URL'sini döndürür. Başarısız olursa null.</summary>
    Task<ScrapeResult?> ScrapeAsync(string url);
}
