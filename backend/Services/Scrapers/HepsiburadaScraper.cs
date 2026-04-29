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
    public override bool CanHandle(string url) =>
        url.Contains("hepsiburada.com") ||
        url.Contains("app.hb.biz", StringComparison.OrdinalIgnoreCase);

    public override async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        // app.hb.biz kısa linklerini gerçek Hepsiburada URL'sine çevir
        if (url.Contains("app.hb.biz", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = await ResolveHbShortLinkAsync(url);
            if (resolved == null)
            {
                Logger.LogWarning("Hepsiburada: app.hb.biz linki çözümlenemedi: {Url}", url);
                return null;
            }
            Logger.LogInformation("Hepsiburada: Kısa link çözümlendi → {Url}", resolved);
            url = resolved;
        }

        Logger.LogInformation("Hepsiburada: Fetching HTML from {Url}", url);
        var html = await FetchHtmlAsync(url);
        if (html == null)
        {
            Logger.LogWarning("HTTP fetch null döndü. Playwright fallback deneniyor.");
            var rendered = await playwright.FetchHtmlAsync(
                url,
                Microsoft.Playwright.WaitUntilState.DOMContentLoaded
            );

            if (string.IsNullOrWhiteSpace(rendered) || IsLikelyBlockedHtml(rendered))
            {
                Logger.LogWarning("Playwright fallback başarısız veya korumalı HTML döndü.");
                return null;
            }

            Logger.LogInformation("Playwright fallback başarılı ({Len} chars).", rendered.Length);
            html = rendered;
        }

        if (IsLikelyBlockedHtml(html))
        {
            Logger.LogWarning("HTTP HTML kısa/korumalı görünüyor ({Len} chars). Playwright fallback deneniyor.", html.Length);

            var rendered = await playwright.FetchHtmlAsync(
                url,
                Microsoft.Playwright.WaitUntilState.DOMContentLoaded
            );

            if (!string.IsNullOrWhiteSpace(rendered) && !IsLikelyBlockedHtml(rendered))
            {
                Logger.LogInformation("Playwright fallback başarılı ({Len} chars).", rendered.Length);
                html = rendered;
            }
            else
            {
                Logger.LogWarning("Playwright fallback da korumalı/eksik HTML döndürdü.");
                return null;
            }
        }

        // 1. Inline Redux state (accountState / __NEXT_DATA__)
        var result = TryExtractFromNextData(html, url);

        // 2. JSON-LD
        result ??= TryExtractFromJsonLd(html, url, "Hepsiburada");

        // 3. HTML meta/data attributes (fallback)
        result ??= TryExtractFromHtml(html, url);

        if (result == null) return null;

        // ── Kampanya / indirim fiyatı arama ─────────────────────────────
        // SSR JSON'da kampanya fiyatı bulunmuyor; merchant listing API'sinden
        // ve Playwright ile DOM'dan daha düşük fiyat aramayı dene.

        // 1) Playwright DOM — cookie warming ile 403 bypass
        var campaignPrice = await TryExtractCampaignPriceViaPlaywright(url);
        if (campaignPrice is > 0 && campaignPrice < result.Price)
        {
            Logger.LogInformation("Playwright kampanya fiyatı {Campaign} < mevcut fiyat {Current} — Playwright fiyatı kullanılıyor.",
                campaignPrice, result.Price);
            result.Price = campaignPrice.Value;
        }

        // 2) Playwright network response taraması — kampanya fiyatı ayrı XHR ile geliyorsa
        // warmup session altında JSON response'lardan en düşük indirimli fiyatı topla.
        var networkCampaignPrice = await playwright.ExtractLowestPriceFromNetworkWithWarmupAsync(
            "https://www.hepsiburada.com",
            url,
            new[]
            {
                "instantDiscountedUnitPrice", "instantDiscountedPrice",
                "campaignPrice", "discountedPrice", "discountedUnitPrice",
                "promotionPrice", "offerPrice", "salePrice", "price", "unitPrice"
            }
        );

        if (networkCampaignPrice is > 0 && networkCampaignPrice < result.Price)
        {
            Logger.LogInformation(
                "Playwright network kampanya fiyatı {Campaign} < mevcut fiyat {Current} — network fiyatı kullanılıyor.",
                networkCampaignPrice,
                result.Price
            );
            result.Price = networkCampaignPrice.Value;
        }

        return result;
    }

    private bool IsLikelyBlockedHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return true;
        if (html.Length < 10_000) return true;

        var titleMatch = Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
        var title = titleMatch.Success ? titleMatch.Groups[1].Value : string.Empty;

        if (title.Contains("güvenlik", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("security", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("captcha", StringComparison.OrdinalIgnoreCase))
            return true;

        // Body-level anti-bot sinyallerini sadece kısa/şüpheli sayfalarda dikkate al.
        // Büyük HTML'lerde "security" kelimesi normal script/metin içinde geçebiliyor.
        if (html.Length < 50_000)
        {
            if (html.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("access denied", StringComparison.OrdinalIgnoreCase) ||
                html.Contains("bot verification", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
                (async () => {
                    const text = document.body.innerText;
                    const prices = [];

                    const pushIfValid = (raw) => {
                        if (!raw) return;
                        const val = parseFloat(String(raw).replace(/\./g, '').replace(',', '.').replace(/[^\d.]/g, ''));
                        if (Number.isFinite(val) && val > 10) prices.push(val);
                    };

                    // 'Sepete özel fiyat 1.475,18 TL' pattern
                    const sepeteMatch = text.match(/[Ss]epete[\s\S]{0,30}zel[\s\S]{0,30}fiyat[\s\S]{0,30}?([\d.]+,\d{2})\s*TL/);
                    if (sepeteMatch) {
                        pushIfValid(sepeteMatch[1]);
                    }

                    // data-test-id='price-current-price'
                    const priceEl = document.querySelector('[data-test-id=""price-current-price""]');
                    if (priceEl) {
                        pushIfValid(priceEl.textContent);
                    }

                    // Tüm fiyat elementlerini tara
                    const allPriceEls = document.querySelectorAll('[class*=""price""], [class*=""Price""], [data-test-id*=""price""]');
                    for (const el of allPriceEls) {
                        pushIfValid(el.textContent);
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

                // Bazı kampanya/sepete-ozel fiyatlar standart alanlarda degil,
                // ham NextData JSON icinde farkli key'lerde geliyor.
                var betterCampaign = TryFindBetterCampaignPriceFromRawJson(jsonText, price.Value);
                if (betterCampaign is > 0 && betterCampaign < price)
                {
                    Logger.LogInformation("Ham JSON kampanya adayi bulundu: {Candidate} < {Current}", betterCampaign, price);
                    price = betterCampaign;
                }

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

    private static decimal? TryFindBetterCampaignPriceFromRawJson(string jsonText, decimal currentPrice)
    {
        if (string.IsNullOrWhiteSpace(jsonText)) return null;

        decimal? best = null;

        // Kampanya/indirime isaret eden alanlari tara.
        var campaignKeys = new[]
        {
            "instantDiscountedUnitPrice", "instantDiscountedPrice",
            "campaignPrice", "discountedPrice", "discountedUnitPrice",
            "promotionPrice", "offerPrice", "finalPrice",
            "priceAfterDiscount", "priceAfterCoupon", "couponPrice",
            "checkoutPrice", "basketPrice", "unitPriceAfterDiscount"
        };

        foreach (var key in campaignKeys)
        {
            foreach (Match m in Regex.Matches(jsonText, $@"""{key}""\s*:\s*([\d.]+)", RegexOptions.IgnoreCase))
            {
                var candidate = ParsePrice(m.Groups[1].Value);
                if (!IsReasonableCampaignCandidate(candidate, currentPrice)) continue;
                if (best == null || candidate < best) best = candidate;
            }
        }

        // Bazi payload'larda kampanya yalniz formattedPrice/value ciftinde olur.
        foreach (Match m in Regex.Matches(jsonText, @"""formattedPrice""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase))
        {
            var candidate = ParsePrice(m.Groups[1].Value);
            if (!IsReasonableCampaignCandidate(candidate, currentPrice)) continue;
            if (best == null || candidate < best) best = candidate;
        }

        return best;
    }

    private static bool IsReasonableCampaignCandidate(decimal? candidate, decimal currentPrice)
    {
        if (candidate is null or <= 10) return false;
        if (candidate > currentPrice) return false;

        // Uzak alakasiz fiyatlari ele (aksesuar/yan urun vb.)
        var lowerBound = currentPrice * 0.70m;
        return candidate >= lowerBound;
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

    /// <summary>
    /// app.hb.biz/{code} → 301 Location'daki adj_fallback parametresinden
    /// gerçek hepsiburada.com ürün URL'sini çıkarır.
    /// </summary>
    private async Task<string?> ResolveHbShortLinkAsync(string shortUrl)
    {
        try
        {
            // Redirect'i takip etme — ilk Location header'ından target URL'yi çıkar.
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var tempClient = new HttpClient(handler);
            using var request = new HttpRequestMessage(HttpMethod.Get, shortUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");

            using var response = await tempClient.SendAsync(request);

            var location = response.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(location))
            {
                Logger.LogWarning("app.hb.biz yanıtında Location header yok: {Url} (HTTP {Status})", shortUrl, (int)response.StatusCode);
                return null;
            }

            // 1) Redirect doğrudan hepsiburada.com'a gidiyorsa onu kullan.
            if (TryNormalizeHepsiburadaUrl(location, out var directHbUrl))
                return directHbUrl;

            // 2) Adjust redirect: adj_fallback parametresini çöz.
            var fallbackMatch = Regex.Match(location, @"(?:\?|&)adj_fallback=([^&]+)", RegexOptions.IgnoreCase);
            if (fallbackMatch.Success)
            {
                var fallback = Uri.UnescapeDataString(fallbackMatch.Groups[1].Value);
                // Bazı payload'larda değer iki kez encode gelebiliyor.
                if (fallback.Contains("%2F", StringComparison.OrdinalIgnoreCase))
                    fallback = Uri.UnescapeDataString(fallback);

                if (TryNormalizeHepsiburadaUrl(fallback, out var fallbackHbUrl))
                    return fallbackHbUrl;
            }

            // 3) adj_fallback yoksa sku ile URL üretmeyi dene.
            var skuMatch = Regex.Match(location, @"(?:\?|&)sku=([A-Z0-9]+)", RegexOptions.IgnoreCase);
            if (skuMatch.Success)
            {
                var sku = skuMatch.Groups[1].Value.ToUpperInvariant();
                return $"https://www.hepsiburada.com/-p-{sku}";
            }

            Logger.LogWarning("app.hb.biz linkinden hepsiburada URL'si çıkarılamadı: {Url} | Location: {Location}",
                shortUrl, location[..Math.Min(400, location.Length)]);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "app.hb.biz link çözümlemesi başarısız: {Url}", shortUrl);
            return null;
        }
    }

    private static bool TryNormalizeHepsiburadaUrl(string url, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.EndsWith("hepsiburada.com", StringComparison.OrdinalIgnoreCase)) return false;

        normalized = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
        return true;
    }
}
