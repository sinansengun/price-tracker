using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace PriceTracker.Services;

public interface IScrapeErrorLogQueryService
{
    Task<IReadOnlyList<ScrapeErrorLogItem>> GetRecentProductErrorsAsync(int productId, int limit, CancellationToken cancellationToken = default);
}

public sealed record ScrapeErrorLogItem(
    string EventId,
    DateTime AttemptedAt,
    string Level,
    string Reason,
    string Message,
    string? Scraper,
    string? CheckRunId,
    string? IssueUrl);

public sealed class SentryScrapeErrorLogQueryService(
    HttpClient httpClient,
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<SentryScrapeErrorLogQueryService> logger) : IScrapeErrorLogQueryService
{
    private const int MinLimit = 1;
    private const int MaxLimit = 25;
    private const int CacheSeconds = 45;

    public async Task<IReadOnlyList<ScrapeErrorLogItem>> GetRecentProductErrorsAsync(int productId, int limit, CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, MinLimit, MaxLimit);
        var settings = GetSettings();
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ApiToken)
            || string.IsNullOrWhiteSpace(settings.OrgSlug)
            || string.IsNullOrWhiteSpace(settings.ProjectSlug))
        {
            return [];
        }

        var orgSlug = settings.OrgSlug!;
        var projectSlug = settings.ProjectSlug!;
        var apiToken = settings.ApiToken!;

        var cacheKey = $"sentry:scrape-errors:{productId}:{safeLimit}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<ScrapeErrorLogItem>? cached) && cached != null)
        {
            return cached;
        }

        try
        {
            using var request = BuildRequest(settings.BaseUrl, orgSlug, projectSlug, apiToken, productId, safeLimit);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Sentry events query failed for productId={ProductId} status={StatusCode}",
                    productId,
                    (int)response.StatusCode);
                return [];
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = ParseEvents(payload, safeLimit);
            cache.Set(cacheKey, parsed, TimeSpan.FromSeconds(CacheSeconds));
            return parsed;
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sentry events query failed for productId={ProductId}", productId);
            return [];
        }
    }

    private HttpRequestMessage BuildRequest(
        string baseUrl,
        string orgSlug,
        string projectSlug,
        string apiToken,
        int productId,
        int limit)
    {
        baseUrl = baseUrl.TrimEnd('/');
        var queryText = $"feature:scrape product_id:{productId}";
        var query = Uri.EscapeDataString(queryText);
        var requestUrl = $"{baseUrl}/projects/{Uri.EscapeDataString(orgSlug)}/{Uri.EscapeDataString(projectSlug)}/events/?query={query}&per_page={limit}";

        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        return request;
    }

    private static IReadOnlyList<ScrapeErrorLogItem> ParseEvents(string payload, int limit)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<ScrapeErrorLogItem>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var tags = ParseTags(element);
            var eventId = GetString(element, "eventID") ?? GetString(element, "id") ?? string.Empty;
            var level = GetString(element, "level") ?? "error";
            var message = GetString(element, "title") ?? GetString(element, "message") ?? "Scrape failed";
            var reason = tags.TryGetValue("reason", out var reasonTag) && !string.IsNullOrWhiteSpace(reasonTag)
                ? reasonTag
                : "unknown";
            var scraper = tags.TryGetValue("scraper_name", out var scraperTag) ? scraperTag : null;
            var checkRunId = tags.TryGetValue("check_run_id", out var runTag) ? runTag : null;
            var attemptedAtRaw = GetString(element, "dateCreated") ?? GetString(element, "dateReceived");
            var attemptedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(attemptedAtRaw)
                && DateTime.TryParse(attemptedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                attemptedAt = parsed.ToUniversalTime();
            }

            items.Add(new ScrapeErrorLogItem(
                EventId: eventId,
                AttemptedAt: attemptedAt,
                Level: level,
                Reason: reason,
                Message: message,
                Scraper: scraper,
                CheckRunId: checkRunId,
                IssueUrl: GetString(element, "permalink")));
        }

        return items
            .OrderByDescending(x => x.AttemptedAt)
            .Take(limit)
            .ToList();
    }

    private static Dictionary<string, string> ParseTags(JsonElement element)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty("tags", out var tagsNode))
        {
            return tags;
        }

        if (tagsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagsNode.EnumerateArray())
            {
                if (tag.ValueKind != JsonValueKind.Object) continue;
                if (!tag.TryGetProperty("key", out var keyNode) || !tag.TryGetProperty("value", out var valueNode)) continue;
                var key = keyNode.GetString();
                var value = valueNode.GetString();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;
                tags[key] = value;
            }
            return tags;
        }

        if (tagsNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in tagsNode.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var value = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        tags[prop.Name] = value;
                    }
                }
            }
        }

        return tags;
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var node))
        {
            return null;
        }

        return node.ValueKind == JsonValueKind.String ? node.GetString() : null;
    }

    private SentrySettings GetSettings()
    {
        var enabled = configuration.GetValue("Sentry:Enabled", true);
        var baseUrl = configuration["Sentry:BaseUrl"] ?? "https://sentry.io/api/0";
        var orgSlug = configuration["Sentry:OrgSlug"];
        var projectSlug = configuration["Sentry:ProjectSlug"];
        var apiToken = configuration["Sentry:ApiToken"]
            ?? Environment.GetEnvironmentVariable("SENTRY_API_TOKEN");

        return new SentrySettings(
            Enabled: enabled,
            BaseUrl: baseUrl,
            OrgSlug: orgSlug,
            ProjectSlug: projectSlug,
            ApiToken: apiToken);
    }

    private sealed record SentrySettings(
        bool Enabled,
        string BaseUrl,
        string? OrgSlug,
        string? ProjectSlug,
        string? ApiToken);
}
