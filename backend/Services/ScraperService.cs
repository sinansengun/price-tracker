using System.Text.RegularExpressions;
using PriceTracker.Services.Scrapers;

namespace PriceTracker.Services;

/// <summary>
/// Kayitli ISiteScraper'lari kullanarak URL'den fiyat/�r�n bilgisi �eken orkestrat�r.
/// Yeni bir site eklemek i�in yalnizca yeni bir ISiteScraper implementasyonu olusturup
/// DI'a kaydetmek yeterlidir.
/// </summary>
using Microsoft.Playwright;

public class ScraperService(
    ILogger<ScraperService> logger,
    IEnumerable<ISiteScraper> scrapers,
    IHttpClientFactory httpClientFactory,
    PlaywrightService playwright)
{
    public async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        try
        {
            var scraper = scrapers.FirstOrDefault(s => s.CanHandle(url));
            if (scraper == null)
            {
                logger.LogWarning("Desteklenmeyen URL: {Url}", url);
                return null;
            }
            return await scraper.ScrapeAsync(url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scraping {Url}", url);
            return null;
        }
    }

    //  Debug helper (ge�ici  /api/products/{id}/debug-html endpoint'i i�in) 

    public async Task<string> GetHtmlSnippetForDebugAsync(string url)
    {
        var html = await FetchHtmlForDebugAsync(url);
        if (html == null) return "HTML alinamadi";

        var sb = new System.Text.StringBuilder();

        var nextDataMatch = Regex.Match(html, @"<script[^>]+id=[""']__NEXT_DATA__[""'][^>]*>([\s\S]{0,2000})");
        sb.AppendLine("=== __NEXT_DATA__ (ilk 2000 char) ===");
        sb.AppendLine(nextDataMatch.Success ? nextDataMatch.Groups[1].Value : "YOK");

        var jsonLdMatches = Regex.Matches(html, @"<script[^>]+type=[""']application/ld\+json[""'][^>]*>([\s\S]*?)</script>");
        sb.AppendLine($"\n=== JSON-LD script taglari ({jsonLdMatches.Count} adet) ===");
        foreach (Match m in jsonLdMatches)
            sb.AppendLine(m.Groups[1].Value[..Math.Min(500, m.Groups[1].Value.Length)]);

        var stateMatch = Regex.Match(html, @"window\.__(?:INITIAL_STATE__|REDUX_STATE__|APP_STATE__|STATE__)[^=]*=\s*(\{[\s\S]{0,1000})");
        sb.AppendLine("\n=== window state (ilk 1000 char) ===");
        sb.AppendLine(stateMatch.Success ? stateMatch.Groups[1].Value : "YOK");

        var priceLines = html.Split('\n').Where(l =>
            (l.Contains("fiyat", StringComparison.OrdinalIgnoreCase) ||
             l.Contains("\"price\"") || l.Contains("'price'") ||
             l.Contains("salePrice") || l.Contains("currentPrice") ||
             Regex.IsMatch(l, @"\d{3,5}[.,]\d{2}"))
            && l.Trim().Length < 300).Take(15);
        sb.AppendLine("\n=== price/fiyat ge�en satirlar (ilk 15) ===");
        foreach (var l in priceLines) sb.AppendLine(l.Trim());

        var inlineScripts = Regex.Matches(html, @"<script(?![^>]+src)[^>]*>([\s\S]*?)</script>")
            .Cast<Match>().OrderByDescending(m => m.Groups[1].Length).Take(5);
        sb.AppendLine("\n=== En b�y�k inline script'ler (ilk 100 char) ===");
        foreach (var s in inlineScripts)
            sb.AppendLine($"[{s.Groups[1].Length} chars] {s.Groups[1].Value.Trim()[..Math.Min(100, s.Groups[1].Value.Trim().Length)]}");

        return sb.ToString();
    }

    private async Task<string?> FetchHtmlForDebugAsync(string url)
    {
        var client = httpClientFactory.CreateClient("Scraper");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");

        using var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync();

        // Fallback: HTTP ile alınamadıysa Playwright ile render edip dene
        try
        {
            var rendered = await playwright.FetchHtmlAsync(url, WaitUntilState.NetworkIdle);
            if (!string.IsNullOrWhiteSpace(rendered)) return rendered;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Playwright fallback failed for debug HTML: {Url}", url);
        }

        return null;
    }
}
