using System.Text.RegularExpressions;

namespace PriceTracker.Services.Scrapers;

/// <summary>
/// edipsaat.com için scraper (Next.js tabanlı).
/// Strateji sırası:
///   1. JSON-LD (Product @type)
///   2. Open Graph meta etiketleri
///   3. Next.js __NEXT_DATA__ inline JSON
///   4. HTML regex (fiyat span)
/// </summary>
public class EdipSaatScraper(ILogger<EdipSaatScraper> logger, IHttpClientFactory httpClientFactory)
    : ScraperBase(logger, httpClientFactory)
{
    public override bool CanHandle(string url) =>
        url.Contains("edipsaat.com", StringComparison.OrdinalIgnoreCase);

    public override async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        Logger.LogInformation("EdipSaat: Fetching {Url}", url);
        var html = await FetchHtmlAsync(url);
        if (html == null) return null;

        if (html.Length < 5_000)
        {
            Logger.LogWarning("EdipSaat: Sayfa çok kısa ({Len} chars), bot koruması olabilir.", html.Length);
            return null;
        }

        // 1. JSON-LD
        var result = TryExtractFromJsonLd(html, url, "Edip Saat");
        if (result != null) return result;

        // 2. Open Graph
        result = TryExtractFromOpenGraph(html, url);
        if (result != null) return result;

        // 3. __NEXT_DATA__ inline JSON
        result = TryExtractFromNextData(html, url);
        if (result != null) return result;

        // 4. HTML regex
        result = TryExtractFromHtml(html, url);
        if (result != null) return result;

        Logger.LogWarning("EdipSaat: Hiçbir strateji çalışmadı: {Url}", url);
        return null;
    }

    // ── Open Graph ────────────────────────────────────────────────────────

    private ScrapeResult? TryExtractFromOpenGraph(string html, string url)
    {
        try
        {
            string? GetMeta(string property)
            {
                var m = Regex.Match(html,
                    $@"<meta[^>]+property=[""']{Regex.Escape(property)}[""'][^>]+content=[""']([^""']+)[""']",
                    RegexOptions.IgnoreCase);
                if (!m.Success)
                    m = Regex.Match(html,
                        $@"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']{Regex.Escape(property)}[""']",
                        RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }

            var priceRaw = GetMeta("og:price:amount") ?? GetMeta("product:price:amount");
            var price    = ParsePrice(priceRaw);
            if (price == null) return null;

            var name     = GetMeta("og:title");
            var imageUrl = GetMeta("og:image");

            Logger.LogInformation("EdipSaat OG başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult { Name = name ?? "Bilinmeyen Ürün", Price = price.Value, ImageUrl = imageUrl, Store = "Edip Saat" };
        }
        catch (Exception ex) { Logger.LogDebug(ex, "EdipSaat OG extraction hatası"); return null; }
    }

    // ── Next.js __NEXT_DATA__ ─────────────────────────────────────────────

    private ScrapeResult? TryExtractFromNextData(string html, string url)
    {
        try
        {
            var m = Regex.Match(html,
                @"<script[^>]+id=[""']__NEXT_DATA__[""'][^>]*>([\s\S]*?)</script>",
                RegexOptions.IgnoreCase);
            if (!m.Success) return null;

            var json = m.Groups[1].Value;
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // pageProps.product veya pageProps.data.product yollarını dene
            System.Text.Json.JsonElement product = default;
            bool found = false;

            if (root.TryGetProperty("props", out var props) &&
                props.TryGetProperty("pageProps", out var pageProps))
            {
                if (pageProps.TryGetProperty("product", out product)) found = true;
                else if (pageProps.TryGetProperty("data", out var data) &&
                         data.TryGetProperty("product", out product)) found = true;
            }

            if (!found) return null;

            string? name     = product.TryGetProperty("name", out var nameEl)     ? nameEl.GetString()     : null;
            string? imageUrl = product.TryGetProperty("image", out var imageEl)   ? imageEl.GetString()    : null;

            decimal? price = null;
            foreach (var key in new[] { "specialPrice", "finalPrice", "price", "salePrice" })
            {
                if (product.TryGetProperty(key, out var priceEl))
                {
                    price = ParsePrice(priceEl.ToString());
                    if (price != null) break;
                }
            }

            if (price == null) return null;

            Logger.LogInformation("EdipSaat __NEXT_DATA__ başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult { Name = name ?? "Bilinmeyen Ürün", Price = price.Value, ImageUrl = imageUrl, Store = "Edip Saat" };
        }
        catch (Exception ex) { Logger.LogDebug(ex, "EdipSaat __NEXT_DATA__ extraction hatası"); return null; }
    }

    // ── HTML regex ────────────────────────────────────────────────────────

    private ScrapeResult? TryExtractFromHtml(string html, string url)
    {
        try
        {
            // İndirimli fiyat önce, yoksa normal fiyat
            // Örnek: ₺27.861,30 veya 27.861,30 ₺
            var pricePatterns = new[]
            {
                @"[""']discountedPrice[""']\s*:\s*[""']?([\d.,]+)[""']?",
                @"[""']salePrice[""']\s*:\s*[""']?([\d.,]+)[""']?",
                @"[""']price[""']\s*:\s*[""']?([\d.,]+)[""']?",
                @"₺\s*([\d.]+,\d{2})",
                @"([\d.]+,\d{2})\s*₺"
            };

            decimal? price = null;
            foreach (var pattern in pricePatterns)
            {
                var m = Regex.Match(html, pattern);
                if (m.Success)
                {
                    price = ParsePrice(m.Groups[1].Value);
                    if (price != null) break;
                }
            }

            if (price == null) return null;

            // Ürün adı: og:title veya <title>
            string? name = null;
            var titleMeta = Regex.Match(html,
                @"<meta[^>]+name=[""']title[""'][^>]+content=[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (titleMeta.Success)
                name = titleMeta.Groups[1].Value.Trim();

            if (string.IsNullOrEmpty(name))
            {
                var titleTag = Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
                if (titleTag.Success)
                    name = titleTag.Groups[1].Value.Trim().Split('|')[0].Trim();
            }

            // Görsel: be.edipsaat.com CDN URL'leri
            string? imageUrl = null;
            var imgM = Regex.Match(html,
                @"https://be\.edipsaat\.com/media/catalog/product[^""' )]+\.jpg",
                RegexOptions.IgnoreCase);
            if (imgM.Success) imageUrl = imgM.Value;

            Logger.LogInformation("EdipSaat HTML regex başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult { Name = name ?? "Bilinmeyen Ürün", Price = price.Value, ImageUrl = imageUrl, Store = "Edip Saat" };
        }
        catch (Exception ex) { Logger.LogDebug(ex, "EdipSaat HTML regex extraction hatası"); return null; }
    }
}
