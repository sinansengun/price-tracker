using System.Text.Json;
using System.Text.RegularExpressions;

namespace PriceTracker.Services.Scrapers;

public class HepsiburadaScraper(ILogger<HepsiburadaScraper> logger, IHttpClientFactory httpClientFactory)
    : ScraperBase(logger, httpClientFactory)
{
    public override bool CanHandle(string url) => url.Contains("hepsiburada.com");

    public override async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        Logger.LogInformation("Hepsiburada: Fetching HTML from {Url}", url);
        var html = await FetchHtmlAsync(url);
        if (html == null) return null;

        if (html.Length < 10_000)
        {
            Logger.LogWarning("Sayfa çok kısa ({Len} chars), bot koruması olabilir.", html.Length);
            return null;
        }

        if (html.Length < 50_000)
        {
            var titleMatch = Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
            var title = titleMatch.Success ? titleMatch.Groups[1].Value : "";
            if (title.Contains("güvenlik",  StringComparison.OrdinalIgnoreCase) ||
                title.Contains("security",  StringComparison.OrdinalIgnoreCase) ||
                title.Contains("captcha",   StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogWarning("Bot koruması sayfası tespit edildi. Title: {Title}", title);
                return null;
            }
        }

        // 1. Inline Redux state (__NEXT_DATA__ veya accountState)
        var result = TryExtractFromNextData(html, url);
        if (result != null) return result;

        // 2. JSON-LD
        result = TryExtractFromJsonLd(html, url, "Hepsiburada");
        if (result != null) return result;

        // 3. Listing API (fiyat AJAX ile yükleniyor olabilir)
        result = await TryListingApiAsync(html, url);
        if (result != null) return result;

        // 4. HTML meta/data attributes (fallback)
        return TryExtractFromHtml(html, url);
    }

    // ── Extractors ────────────────────────────────────────────────────────

    private ScrapeResult? TryExtractFromNextData(string html, string url)
    {
        try
        {
            var match = Regex.Match(html, @"<script[^>]+id=[""']__NEXT_DATA__[""'][^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(html, @"<script[^>]*>\s*(\{""accountState""[\s\S]*?)\s*</script>", RegexOptions.IgnoreCase);

            Logger.LogInformation("Redux/NextData regex match: {Success} | Group1 length: {Len}",
                match.Success, match.Success ? match.Groups[1].Length : 0);

            if (!match.Success) return null;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(match.Groups[1].Value); }
            catch (Exception ex) { Logger.LogWarning(ex, "Redux JSON parse hatası"); return null; }

            using (doc)
            {
                var root = doc.RootElement;
                Logger.LogInformation("Redux JSON parsed. Top-level keys: {Keys}",
                    string.Join(", ", root.EnumerateObject().Take(5).Select(p => p.Name)));

                if (!root.TryGetProperty("productState", out var productState))
                { Logger.LogWarning("productState bulunamadı"); return null; }

                if (!productState.TryGetProperty("product", out var data))
                { Logger.LogWarning("productState.product bulunamadı"); return null; }

                Logger.LogInformation("productState.product keys: {Keys}",
                    string.Join(", ", data.EnumerateObject().Select(p => p.Name)));

                decimal? price = ExtractPriceFromReduxProduct(data);
                if (price == null) return null;

                string? name = null;
                foreach (var key in new[] { "name", "displayName", "productName", "catalogName", "title" })
                {
                    if (data.TryGetProperty(key, out var nEl) && nEl.ValueKind == JsonValueKind.String)
                    { name = nEl.GetString(); if (!string.IsNullOrEmpty(name)) break; }
                }

                string? imageUrl = null;
                if (data.TryGetProperty("media", out var mediaArr) && mediaArr.ValueKind == JsonValueKind.Array && mediaArr.GetArrayLength() > 0)
                {
                    foreach (var item in mediaArr.EnumerateArray())
                    {
                        if (item.TryGetProperty("url", out var urlEl))
                        { imageUrl = urlEl.GetString(); break; }
                    }
                }
                if (imageUrl == null)
                {
                    foreach (var key in new[] { "images", "imageUrls" })
                    {
                        if (data.TryGetProperty(key, out var imgEl) && imgEl.ValueKind == JsonValueKind.Array && imgEl.GetArrayLength() > 0)
                        { imageUrl = imgEl[0].GetString(); break; }
                    }
                }

                Logger.LogInformation("Redux state başarılı: {Name} = {Price}", name, price);
                return new ScrapeResult { Name = name ?? "Bilinmeyen Ürün", Price = price.Value, ImageUrl = imageUrl, Store = "Hepsiburada" };
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Redux state extraction failed for {Url}", url);
            return null;
        }
    }

    private static decimal? ExtractPriceFromReduxProduct(JsonElement data)
    {
        // prices array: [{"value": 4999.9, "formattedPrice": "4.999,90 TL", ...}]
        if (data.TryGetProperty("prices", out var pricesArr) &&
            pricesArr.ValueKind == JsonValueKind.Array && pricesArr.GetArrayLength() > 0)
        {
            var first = pricesArr[0];
            if (first.TryGetProperty("value", out var valEl))
            {
                var price = ParsePrice(valEl.ToString());
                if (price != null) return price;
            }
            if (first.TryGetProperty("formattedPrice", out var fmtEl))
            {
                var price = ParsePrice(fmtEl.GetString());
                if (price != null) return price;
            }
        }

        foreach (var key in new[] { "unitPrice", "price", "salePrice", "currentPrice", "lowestPrice" })
        {
            if (data.TryGetProperty(key, out var prEl))
            {
                var price = ParsePrice(prEl.ToString());
                if (price != null) return price;
            }
        }

        if (data.TryGetProperty("listings", out var listings) &&
            listings.ValueKind == JsonValueKind.Array && listings.GetArrayLength() > 0)
        {
            var first = listings[0];
            foreach (var key in new[] { "price", "salePrice", "unitPrice" })
            {
                if (first.TryGetProperty(key, out var prEl))
                {
                    var price = ParsePrice(prEl.ToString());
                    if (price != null) return price;
                }
            }
        }

        return null;
    }

    private async Task<ScrapeResult?> TryListingApiAsync(string pageHtml, string url)
    {
        try
        {
            // SKU'yu önce JSON-LD'den, bulamazsa URL'den al
            var skuMatch = Regex.Match(pageHtml, @"""sku""\s*:\s*""([^""]+)""");
            if (!skuMatch.Success)
                skuMatch = Regex.Match(url, @"-p(?:m)?-([A-Z0-9]+)(?:[?#]|$)");
            if (!skuMatch.Success) return null;

            var sku = skuMatch.Groups[1].Value;
            Logger.LogInformation("Listing API deneniyor, SKU: {Sku}", sku);

            var client = httpClientFactory.CreateClient("Scraper");
            var apiUrl = $"https://www.hepsiburada.com/api/listing/merchantlisting/allmerchants/sku/{sku}";

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.TryAddWithoutValidation("User-Agent",        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept",            "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Accept-Language",   "tr-TR,tr;q=0.9");
            request.Headers.TryAddWithoutValidation("Origin",            "https://www.hepsiburada.com");
            request.Headers.TryAddWithoutValidation("Referer",           url);
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode",    "cors");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site",    "same-origin");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest",    "empty");
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Logger.LogInformation("Listing API HTTP {Status} | {Sku}", (int)response.StatusCode, sku);

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("Listing API başarısız: HTTP {Status}. Body: {Body}",
                    (int)response.StatusCode, body[..Math.Min(300, body.Length)]);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var data = root;
            foreach (var key in new[] { "result", "data", "listing", "product" })
            {
                if (root.TryGetProperty(key, out var el)) { data = el; break; }
            }

            decimal? price = null;
            foreach (var key in new[] { "price", "salePrice", "unitPrice", "lowestPrice", "currentPrice" })
            {
                if (data.TryGetProperty(key, out var prEl))
                {
                    price = ParsePrice(prEl.ToString());
                    if (price != null) break;
                }
            }

            if (price == null)
            {
                Logger.LogWarning("Listing API: fiyat bulunamadı. Body: {Body}", body[..Math.Min(500, body.Length)]);
                return null;
            }

            string? name = null;
            foreach (var key in new[] { "name", "displayName", "productName" })
            {
                if (data.TryGetProperty(key, out var nEl) && nEl.ValueKind == JsonValueKind.String)
                { name = nEl.GetString(); if (!string.IsNullOrEmpty(name)) break; }
            }
            if (string.IsNullOrEmpty(name))
            {
                var ogTitle = Regex.Match(pageHtml, @"<meta[^>]+property=[""']og:title[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                name = ogTitle.Success ? ogTitle.Groups[1].Value.Trim() : null;
            }

            string? imageUrl = null;
            var ogImg = Regex.Match(pageHtml, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (ogImg.Success) imageUrl = ogImg.Groups[1].Value;

            Logger.LogInformation("Listing API başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult { Name = name ?? "Bilinmeyen Ürün", Price = price.Value, ImageUrl = imageUrl, Store = "Hepsiburada" };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Listing API hatası: {Url}", url);
            return null;
        }
    }

    private ScrapeResult? TryExtractFromHtml(string html, string url)
    {
        try
        {
            var pricePatterns = new[]
            {
                @"data-price=[""']([0-9.,]+)[""']",
                @"itemprop=[""']price[""'][^>]*content=[""']([0-9.,]+)[""']",
                @"content=[""']([0-9.,]+)[""'][^>]*itemprop=[""']price[""']",
                @"""currentPrice""\s*:\s*([0-9.,]+)",
                @"""salePrice""\s*:\s*([0-9.,]+)",
                @"""price""\s*:\s*([0-9.,]+)",
            };

            decimal? price = null;
            foreach (var pattern in pricePatterns)
            {
                var m = Regex.Match(html, pattern);
                if (!m.Success) continue;
                price = ParsePrice(m.Groups[1].Value);
                if (price != null) break;
            }

            var ogTitleMatch = Regex.Match(html, @"<meta[^>]+property=[""']og:title[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            string? name = ogTitleMatch.Success ? ogTitleMatch.Groups[1].Value.Trim() : null;

            if (string.IsNullOrEmpty(name))
            {
                var titleMatch = Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
                name = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : null;
                if (name != null)
                {
                    var pipeIdx = name.LastIndexOf('|');
                    if (pipeIdx > 0) name = name[..pipeIdx].Trim();
                }
            }

            var imgMatch = Regex.Match(html, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            var imageUrl = imgMatch.Success ? imgMatch.Groups[1].Value : null;

            if (price == null)
            {
                Logger.LogWarning("HTML fallback: fiyat bulunamadı. {Url}", url);
                return null;
            }

            Logger.LogInformation("HTML fallback başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult { Name = name ?? "Bilinmeyen Ürün", Price = price.Value, ImageUrl = imageUrl, Store = "Hepsiburada" };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "HTML fallback failed for {Url}", url);
            return null;
        }
    }
}
