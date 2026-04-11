namespace PriceTracker.Services.Scrapers;

/// <summary>
/// aslansaat.com için scraper.
/// Site standart JSON-LD (Product @type) kullandığından
/// ScraperBase.TryExtractFromJsonLd yeterlidir.
/// </summary>
public class AslanSaatScraper(
    ILogger<AslanSaatScraper> logger,
    IHttpClientFactory httpClientFactory)
    : ScraperBase(logger, httpClientFactory)
{
    public override bool CanHandle(string url) =>
        url.Contains("aslansaat.com", StringComparison.OrdinalIgnoreCase);

    public override async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        Logger.LogInformation("AslanSaat: Fetching {Url}", url);
        var html = await FetchHtmlAsync(url);
        if (html == null) return null;

        var result = TryExtractFromJsonLd(html, url, "AslanSaat");
        if (result != null) return result;

        Logger.LogWarning("AslanSaat: JSON-LD bulunamadı: {Url}", url);
        return null;
    }
}
