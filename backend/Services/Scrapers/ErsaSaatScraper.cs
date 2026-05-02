using System.Text.RegularExpressions;
using System.Net;

namespace PriceTracker.Services.Scrapers;

/// <summary>
/// shopcasio.ersasaat.com.tr / ersasaat.com.tr için scraper.
/// Strateji sırası:
///   1. Product JSON-LD (offers.price)
///   2. Hidden input (proPrice / oldPrice)
///   3. addBasket form içindeki görünür fiyat
///   4. productDetails.push(...) script fiyatı
/// </summary>
public class ErsaSaatScraper(ILogger<ErsaSaatScraper> logger, IHttpClientFactory httpClientFactory)
    : ScraperBase(logger, httpClientFactory)
{
    public override bool CanHandle(string url) =>
        url.Contains("ersasaat.com.tr", StringComparison.OrdinalIgnoreCase);

    public override async Task<ScrapeResult?> ScrapeAsync(string url)
    {
        Logger.LogInformation("ErsaSaat: Fetching {Url}", url);
        var html = await FetchHtmlAsync(url);
        if (html == null) return null;

        if (html.Length < 5_000)
        {
            Logger.LogWarning("ErsaSaat: Sayfa çok kısa ({Len} chars), bot koruması olabilir.", html.Length);
            return null;
        }

        // 1. Sepetteki fiyat (iş kuralı: son/final fiyat)
        var result = TryExtractBasketPrice(html);
        if (result != null) return result;

        // 2. JSON-LD Product
        result = TryExtractFromJsonLd(html, url, "Ersa Saat");
        if (result != null)
        {
            result.Name = string.IsNullOrWhiteSpace(result.Name)
                ? ExtractName(html)
                : result.Name;
            result.ImageUrl ??= ExtractImageUrl(html);
            return result;
        }

        // 3. Hidden input fiyatı
        result = TryExtractFromHiddenInputs(html);
        if (result != null) return result;

        // 4. addBasket form fiyatı
        result = TryExtractFromAddBasketArea(html);
        if (result != null) return result;

        // 5. Inline productDetails.push script
        result = TryExtractFromProductDetailsScript(html);
        if (result != null) return result;

        Logger.LogWarning("ErsaSaat: Hiçbir strateji çalışmadı: {Url}", url);
        return null;
    }

    private ScrapeResult? TryExtractBasketPrice(string html)
    {
        try
        {
            // Mobil sabit alt bar: "Sepete özel fiyat" + ana sepet fiyatı.
            var mobileBasket = Regex.Match(
                html,
                "<div[^>]+class=[\"'][^\"']*MobileAddBasket[^\"']*[\"'][\\s\\S]{0,2500}?Sepete\\s*(?:özel|ozel)\\s*fiyat[\\s\\S]{0,500}?<span[^>]+class=[\"'][^\"']*\\bprice\\b[^\"']*[\"'][^>]*>\\s*([\\d\\.,]+)\\s*(?:TL|₺)?",
                RegexOptions.IgnoreCase);

            if (mobileBasket.Success)
            {
                var mobilePrice = ParsePrice(mobileBasket.Groups[1].Value);
                if (mobilePrice != null)
                {
                    var name = ExtractName(html);
                    var imageUrl = ExtractImageUrl(html);

                    Logger.LogInformation("ErsaSaat sepet fiyatı (mobile) başarılı: {Name} = {Price}", name, mobilePrice);
                    return new ScrapeResult
                    {
                        Name = name,
                        Price = mobilePrice.Value,
                        ImageUrl = imageUrl,
                        Store = "Ersa Saat"
                    };
                }
            }

            // Desktop alan: basketSaleInfoDetail içindeki <p>...
            var desktopBasket = Regex.Match(
                html,
                "<form[^>]+name=[\"']addBasket[\"'][^>]*[\\s\\S]{0,5000}?<span[^>]+class=[\"'][^\"']*basketSaleInfoDetail[^\"']*[\"'][^>]*>[\\s\\S]{0,500}?<p>\\s*([\\d\\.,]+)\\s*(?:TL|₺)?",
                RegexOptions.IgnoreCase);

            if (!desktopBasket.Success)
            {
                desktopBasket = Regex.Match(
                    html,
                    "Sepette\\s*[\\s\\S]{0,250}?<p>\\s*([\\d\\.,]+)\\s*(?:TL|₺)?",
                    RegexOptions.IgnoreCase);
            }

            var desktopPrice = desktopBasket.Success ? ParsePrice(desktopBasket.Groups[1].Value) : null;
            if (desktopPrice == null) return null;

            var productName = ExtractName(html);
            var productImage = ExtractImageUrl(html);

            Logger.LogInformation("ErsaSaat sepet fiyatı (desktop) başarılı: {Name} = {Price}", productName, desktopPrice);
            return new ScrapeResult
            {
                Name = productName,
                Price = desktopPrice.Value,
                ImageUrl = productImage,
                Store = "Ersa Saat"
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "ErsaSaat basket price extraction hatası");
            return null;
        }
    }

    private ScrapeResult? TryExtractFromHiddenInputs(string html)
    {
        try
        {
            var priceMatch = Regex.Match(
                html,
                "<input[^>]+name=[\"']proPrice[\"'][^>]+value=[\"']([^\"']+)[\"']",
                RegexOptions.IgnoreCase);

            if (!priceMatch.Success)
            {
                priceMatch = Regex.Match(
                    html,
                    "<input[^>]+name=[\"']oldPrice[\"'][^>]+value=[\"']([^\"']+)[\"']",
                    RegexOptions.IgnoreCase);
            }

            var price = priceMatch.Success ? ParsePrice(priceMatch.Groups[1].Value) : null;
            if (price == null) return null;

            var name = ExtractName(html);
            var imageUrl = ExtractImageUrl(html);

            Logger.LogInformation("ErsaSaat hidden input başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult
            {
                Name = name,
                Price = price.Value,
                ImageUrl = imageUrl,
                Store = "Ersa Saat"
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "ErsaSaat hidden input extraction hatası");
            return null;
        }
    }

    private ScrapeResult? TryExtractFromAddBasketArea(string html)
    {
        try
        {
            var priceMatch = Regex.Match(
                html,
                "<form[^>]+id=[\"']addBasket[\"'][\\s\\S]{0,3000}?<span[^>]+class=[\"'][^\"']*\\bprice\\b[^\"']*[\"'][^>]*>\\s*([\\d\\.,]+)\\s*(?:TL|₺)",
                RegexOptions.IgnoreCase);

            var price = priceMatch.Success ? ParsePrice(priceMatch.Groups[1].Value) : null;
            if (price == null) return null;

            var name = ExtractName(html);
            var imageUrl = ExtractImageUrl(html);

            Logger.LogInformation("ErsaSaat addBasket fiyatı başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult
            {
                Name = name,
                Price = price.Value,
                ImageUrl = imageUrl,
                Store = "Ersa Saat"
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "ErsaSaat addBasket extraction hatası");
            return null;
        }
    }

    private ScrapeResult? TryExtractFromProductDetailsScript(string html)
    {
        try
        {
            var match = Regex.Match(
                html,
                "productDetails\\.push\\(\\{[\\s\\S]{0,1500}?\"name\"\\s*:\\s*\"([^\"]+)\"[\\s\\S]{0,1500}?\"price\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                match = Regex.Match(
                    html,
                    "productDetails\\.push\\(\\{[\\s\\S]{0,1500}?\"price\"\\s*:\\s*\"([^\"]+)\"",
                    RegexOptions.IgnoreCase);
            }

            if (!match.Success) return null;

            string? name;
            string rawPrice;
            if (match.Groups.Count >= 3 && !string.IsNullOrWhiteSpace(match.Groups[2].Value))
            {
                name = match.Groups[1].Value.Trim();
                rawPrice = match.Groups[2].Value;
            }
            else
            {
                name = ExtractName(html);
                rawPrice = match.Groups[1].Value;
            }

            var price = ParsePrice(rawPrice);
            if (price == null) return null;

            var imageUrl = ExtractImageUrl(html);

            Logger.LogInformation("ErsaSaat productDetails script başarılı: {Name} = {Price}", name, price);
            return new ScrapeResult
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Bilinmeyen Ürün" : name,
                Price = price.Value,
                ImageUrl = imageUrl,
                Store = "Ersa Saat"
            };
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "ErsaSaat productDetails script extraction hatası");
            return null;
        }
    }

    private static string ExtractName(string html)
    {
        var h1 = Regex.Match(
            html,
            "<h1[^>]*class=[\"'][^\"']*DetailPageProductTitle[^\"']*[\"'][^>]*>([^<]+)</h1>",
            RegexOptions.IgnoreCase);
        if (h1.Success)
            return WebUtility.HtmlDecode(h1.Groups[1].Value.Trim());

        h1 = Regex.Match(html, "<h1[^>]*>([^<]+)</h1>", RegexOptions.IgnoreCase);
        if (h1.Success)
            return WebUtility.HtmlDecode(h1.Groups[1].Value.Trim());

        var title = Regex.Match(html, "<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
        if (title.Success)
        {
            var t = WebUtility.HtmlDecode(title.Groups[1].Value.Trim());
            return t.Split('|', '-')[0].Trim();
        }

        return "Bilinmeyen Ürün";
    }

    private static string? ExtractImageUrl(string html)
    {
        // Ürün görseli için en güvenilir kaynak: Ersa Cloud ürün CDN.
        var imageMatch = Regex.Match(
            html,
            "https?://img\\.ersacloud\\.com/product/[^\\s\"'<>]+",
            RegexOptions.IgnoreCase);
        if (imageMatch.Success) return imageMatch.Value;

        // Fallback: og:image (bazı sayfalarda logo olabilir).
        var ogImage = Regex.Match(
            html,
            "<meta[^>]+property=[\"']og:image[\"'][^>]+content=[\"']([^\"']+)[\"']",
            RegexOptions.IgnoreCase);
        if (!ogImage.Success)
        {
            ogImage = Regex.Match(
                html,
                "<meta[^>]+content=[\"']([^\"']+)[\"'][^>]+property=[\"']og:image[\"']",
                RegexOptions.IgnoreCase);
        }

        return ogImage.Success ? ogImage.Groups[1].Value.Trim() : null;
    }
}
