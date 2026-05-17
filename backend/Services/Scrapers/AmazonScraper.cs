using System.Text.RegularExpressions;
using PriceTracker.Models;

namespace PriceTracker.Services.Scrapers;

public class AmazonScraper(ILogger<AmazonScraper> logger, IHttpClientFactory httpClientFactory)
    : ScraperBase(logger, httpClientFactory)
{
    private static readonly string[] PrimaryPriceMarkers =
    [
        "id=\"corePriceDisplay_desktop_feature_div\"",
        "id=\"corePrice_feature_div\"",
        "id=\"corePrice_desktop\"",
        "id=\"apex_desktop\"",
        "id=\"desktop_buybox\"",
        "id=\"exports_desktop_qualifiedBuybox_priceStrings\""
    ];

    private static readonly string[] SecondaryPriceMarkers =
    [
        "id=\"productTitle\"",
        "id=\"ppd\""
    ];

    public override bool CanHandle(string url) =>
        url.Contains("amazon.com") || url.Contains("amzn.eu") || url.Contains("amzn.to");

    public override async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        // Kısa URL'leri (amzn.eu, amzn.to) gerçek Amazon URL'ine çevir
        if (url.Contains("amzn.eu") || url.Contains("amzn.to"))
        {
            url = await ResolveAmazonShortUrlAsync(url);
        }

        Logger.LogInformation("Amazon: Fetching HTML from {Url}", url);
        var html = await FetchHtmlAsync(url);
        if (html == null) return null;

        if (html.Length < 10_000)
        {
            Logger.LogWarning("Amazon: Sayfa çok kısa ({Len} chars), bot koruması olabilir.", html.Length);
            return null;
        }

        var store = url.Contains("amazon.com.tr") ? "Amazon TR" : "Amazon";

        // 1. JSON-LD (en temiz kaynak)
        var result = TryExtractFromJsonLd(html, url, store);
        if (result != null) return result;

        // 2. Amazon-specific HTML patterns
        return TryExtractFromAmazonHtml(html, url, store);
    }

    private async Task<string> ResolveAmazonShortUrlAsync(string shortUrl)
    {
        try
        {
            var client = HttpClientFactory.CreateClient("Scraper");

            // amzn.eu bazı linklerde HEAD'e 404 döndürüp GET'te doğru yönlendiriyor.
            using var request = new HttpRequestMessage(HttpMethod.Get, shortUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var resolved = response.RequestMessage?.RequestUri?.ToString() ?? shortUrl;

            Logger.LogInformation(
                "Amazon short URL resolved: {Short} -> {Long} (HTTP {Status})",
                shortUrl,
                resolved,
                (int)response.StatusCode);

            return resolved;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Amazon short URL resolve failed, using original URL: {Url}", shortUrl);
            return shortUrl;
        }
    }

    // ── Amazon HTML extraction ────────────────────────────────────────────

    private ScrapeResult? TryExtractFromAmazonHtml(string html, string url, string store)
    {
        try
        {
            var name = ExtractProductName(html);
            var imageUrl = ExtractProductImageUrl(html);
            var price = TryExtractPriceFromCandidates(GetLineWindowsAroundMarkers(html, PrimaryPriceMarkers, beforeLines: 20, afterLines: 220));
            price ??= TryExtractPriceFromCandidates(GetLineWindowsAroundMarkers(html, SecondaryPriceMarkers, beforeLines: 20, afterLines: 260));

            if (price == null)
            {
                if (IsExplicitlyUnavailable(html))
                {
                    Logger.LogInformation("Amazon HTML: ürün stokta yok veya fiyat görünmüyor. {Url}", url);
                    return new ScrapeResult
                    {
                        Name = name ?? "Bilinmeyen Ürün",
                        ImageUrl = imageUrl,
                        Store = store,
                        HasPrice = false,
                        PriceStatus = ProductPriceStatus.OutOfStock
                    };
                }

                Logger.LogWarning("Amazon HTML: fiyat bulunamadı. {Url}", url);
                return new ScrapeResult
                {
                    Name = name ?? "Bilinmeyen Ürün",
                    ImageUrl = imageUrl,
                    Store = store,
                    HasPrice = false,
                    PriceStatus = ProductPriceStatus.PriceNotFound
                };
            }

            Logger.LogInformation("Amazon HTML başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult { Name = name ?? "Bilinmeyen Ürün", Price = price.Value, ImageUrl = imageUrl, Store = store };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Amazon HTML extraction failed for {Url}", url);
            return null;
        }
    }

    private static decimal? TryExtractPriceFromCandidates(IEnumerable<string> candidates)
    {
        foreach (var candidateHtml in candidates)
        {
            var price = TryExtractPriceFromFragment(candidateHtml);
            if (price != null)
            {
                return price;
            }
        }

        return null;
    }

    private static decimal? TryExtractPriceFromFragment(string html)
    {
        var m = Regex.Match(html, @"""priceAmount""\s*:\s*""?([\d.,]+)""?", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return ParsePrice(m.Groups[1].Value);
        }

        foreach (Match offscreenMatch in Regex.Matches(html, @"<span[^>]+class=""a-offscreen""[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase))
        {
            var price = ParsePrice(offscreenMatch.Groups[1].Value);
            if (price is > 0)
            {
                return price;
            }
        }

        m = Regex.Match(html, @"id=""priceblock_(?:ourprice|dealprice|saleprice)""[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return ParsePrice(m.Groups[1].Value);
        }

        m = Regex.Match(html, @"""buyingPrice""\s*:\s*([\d.,]+)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return ParsePrice(m.Groups[1].Value);
        }

        return null;
    }

    private static IEnumerable<string> GetLineWindowsAroundMarkers(string html, IEnumerable<string> markers, int beforeLines, int afterLines)
    {
        var lines = html.Split('\n');
        var seenLineIndexes = new HashSet<int>();

        foreach (var marker in markers)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(marker, StringComparison.OrdinalIgnoreCase) || !seenLineIndexes.Add(i))
                {
                    continue;
                }

                var start = Math.Max(0, i - beforeLines);
                var count = Math.Min(lines.Length - start, beforeLines + afterLines + 1);
                yield return string.Join("\n", lines, start, count);
                break;
            }
        }
    }

    private static bool IsExplicitlyUnavailable(string html)
    {
        return html.Contains("Şu anda mevcut değil", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Currently unavailable", StringComparison.OrdinalIgnoreCase)
            || html.Contains("See all buying options", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Tüm satın alma seçeneklerini gör", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractProductName(string html)
    {
        var m = Regex.Match(html, @"id=""productTitle""[^>]*>\s*([\s\S]*?)\s*</span>", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim();
        }

        m = Regex.Match(html, @"<meta[^>]+property=""og:title""[^>]+content=""([^""]+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractProductImageUrl(string html)
    {
        var m = Regex.Match(html, @"data-a-dynamic-image=""(\{[^""]+})", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var jsonRaw = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            var urlKey = Regex.Match(jsonRaw, @"""(https://[^""]+)""");
            if (urlKey.Success)
            {
                return urlKey.Groups[1].Value;
            }
        }

        m = Regex.Match(html, @"id=""landingImage""[^>]+src=""([^""]+)""", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return m.Groups[1].Value;
        }

        m = Regex.Match(html, @"id=""imgTagWrapper""[\s\S]{0,200}?src=""([^""]+)""", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return m.Groups[1].Value;
        }

        m = Regex.Match(html, @"<meta[^>]+property=""og:image""[^>]+content=""([^""]+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
