using System.Text.RegularExpressions;

namespace PriceTracker.Services.Scrapers;

/// <summary>
/// aydinsaatstore.com için scraper (IdeaSoft e-ticaret platformu).
/// Strateji sırası:
///   1. JSON-LD (Product @type) — IdeaSoft standart olarak yayar
///   2. Open Graph meta etiketleri (og:price:amount, og:title, og:image)
///   3. itemprop microdata
///   4. IdeaSoft HTML örüntüleri (data-price, .price, /idea/il/ görsel)
/// </summary>
public class AydinSaatScraper(ILogger<AydinSaatScraper> logger, IHttpClientFactory httpClientFactory)
    : ScraperBase(logger, httpClientFactory)
{
    public override bool CanHandle(string url) =>
        url.Contains("aydinsaatstore.com", StringComparison.OrdinalIgnoreCase);

    public override async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        Logger.LogInformation("AydinSaat: Fetching {Url}", url);
        var html = await FetchHtmlAsync(url);
        if (html == null) return null;

        if (html.Length < 5_000)
        {
            Logger.LogWarning("AydinSaat: Sayfa çok kısa ({Len} chars), bot koruması olabilir.", html.Length);
            return null;
        }

        // 1. JSON-LD
        var result = TryExtractFromJsonLd(html, url, "Aydın Saat");
        if (result != null) return result;

        // 2. Open Graph
        result = TryExtractFromOpenGraph(html, url);
        if (result != null) return result;

        // 3. itemprop microdata
        result = TryExtractFromMicrodata(html, url);
        if (result != null) return result;

        // 4. IdeaSoft HTML patterns
        result = TryExtractFromIdeaSoftHtml(html, url);
        if (result != null) return result;

        Logger.LogWarning("AydinSaat: Hiçbir strateji çalışmadı: {Url}", url);
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

            Logger.LogInformation("AydinSaat OG başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult { Name = name ?? "Bilinmeyen Ürün", Price = price.Value, ImageUrl = imageUrl, Store = "Aydın Saat" };
        }
        catch (Exception ex) { Logger.LogDebug(ex, "OG extraction hatası"); return null; }
    }

    // ── itemprop Microdata ────────────────────────────────────────────────

    private ScrapeResult? TryExtractFromMicrodata(string html, string url)
    {
        try
        {
            // content attribute
            var pm = Regex.Match(html,
                @"itemprop=[""']price[""'][^>]+content=[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!pm.Success)
                pm = Regex.Match(html,
                    @"content=[""']([^""']+)[""'][^>]+itemprop=[""']price[""']",
                    RegexOptions.IgnoreCase);

            var price = pm.Success ? ParsePrice(pm.Groups[1].Value) : null;

            // inner text fallback
            if (price == null)
            {
                var im = Regex.Match(html,
                    @"itemprop=[""']price[""'][^>]*>\s*([\d.,\s]+(?:TL|₺)?)\s*<",
                    RegexOptions.IgnoreCase);
                if (im.Success) price = ParsePrice(im.Groups[1].Value);
            }

            if (price == null) return null;

            var nm = Regex.Match(html, @"itemprop=[""']name[""'][^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            var imgm = Regex.Match(html,
                @"itemprop=[""']image[""'][^>]+(?:content|src)=[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);

            Logger.LogInformation("AydinSaat Microdata başarılı: {Name} = {Price}",
                nm.Groups[1].Value.Trim(), price);
            return new ScrapeResult
            {
                Name     = nm.Success ? System.Web.HttpUtility.HtmlDecode(nm.Groups[1].Value.Trim()) : "Bilinmeyen Ürün",
                Price    = price.Value,
                ImageUrl = imgm.Success ? imgm.Groups[1].Value.Trim() : null,
                Store    = "Aydın Saat"
            };
        }
        catch (Exception ex) { Logger.LogDebug(ex, "Microdata extraction hatası"); return null; }
    }

    // ── IdeaSoft HTML Patterns ────────────────────────────────────────────

    private ScrapeResult? TryExtractFromIdeaSoftHtml(string html, string url)
    {
        try
        {
            decimal? price = null;

            // Pattern 1: data-price attribute (IdeaSoft sepete ekle butonunda taşır)
            var m = Regex.Match(html, @"data-price=[""']([\d.,]+)[""']", RegexOptions.IgnoreCase);
            if (m.Success) price = ParsePrice(m.Groups[1].Value);

            // Pattern 2: IdeaSoft'un ürettiği JSON inline — window.__STORE__ veya benzeri
            if (price == null)
            {
                m = Regex.Match(html,
                    @"""price""\s*:\s*""?([\d.,]+)""?",
                    RegexOptions.IgnoreCase);
                if (m.Success) price = ParsePrice(m.Groups[1].Value);
            }

            // Pattern 3: class tabanlı fiyat elementleri
            if (price == null)
            {
                foreach (var pattern in new[]
                {
                    @"class=[""'][^""']*product[_-]?price[^""']*[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                    @"class=[""'][^""']*current[_-]?price[^""']*[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                    @"class=[""'][^""']*fiyat[^""']*[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                    @"class=[""'][^""']*price[^""']*[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                })
                {
                    m = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                    if (m.Success) { price = ParsePrice(m.Groups[1].Value); if (price != null) break; }
                }
            }

            // Pattern 4: "Tavsiye edilen perakende satış fiyatı X TL"
            if (price == null)
            {
                m = Regex.Match(html,
                    @"(?:perakende\s+sat[ışi]+\s+fiyat[ıi]+|tavsiye[^<]{0,60}?fiyat[^<]{0,30}?)[^<]*?([\d]{1,3}(?:[.,]\d{3})*(?:[.,]\d{2})?)\s*(?:TL|₺)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success) price = ParsePrice(m.Groups[1].Value);
            }

            // Pattern 5: Genel TL regex fallback — ilk makul fiyat
            if (price == null)
            {
                foreach (Match pm in Regex.Matches(html,
                    @"([\d]{1,3}(?:\.\d{3})+,\d{2})\s*(?:TL|₺)|(?:₺|TL)\s*([\d]{1,3}(?:\.\d{3})+,\d{2})",
                    RegexOptions.IgnoreCase))
                {
                    var raw = pm.Groups[1].Success ? pm.Groups[1].Value : pm.Groups[2].Value;
                    var candidate = ParsePrice(raw);
                    if (candidate is > 100m) { price = candidate; break; }
                }
            }

            if (price == null) { Logger.LogWarning("AydinSaat HTML: Fiyat bulunamadı."); return null; }

            // ── Ürün Adı ──────────────────────────────────────────────────
            string? name = null;
            var h1 = Regex.Match(html, @"<h1[^>]*>([^<]+)</h1>", RegexOptions.IgnoreCase);
            if (h1.Success)
                name = System.Web.HttpUtility.HtmlDecode(h1.Groups[1].Value.Trim());

            if (string.IsNullOrWhiteSpace(name))
            {
                var title = Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
                if (title.Success)
                    name = System.Web.HttpUtility.HtmlDecode(title.Groups[1].Value.Trim())
                           .Split('|', '-')[0].Trim();
            }

            // ── Görsel ────────────────────────────────────────────────────
            // IdeaSoft pattern: /idea/il/{tenant}/myassets/products/{id}/{filename}
            string? imageUrl = null;
            var imgm = Regex.Match(html,
                @"[""']((?:https?:)?//[^""'\s]+/idea/il/\d+/myassets/products/[^""'\s]+\.(?:png|jpe?g|webp)(?:\?[^""'\s]*)?)[""']",
                RegexOptions.IgnoreCase);
            if (imgm.Success)
            {
                imageUrl = imgm.Groups[1].Value;
                if (imageUrl.StartsWith("//")) imageUrl = "https:" + imageUrl;
            }

            // Fallback: og:image
            if (imageUrl == null)
            {
                var ogImg = Regex.Match(html,
                    @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']",
                    RegexOptions.IgnoreCase);
                if (!ogImg.Success)
                    ogImg = Regex.Match(html,
                        @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']",
                        RegexOptions.IgnoreCase);
                if (ogImg.Success) imageUrl = ogImg.Groups[1].Value.Trim();
            }

            Logger.LogInformation("AydinSaat HTML başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult
            {
                Name     = name ?? "Bilinmeyen Ürün",
                Price    = price.Value,
                ImageUrl = imageUrl,
                Store    = "Aydın Saat"
            };
        }
        catch (Exception ex) { Logger.LogDebug(ex, "IdeaSoft HTML extraction hatası"); return null; }
    }
}
