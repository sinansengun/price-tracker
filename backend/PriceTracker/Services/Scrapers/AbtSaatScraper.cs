using System.Text.RegularExpressions;
using PriceTracker.Services;

namespace PriceTracker.Services.Scrapers;

/// <summary>
/// abtsaat.com için scraper (Kobimaster ASP.NET platformu).
/// Bot/TLS korumasını aşmak için Playwright (gerçek Chromium) kullanır.
/// Strateji sırası:
///   1. JSON-LD  (Product @type)
///   2. Open Graph meta etiketleri
///   3. itemprop="price" mikroveri
///   4. Kobimaster/ASP.NET HTML örüntüleri (id/class tabanlı)
/// </summary>
public class AbtSaatScraper(
    ILogger<AbtSaatScraper> logger,
    IHttpClientFactory httpClientFactory,
    PlaywrightService playwright)
    : ScraperBase(logger, httpClientFactory)
{
    public override bool CanHandle(string url) =>
        url.Contains("abtsaat.com", StringComparison.OrdinalIgnoreCase);

    public override async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        Logger.LogInformation("AbtSaat: Fetching {Url}", url);
        var html = await playwright.FetchHtmlAsync(url);
        if (html == null) return null;

        if (html.Length < 5_000)
        {
            Logger.LogWarning("AbtSaat: Sayfa çok kısa ({Len} chars), bot koruması olabilir.", html.Length);
            return null;
        }

        // 1. JSON-LD
        var result = TryExtractFromJsonLd(html, url, "AbtSaat");
        if (result != null) return result;

        // 2. Open Graph meta tags
        result = TryExtractFromOpenGraph(html, url);
        if (result != null) return result;

        // 3. itemprop microdata
        result = TryExtractFromMicrodata(html, url);
        if (result != null) return result;

        // 4. Kobimaster platform HTML patterns
        result = TryExtractFromKobimasterHtml(html, url);
        if (result != null) return result;

        Logger.LogWarning("AbtSaat: Hiçbir strateji çalışmadı: {Url}", url);
        return null;
    }

    // ── Open Graph ────────────────────────────────────────────────────────

    private ScrapeResult? TryExtractFromOpenGraph(string html, string url)
    {
        try
        {
            // og:title
            var titleMatch = Regex.Match(html,
                @"<meta[^>]+property=[""']og:title[""'][^>]+content=[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!titleMatch.Success)
                titleMatch = Regex.Match(html,
                    @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:title[""']",
                    RegexOptions.IgnoreCase);

            var name = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : null;

            // og:price:amount  (bazı siteler bunu ekler)
            var priceMatch = Regex.Match(html,
                @"<meta[^>]+property=[""']og:price:amount[""'][^>]+content=[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!priceMatch.Success)
                priceMatch = Regex.Match(html,
                    @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:price:amount[""']",
                    RegexOptions.IgnoreCase);

            var price = priceMatch.Success ? ParsePrice(priceMatch.Groups[1].Value) : null;
            if (price == null) return null;

            // og:image
            var imgMatch = Regex.Match(html,
                @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!imgMatch.Success)
                imgMatch = Regex.Match(html,
                    @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:image[""']",
                    RegexOptions.IgnoreCase);

            var imageUrl = imgMatch.Success ? imgMatch.Groups[1].Value.Trim() : null;

            Logger.LogInformation("AbtSaat OG başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult
            {
                Name     = name ?? "Bilinmeyen Ürün",
                Price    = price.Value,
                ImageUrl = imageUrl,
                Store    = "AbtSaat"
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "OG extraction hatası");
            return null;
        }
    }

    // ── itemprop Microdata ────────────────────────────────────────────────

    private ScrapeResult? TryExtractFromMicrodata(string html, string url)
    {
        try
        {
            // <span itemprop="price" content="70040.00"> veya >70.040,00 TL<
            var priceMatch = Regex.Match(html,
                @"itemprop=[""']price[""'][^>]+content=[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!priceMatch.Success)
                priceMatch = Regex.Match(html,
                    @"content=[""']([^""']+)[""'][^>]+itemprop=[""']price[""']",
                    RegexOptions.IgnoreCase);

            decimal? price = null;
            if (priceMatch.Success)
                price = ParsePrice(priceMatch.Groups[1].Value);

            // Eğer content attribute'u yoksa inner text'ten dene
            if (price == null)
            {
                var innerMatch = Regex.Match(html,
                    @"itemprop=[""']price[""'][^>]*>\s*([\d.,\s]+(?:TL|₺)?)\s*<",
                    RegexOptions.IgnoreCase);
                if (innerMatch.Success)
                    price = ParsePrice(innerMatch.Groups[1].Value);
            }

            if (price == null) return null;

            // itemprop="name"
            var nameMatch = Regex.Match(html,
                @"itemprop=[""']name[""'][^>]*>([^<]+)<",
                RegexOptions.IgnoreCase);
            var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : null;

            // itemprop="image"
            var imgMatch = Regex.Match(html,
                @"itemprop=[""']image[""'][^>]+(?:content|src)=[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            var imageUrl = imgMatch.Success ? imgMatch.Groups[1].Value.Trim() : null;

            Logger.LogInformation("AbtSaat Microdata başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult
            {
                Name     = name ?? "Bilinmeyen Ürün",
                Price    = price.Value,
                ImageUrl = imageUrl,
                Store    = "AbtSaat"
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Microdata extraction hatası");
            return null;
        }
    }

    // ── Kobimaster HTML Patterns ──────────────────────────────────────────

    private ScrapeResult? TryExtractFromKobimasterHtml(string html, string url)
    {
        try
        {
            decimal? price = null;
            string? name   = null;
            string? imageUrl = null;

            // ── Fiyat ─────────────────────────────────────────────────────

            // Pattern 1: id="productPrice" veya id="lblPrice" gibi Kobimaster id'leri
            foreach (var idPattern in new[]
            {
                @"id=[""']productPrice[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                @"id=[""']lblPrice[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                @"id=[""']price[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                @"id=[""']ProductPrice[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
            })
            {
                var m = Regex.Match(html, idPattern, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    price = ParsePrice(m.Groups[1].Value);
                    if (price != null) break;
                }
            }

            // Pattern 2: class tabanlı Kobimaster/genel e-ticaret pattern'leri
            if (price == null)
            {
                foreach (var classPattern in new[]
                {
                    @"class=[""'][^""']*product[-_]?price[^""']*[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                    @"class=[""'][^""']*current[-_]?price[^""']*[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                    @"class=[""'][^""']*sale[-_]?price[^""']*[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                    @"class=[""'][^""']*fiyat[^""']*[""'][^>]*>([\d.,\s]+(?:TL|₺)?)",
                })
                {
                    var m = Regex.Match(html, classPattern, RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        price = ParsePrice(m.Groups[1].Value);
                        if (price != null) break;
                    }
                }
            }

            // Pattern 3: data-price attribute
            if (price == null)
            {
                var m = Regex.Match(html, @"data-price=[""']([\d.,]+)[""']", RegexOptions.IgnoreCase);
                if (m.Success) price = ParsePrice(m.Groups[1].Value);
            }

            // Pattern 4: Fiyatın genellikle Kobimaster'da görünen TL içeren span/div — ham fallback
            // Fiyatı TL/₺ ile sonlanan veya başlayan sayısal value olarak ara
            if (price == null)
            {
                // Türkçe fiyat formatı: 1.234,56 TL veya ₺1.234,56
                var priceMatches = Regex.Matches(html,
                    @"(?:₺|TL)\s*([\d]{1,3}(?:\.\d{3})*(?:,\d{2})?)|" +
                    @"([\d]{1,3}(?:\.\d{3})*(?:,\d{2})?)\s*(?:TL|₺)",
                    RegexOptions.IgnoreCase);

                decimal? bestPrice = null;
                foreach (Match m in priceMatches)
                {
                    var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    var candidate = ParsePrice(raw);
                    // Makul fiyat aralığı: saatler için >100 TL
                    if (candidate is > 100m)
                    {
                        bestPrice = candidate;
                        break;
                    }
                }
                price = bestPrice;
            }

            if (price == null)
            {
                Logger.LogWarning("AbtSaat HTML: Fiyat bulunamadı.");
                return null;
            }

            // ── Ürün Adı ──────────────────────────────────────────────────

            // H1 etiketi (ürün sayfasında genellikle ürün adıdır)
            var h1Match = Regex.Match(html, @"<h1[^>]*>([^<]+)</h1>", RegexOptions.IgnoreCase);
            if (h1Match.Success)
                name = System.Web.HttpUtility.HtmlDecode(h1Match.Groups[1].Value.Trim());

            // Fallback: <title>
            if (string.IsNullOrWhiteSpace(name))
            {
                var titleMatch = Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
                if (titleMatch.Success)
                    name = System.Web.HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim())
                           .Split('|', '-')[0].Trim();
            }

            // ── Görsel ────────────────────────────────────────────────────

            // Kobimaster pattern: /productimages/{id}/big/{filename}
            var imgMatch = Regex.Match(html,
                @"[""'](/productimages/\d+/big/[^""'\s]+)[""']",
                RegexOptions.IgnoreCase);
            if (imgMatch.Success)
                imageUrl = "https://www.abtsaat.com" + imgMatch.Groups[1].Value;

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
                if (ogImg.Success)
                    imageUrl = ogImg.Groups[1].Value.Trim();
            }

            Logger.LogInformation("AbtSaat HTML başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult
            {
                Name     = name ?? "Bilinmeyen Ürün",
                Price    = price.Value,
                ImageUrl = imageUrl,
                Store    = "AbtSaat"
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Kobimaster HTML extraction hatası");
            return null;
        }
    }
}
