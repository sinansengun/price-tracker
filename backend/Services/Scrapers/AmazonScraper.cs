using System.Text.RegularExpressions;

namespace PriceTracker.Services.Scrapers;

public class AmazonScraper(ILogger<AmazonScraper> logger, IHttpClientFactory httpClientFactory)
    : ScraperBase(logger, httpClientFactory)
{
    public override bool CanHandle(string url) =>
        url.Contains("amazon.com") || url.Contains("amzn.eu") || url.Contains("amzn.to");

    public override async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        // Kısa URL'leri (amzn.eu, amzn.to) gerçek Amazon URL'ine çevir
        if (url.Contains("amzn.eu") || url.Contains("amzn.to"))
        {
            var client = HttpClientFactory.CreateClient("Scraper");
            var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead);
            var resolved = resp.RequestMessage?.RequestUri?.ToString() ?? url;
            Logger.LogInformation("Amazon short URL resolved: {Short} → {Long}", url, resolved);
            url = resolved;
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

    // ── Amazon HTML extraction ────────────────────────────────────────────

    private ScrapeResult? TryExtractFromAmazonHtml(string html, string url, string store)
    {
        try
        {
            decimal? price = null;

            // Pattern 1: priceAmount JSON in script tags
            var m = Regex.Match(html, @"""priceAmount""\s*:\s*""?([\d.]+)""?");
            if (m.Success) price = ParsePrice(m.Groups[1].Value);

            // Pattern 2: <span class="a-offscreen"> (screen reader price — most reliable)
            if (price == null)
            {
                foreach (Match om in Regex.Matches(html, @"<span[^>]+class=""a-offscreen""[^>]*>([^<]+)</span>"))
                {
                    var candidate = ParsePrice(om.Groups[1].Value);
                    if (candidate is > 0) { price = candidate; break; }
                }
            }

            // Pattern 3: priceblock_ourprice / priceblock_dealprice (eski sayfalar)
            if (price == null)
            {
                m = Regex.Match(html, @"id=""priceblock_(?:ourprice|dealprice|saleprice)""[^>]*>([^<]+)<");
                if (m.Success) price = ParsePrice(m.Groups[1].Value);
            }

            // Pattern 4: buyingPrice JSON
            if (price == null)
            {
                m = Regex.Match(html, @"""buyingPrice""\s*:\s*([\d.]+)");
                if (m.Success) price = ParsePrice(m.Groups[1].Value);
            }

            if (price == null)
            {
                Logger.LogWarning("Amazon HTML: fiyat bulunamadı. {Url}", url);
                return null;
            }

            // İsim: productTitle span ya da og:title
            string? name = null;
            m = Regex.Match(html, @"id=""productTitle""[^>]*>\s*([\s\S]*?)\s*</span>", RegexOptions.IgnoreCase);
            if (m.Success) name = Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim();

            if (string.IsNullOrEmpty(name))
            {
                m = Regex.Match(html, @"<meta[^>]+property=""og:title""[^>]+content=""([^""]+)""", RegexOptions.IgnoreCase);
                if (m.Success) name = m.Groups[1].Value.Trim();
            }

            // Görsel: birden fazla Amazon pattern'i
            string? imageUrl = null;

            // Pattern 1: data-a-dynamic-image (JSON map url→[w,h])
            m = Regex.Match(html, @"data-a-dynamic-image=""(\{[^""]+})", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var jsonRaw = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
                var urlKey  = Regex.Match(jsonRaw, @"""(https://[^""]+)""");
                if (urlKey.Success) imageUrl = urlKey.Groups[1].Value;
            }

            // Pattern 2: landingImage src
            if (imageUrl == null)
            {
                m = Regex.Match(html, @"id=""landingImage""[^>]+src=""([^""]+)""", RegexOptions.IgnoreCase);
                if (m.Success) imageUrl = m.Groups[1].Value;
            }

            // Pattern 3: imgTagWrapper img src
            if (imageUrl == null)
            {
                m = Regex.Match(html, @"id=""imgTagWrapper""[\s\S]{0,200}?src=""([^""]+)""", RegexOptions.IgnoreCase);
                if (m.Success) imageUrl = m.Groups[1].Value;
            }

            // Pattern 4: og:image
            if (imageUrl == null)
            {
                m = Regex.Match(html, @"<meta[^>]+property=""og:image""[^>]+content=""([^""]+)""", RegexOptions.IgnoreCase);
                if (m.Success) imageUrl = m.Groups[1].Value;
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
}
