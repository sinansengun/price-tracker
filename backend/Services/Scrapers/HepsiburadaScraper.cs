using System.Text.Json;
using System.Text.RegularExpressions;
using PriceTracker.Services;

namespace PriceTracker.Services.Scrapers;

public class HepsiburadaScraper(
    ILogger<HepsiburadaScraper> logger,
    IHttpClientFactory httpClientFactory,
    PlaywrightService playwright)
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

        // 1. Inline Redux state (accountState / __NEXT_DATA__)
        var result = TryExtractFromNextData(html, url);

        // 2. JSON-LD
        result ??= TryExtractFromJsonLd(html, url, "Hepsiburada");

        // 3. Listing API (full ScrapeResult fallback)
        result ??= await TryListingApiAsync(html, url);

        // 4. HTML meta/data attributes (fallback)
        result ??= TryExtractFromHtml(html, url);

        if (result == null) return null;

        // ── Kampanya / indirim fiyatı arama ─────────────────────────────
        // SSR JSON'da kampanya fiyatı bulunmuyor; merchant listing API'sinden
        // ve Playwright ile DOM'dan daha düşük fiyat aramayı dene.

        // 1) Merchant listing API — HttpClient ile çalışır, Playwright'a gerek yok
        var apiPrice = await TryFindLowestApiPriceAsync(html, url);
        if (apiPrice is > 0 && apiPrice < result.Price)
        {
            Logger.LogInformation("Merchant API kampanya fiyatı {Api} < SSR fiyatı {Ssr} — API fiyatı kullanılıyor.",
                apiPrice, result.Price);
            result.Price = apiPrice.Value;
        }

        // 2) Playwright DOM — cookie warming ile 403 bypass
        var campaignPrice = await TryExtractCampaignPriceViaPlaywright(url);
        if (campaignPrice is > 0 && campaignPrice < result.Price)
        {
            Logger.LogInformation("Playwright kampanya fiyatı {Campaign} < mevcut fiyat {Current} — Playwright fiyatı kullanılıyor.",
                campaignPrice, result.Price);
            result.Price = campaignPrice.Value;
        }

        return result;
    }

    /// <summary>
    /// Merchant listing API'sinden kampanya/indirim fiyatlarını arar.
    /// Ham JSON üzerinde regex ile tüm nesting seviyelerindeki fiyat alanlarını tarar.
    /// </summary>
    private async Task<decimal?> TryFindLowestApiPriceAsync(string pageHtml, string url)
    {
        try
        {
            var skuMatch = Regex.Match(url, @"-p(?:m)?-([A-Z0-9]+)(?:[?#]|$)");
            if (!skuMatch.Success)
                skuMatch = Regex.Match(pageHtml, @"""sku""\s*:\s*""([^""]+)""");
            if (!skuMatch.Success) return null;

            var sku = skuMatch.Groups[1].Value;
            Logger.LogInformation("Merchant API kampanya fiyatı aranıyor, SKU: {Sku}", sku);

            var client = httpClientFactory.CreateClient("Scraper");
            var apiUrl = $"https://www.hepsiburada.com/api/listing/merchantlisting/allmerchants/sku/{sku}";

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.TryAddWithoutValidation("User-Agent",      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept",          "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9");
            request.Headers.TryAddWithoutValidation("Referer",         url);
            request.Headers.TryAddWithoutValidation("Origin",          "https://www.hepsiburada.com");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Logger.LogInformation("Merchant API HTTP {Status} | Body ({Len} chars): {Preview}",
                (int)response.StatusCode, body.Length, body[..Math.Min(600, body.Length)]);

            if (!response.IsSuccessStatusCode) return null;

            // Kampanya/indirim alanlarını ham JSON üzerinde regex ile tara (nesting bağımsız)
            decimal? lowest = null;

            foreach (var field in new[]
            {
                "instantDiscountPrice", "instantDiscountedPrice", "instantDiscountedUnitPrice",
                "campaignPrice", "discountedPrice", "discountedUnitPrice",
                "promotionPrice", "offerPrice"
            })
            {
                foreach (Match m in Regex.Matches(body, $@"""{field}""\s*:\s*([\d.]+)"))
                {
                    var p = ParsePrice(m.Groups[1].Value);
                    if (p is > 10 && (lowest == null || p < lowest))
                    {
                        Logger.LogInformation("Merchant API field '{Field}' = {Price}", field, p);
                        lowest = p;
                    }
                }
            }

            // Kampanya fiyatı bulunamadıysa genel fiyat alanlarından en düşüğünü al
            if (lowest == null)
            {
                foreach (var field in new[] { "salePrice", "price", "unitPrice" })
                {
                    foreach (Match m in Regex.Matches(body, $@"""{field}""\s*:\s*([\d.]+)"))
                    {
                        var p = ParsePrice(m.Groups[1].Value);
                        if (p is > 10 && (lowest == null || p < lowest))
                            lowest = p;
                    }
                }
            }

            if (lowest != null)
                Logger.LogInformation("Merchant API en düşük fiyat: {Price} (SKU: {Sku})", lowest, sku);

            return lowest;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Merchant API fiyat araması başarısız");
            return null;
        }
    }

    /// <summary>
    /// Playwright ile sayfayı render edip kampanya/indirim fiyatını DOM'dan JS ile çıkarır.
    /// Cookie warming: önce hepsiburada.com anasayfasını ziyaret edip cookie alır,
    /// ardından ürün sayfasına gider.
    /// </summary>
    private async Task<decimal?> TryExtractCampaignPriceViaPlaywright(string url)
    {
        try
        {
            var jsExtract = @"
                (() => {
                    const text = document.body.innerText;
                    const prices = [];

                    // 'Sepete özel fiyat 1.475,18 TL' pattern
                    const sepeteMatch = text.match(/[Ss]epete[\s\S]{0,30}zel[\s\S]{0,30}fiyat[\s\S]{0,30}?([\d.]+,\d{2})\s*TL/);
                    if (sepeteMatch) {
                        const raw = sepeteMatch[1].replace(/\./g, '').replace(',', '.');
                        const val = parseFloat(raw);
                        if (val > 0) prices.push(val);
                    }

                    // data-test-id='price-current-price'
                    const priceEl = document.querySelector('[data-test-id=""price-current-price""]');
                    if (priceEl) {
                        const raw = priceEl.textContent.replace(/[^\d.,]/g, '').replace(/\./g, '').replace(',', '.');
                        const val = parseFloat(raw);
                        if (val > 0) prices.push(val);
                    }

                    // Tüm fiyat elementlerini tara
                    const allPriceEls = document.querySelectorAll('[class*=""price""], [class*=""Price""], [data-test-id*=""price""]');
                    for (const el of allPriceEls) {
                        const raw = el.textContent.replace(/[^\d.,]/g, '').replace(/\./g, '').replace(',', '.');
                        const val = parseFloat(raw);
                        if (val > 10) prices.push(val);
                    }

                    if (prices.length === 0) return null;
                    return Math.min(...prices).toString();
                })()
            ";

            // Cookie warming: önce anasayfayı ziyaret et, sonra ürün sayfasına git
            var priceStr = await playwright.EvaluateWithWarmupAsync(
                "https://www.hepsiburada.com",
                url,
                "[data-test-id*='price'], [class*='productPrice'], [class*='Price']",
                jsExtract,
                timeoutMs: 12_000
            );

            if (!string.IsNullOrWhiteSpace(priceStr))
            {
                var price = ParsePrice(priceStr);
                if (price is > 0)
                {
                    Logger.LogInformation("Playwright DOM fiyatı: {Price}", price);
                    return price;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Playwright kampanya fiyatı çıkarılamadı");
        }

        return null;
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

            var jsonText = match.Groups[1].Value;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(jsonText); }
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

                // ── Fiyat: ham JSON metni üzerinde indirim alanlarını tara ──────
                // JSON ağacının derinliğinden bağımsız olarak ilk indirim değerini bulur.
                decimal? price = null;

                // 1) Kampanya/indirim-spesifik alanlar (ham regex — nesting seviyesinden bağımsız)
                foreach (var discountKey in new[] {
                    "instantDiscountedUnitPrice", "instantDiscountedPrice",
                    "campaignPrice", "discountedUnitPrice", "discountedPrice",
                    "promotionPrice", "offerPrice" })
                {
                    var dm = Regex.Match(jsonText,
                        $@"""{discountKey}""\s*:\s*([\d.]+)",
                        RegexOptions.IgnoreCase);
                    if (dm.Success)
                    {
                        var candidate = ParsePrice(dm.Groups[1].Value);
                        if (candidate is > 0)
                        {
                            Logger.LogInformation("Ham JSON indirim alanı '{Key}' = {Price}", discountKey, candidate);
                            price = candidate;
                            break;
                        }
                    }
                }

                // 2) productState.listings[] içindeki price/salePrice/unitPrice
                if (price == null)
                {
                    if (productState.TryGetProperty("listings", out var psListings) &&
                        psListings.ValueKind == JsonValueKind.Array && psListings.GetArrayLength() > 0)
                    {
                        var first = psListings[0];
                        Logger.LogInformation("productState.listings[0] keys: {Keys}",
                            string.Join(", ", first.EnumerateObject().Select(p => p.Name)));
                        foreach (var key in new[] { "salePrice", "price", "unitPrice" })
                        {
                            if (first.TryGetProperty(key, out var prEl) && prEl.ValueKind != JsonValueKind.Null)
                            {
                                price = ParsePrice(prEl.ToString());
                                if (price != null) { Logger.LogInformation("listings[0].{Key} = {Price}", key, price); break; }
                            }
                        }
                    }
                }

                // 3) productState.product içindeki alanlar (fallback)
                price ??= ExtractPriceFromReduxProduct(data);
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
        // 1. listings[0] — kampanya/indirim fiyatları önce kontrol edilir
        if (data.TryGetProperty("listings", out var listings) &&
            listings.ValueKind == JsonValueKind.Array && listings.GetArrayLength() > 0)
        {
            var first = listings[0];
            // İndirimli/kampanya fiyatları önce dene
            foreach (var key in new[] {
                "instantDiscountedUnitPrice", "campaignPrice", "discountedUnitPrice",
                "discountedPrice", "promotionPrice", "offerPrice",
                "salePrice", "price", "unitPrice" })
            {
                if (first.TryGetProperty(key, out var prEl) && prEl.ValueKind != JsonValueKind.Null)
                {
                    var price = ParsePrice(prEl.ToString());
                    if (price != null) return price;
                }
            }
        }

        // 2. Doğrudan ürün üzerindeki indirim alanları
        foreach (var key in new[] {
            "instantDiscountedPrice", "campaignPrice", "discountedPrice",
            "promotionPrice", "offerPrice" })
        {
            if (data.TryGetProperty(key, out var prEl) && prEl.ValueKind != JsonValueKind.Null)
            {
                var price = ParsePrice(prEl.ToString());
                if (price != null) return price;
            }
        }

        // 3. prices array — önce indirimli alt alanları dene, sonra value
        if (data.TryGetProperty("prices", out var pricesArr) &&
            pricesArr.ValueKind == JsonValueKind.Array && pricesArr.GetArrayLength() > 0)
        {
            var first = pricesArr[0];
            foreach (var key in new[] { "discountedPrice", "campaignPrice", "value" })
            {
                if (first.TryGetProperty(key, out var valEl) && valEl.ValueKind != JsonValueKind.Null)
                {
                    var price = ParsePrice(valEl.ToString());
                    if (price != null) return price;
                }
            }
            if (first.TryGetProperty("formattedPrice", out var fmtEl))
            {
                var price = ParsePrice(fmtEl.GetString());
                if (price != null) return price;
            }
        }

        // 4. Genel fiyat alanları (fallback)
        foreach (var key in new[] { "unitPrice", "price", "salePrice", "currentPrice", "lowestPrice" })
        {
            if (data.TryGetProperty(key, out var prEl))
            {
                var price = ParsePrice(prEl.ToString());
                if (price != null) return price;
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
            foreach (var key in new[] {
                "instantDiscountedUnitPrice", "campaignPrice", "discountedUnitPrice",
                "discountedPrice", "promotionPrice", "offerPrice",
                "salePrice", "price", "unitPrice", "lowestPrice", "currentPrice" })
            {
                if (data.TryGetProperty(key, out var prEl) && prEl.ValueKind != JsonValueKind.Null)
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
                // İndirimli/kampanya fiyatı önce ara — "Sepete özel fiyat 1.475,18 TL"
                @"[Ss]epete\s+[öo]zel\s+fiyat[\s\S]{0,10}?(\d[\d.,]+)\s*TL",
                @"""instantDiscountedUnitPrice""\s*:\s*([0-9.,]+)",
                @"""campaignPrice""\s*:\s*([0-9.,]+)",
                @"""discountedPrice""\s*:\s*([0-9.,]+)",
                @"""promotionPrice""\s*:\s*([0-9.,]+)",
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
