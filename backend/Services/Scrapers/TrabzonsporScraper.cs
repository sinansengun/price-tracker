using System.Text.Json;
using System.Text.RegularExpressions;

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
}
