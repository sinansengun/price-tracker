using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

namespace PriceTracker.Services.Scrapers;

/// <summary>
/// trabzonspor.com.tr (TS Club / Ticimax) ürün scraper'ı.
/// Strateji sırası:
///   1. productDetailModel inline JSON
///   2. JSON-LD (Product)
///   3. Open Graph meta etiketleri
/// </summary>
public class TrabzonsporScraper(
    ILogger<TrabzonsporScraper> logger,
    IHttpClientFactory httpClientFactory)
    : ScraperBase(logger, httpClientFactory)
{
    public override bool CanHandle(string url) =>
        url.Contains("trabzonspor.com.tr", StringComparison.OrdinalIgnoreCase);

    public override async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        Logger.LogInformation("Trabzonspor: Fetching {Url}", url);
        var html = await FetchHtmlAsync(url);
        if (html == null) return null;

        var result = TryExtractFromProductDetailModel(html);
        if (result != null) return result;

        result = TryExtractFromJsonLd(html, url, "TS Club");
        if (result != null) return result;

        result = TryExtractFromOpenGraph(html);
        if (result != null) return result;

        Logger.LogWarning("Trabzonspor: Hiçbir strateji çalışmadı: {Url}", url);
        return null;
    }

    private ScrapeResult? TryExtractFromProductDetailModel(string html)
    {
        try
        {
            var modelMatch = Regex.Match(
                html,
                @"var\s+productDetailModel\s*=\s*(\{[\s\S]*?\})\s*;\s*globalModel\.pageType",
                RegexOptions.IgnoreCase);

            if (!modelMatch.Success) return null;

            using var doc = JsonDocument.Parse(modelMatch.Groups[1].Value);
            var root = doc.RootElement;

            var name = root.TryGetProperty("productName", out var nameEl)
                ? nameEl.GetString()
                : null;

            decimal? price = null;

            if (root.TryGetProperty("productPriceStr", out var priceStrEl))
                price = ParsePrice(priceStrEl.ToString());

            if (price == null && root.TryGetProperty("productPriceKDVIncluded", out var kdvPriceEl))
                price = ParsePrice(kdvPriceEl.ToString());

            if (price == null && root.TryGetProperty("productPrice", out var rawPriceEl))
                price = ParsePrice(rawPriceEl.ToString());

            if (price == null && root.TryGetProperty("product", out var product) &&
                product.TryGetProperty("indirimliFiyatiStr", out var discountedStrEl))
            {
                price = ParsePrice(discountedStrEl.ToString());
            }

            if (price == null) return null;

            // Sayfa döviz modunda (örn. EUR) render edildiyse fiyat TRY'ye çevrilmeden gelebilir.
            // Ticimax modelindeki currencies[].kur değerini kullanarak TRY'ye normalize ediyoruz.
            var currencyCode = root.TryGetProperty("productCurrency", out var currencyEl)
                ? currencyEl.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(currencyCode) &&
                !currencyCode.Equals("TRY", StringComparison.OrdinalIgnoreCase) &&
                TryGetCurrencyRateToTry(root, currencyCode) is { } rateToTry)
            {
                price = decimal.Round(price.Value * rateToTry, 2, MidpointRounding.AwayFromZero);
            }

            string? imageUrl = null;
            if (root.TryGetProperty("productImages", out var productImages) &&
                productImages.ValueKind == JsonValueKind.Array &&
                productImages.GetArrayLength() > 0)
            {
                var first = productImages[0];
                if (first.TryGetProperty("bigImagePath", out var bigImageEl))
                    imageUrl = bigImageEl.GetString();
                else if (first.TryGetProperty("imagePath", out var imagePathEl))
                    imageUrl = imagePathEl.GetString();
            }

            Logger.LogInformation("Trabzonspor productDetailModel başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Bilinmeyen Ürün" : name,
                Price = price.Value,
                ImageUrl = imageUrl,
                Store = "TS Club"
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Trabzonspor productDetailModel extraction hatası");
            return null;
        }
    }

    private ScrapeResult? TryExtractFromOpenGraph(string html)
    {
        try
        {
            string? GetMeta(string property)
            {
                var m = Regex.Match(
                    html,
                    $@"<meta[^>]+property=[""']{Regex.Escape(property)}[""'][^>]+content=[""']([^""']+)[""']",
                    RegexOptions.IgnoreCase);
                if (!m.Success)
                {
                    m = Regex.Match(
                        html,
                        $@"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']{Regex.Escape(property)}[""']",
                        RegexOptions.IgnoreCase);
                }
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }

            var name = GetMeta("og:title");
            var imageUrl = GetMeta("og:image");
            var priceRaw = GetMeta("product:price:amount") ?? GetMeta("og:price:amount");
            var price = ParsePrice(priceRaw);

            if (price == null || string.IsNullOrWhiteSpace(name)) return null;

            Logger.LogInformation("Trabzonspor OG başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult
            {
                Name = name,
                Price = price.Value,
                ImageUrl = imageUrl,
                Store = "TS Club"
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Trabzonspor OG extraction hatası");
            return null;
        }
    }

    private static decimal? TryGetCurrencyRateToTry(JsonElement root, string currencyCode)
    {
        if (!root.TryGetProperty("currencies", out var currencies) ||
            currencies.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var currency in currencies.EnumerateArray())
        {
            if (!currency.TryGetProperty("dovizKodu", out var codeEl)) continue;
            var code = codeEl.GetString();
            if (!string.Equals(code, currencyCode, StringComparison.OrdinalIgnoreCase)) continue;

            if (!currency.TryGetProperty("kur", out var rateEl)) return null;

            if (rateEl.ValueKind == JsonValueKind.Number && rateEl.TryGetDecimal(out var numericRate))
                return numericRate;

            if (rateEl.ValueKind == JsonValueKind.String &&
                decimal.TryParse(rateEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var stringRate))
            {
                return stringRate;
            }

            return null;
        }

        return null;
    }
}
