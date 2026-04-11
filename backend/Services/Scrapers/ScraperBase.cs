using System.Text.Json;
using System.Text.RegularExpressions;

namespace PriceTracker.Services.Scrapers;

/// <summary>
/// Tüm site scraper'larının paylaştığı temel sınıf.
/// HTML çekme, JSON-LD ayrıştırma ve fiyat parse etme işlevlerini içerir.
/// </summary>
public abstract class ScraperBase(ILogger logger, IHttpClientFactory httpClientFactory) : ISiteScraper
{
    protected ILogger Logger { get; } = logger;
    protected IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;

    public abstract bool CanHandle(string url);
    public abstract Task<ScrapeResult?> ScrapeAsync(string url);

    protected async Task<string?> FetchHtmlAsync(string url)
    {
        var client = HttpClientFactory.CreateClient("Scraper");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent",  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        request.Headers.TryAddWithoutValidation("Accept",      "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        request.Headers.TryAddWithoutValidation("Accept-Language",           "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
        request.Headers.TryAddWithoutValidation("Referer",                   "https://www.google.com/search?q=hepsiburada");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest",            "document");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode",            "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site",            "cross-site");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-User",            "?1");
        request.Headers.TryAddWithoutValidation("Sec-Ch-Ua",                 "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile",          "?0");
        request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform",        "\"Windows\"");
        request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        request.Headers.TryAddWithoutValidation("Cache-Control",             "max-age=0");

        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        Logger.LogInformation("HTTP {Status} | HTML length: {Len} chars", (int)response.StatusCode, html.Length);

        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("Non-success HTTP {Status}", (int)response.StatusCode);
            return null;
        }
        return html;
    }

    // ── Shared extractors ─────────────────────────────────────────────────

    protected ScrapeResult? TryExtractFromJsonLd(string html, string url, string store)
    {
        try
        {
            var scriptPattern = new Regex(
                @"<script[^>]+type=[""']application/ld\+json[""'][^>]*>([\s\S]*?)</script>",
                RegexOptions.IgnoreCase);

            foreach (Match match in scriptPattern.Matches(html))
            {
                var json = match.Groups[1].Value.Trim();
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var items = root.ValueKind == JsonValueKind.Array
                        ? root.EnumerateArray().ToList()
                        : [root];

                    foreach (var item in items)
                    {
                        if (!item.TryGetProperty("@type", out var typeEl)) continue;
                        var type = typeEl.GetString();
                        if (type != "Product" && type != "ItemPage") continue;

                        decimal? price = null;
                        if (item.TryGetProperty("offers", out var offers))
                        {
                            var offer = offers.ValueKind == JsonValueKind.Array ? offers[0] : offers;
                            if (offer.TryGetProperty("price", out var priceEl))
                                price = ParsePrice(priceEl.ToString());
                        }

                        if (price == null) continue;

                        var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;

                        string? imageUrl = null;
                        if (item.TryGetProperty("image", out var imageEl))
                        {
                            imageUrl = imageEl.ValueKind == JsonValueKind.Array
                                ? imageEl[0].GetString()
                                : imageEl.GetString();
                        }

                        Logger.LogInformation("JSON-LD başarılı: {Name} = {Price}", name, price);
                        return new ScrapeResult { Name = name ?? "Bilinmeyen Ürün", Price = price.Value, ImageUrl = imageUrl, Store = store };
                    }
                }
                catch { /* geçersiz JSON, atla */ }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "JSON-LD extraction failed for {Url}", url);
        }

        Logger.LogWarning("JSON-LD bulunamadı: {Url}", url);
        return null;
    }

    // ── Price parser (shared) ─────────────────────────────────────────────

    protected static decimal? ParsePrice(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var cleaned = raw
            .Replace("TL", "").Replace("₺", "")
            .Replace("$",  "").Replace("€", "").Replace("£", "")
            .Trim();

        // Türk formatı: 1.234,56 → 1234.56
        if (cleaned.Contains(',') && cleaned.Contains('.'))
            cleaned = cleaned.Replace(".", "").Replace(",", ".");
        else if (cleaned.Contains(','))
            cleaned = cleaned.Replace(",", ".");

        return decimal.TryParse(cleaned,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var price) ? price : null;
    }
}
