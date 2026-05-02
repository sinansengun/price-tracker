using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

namespace PriceTracker.Services;

public class FirebaseRemoteConfigService(
    IConfiguration configuration,
    ILogger<FirebaseRemoteConfigService> logger)
{
    private static readonly HttpClient RemoteConfigHttp = new();
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    private decimal cachedMinDropPercent;
    private DateTime cacheExpiresAtUtc = DateTime.MinValue;
    private bool cacheInitialized;

    private string ThresholdParameterKey =>
        configuration["Firebase:RemoteConfig:PriceDropThresholdPercentParameter"]
        ?? "price_drop_min_percent";

    private decimal FallbackThresholdPercent =>
        ParseThreshold(configuration["Firebase:RemoteConfig:FallbackPriceDropThresholdPercent"], 1m);

    private int CacheMinutes =>
        Math.Clamp(configuration.GetValue("Firebase:RemoteConfig:CacheMinutes", 5), 1, 60);

    public async Task<decimal> GetMinPriceDropPercentAsync()
    {
        if (cacheInitialized && DateTime.UtcNow < cacheExpiresAtUtc)
            return cachedMinDropPercent;

        await refreshLock.WaitAsync();
        try
        {
            if (cacheInitialized && DateTime.UtcNow < cacheExpiresAtUtc)
                return cachedMinDropPercent;

            var remoteValue = await FetchMinPriceDropPercentFromRemoteConfigAsync();
            cachedMinDropPercent = Math.Max(0m, remoteValue ?? FallbackThresholdPercent);
            cacheExpiresAtUtc = DateTime.UtcNow.AddMinutes(CacheMinutes);
            cacheInitialized = true;

            return cachedMinDropPercent;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task<decimal?> FetchMinPriceDropPercentFromRemoteConfigAsync()
    {
        var app = FirebaseApp.DefaultInstance;
        if (app == null)
        {
            logger.LogDebug("Remote Config okunamadı: FirebaseApp hazır değil.");
            return null;
        }

        var projectId = app.Options.ProjectId;
        if (string.IsNullOrWhiteSpace(projectId))
        {
            logger.LogWarning("Remote Config okunamadı: Firebase ProjectId bulunamadı.");
            return null;
        }

        var tokenProvider = app.Options.Credential.UnderlyingCredential as ITokenAccess;
        var accessToken = tokenProvider == null
            ? null
            : await tokenProvider.GetAccessTokenForRequestAsync();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            logger.LogWarning("Remote Config okunamadı: access token alınamadı.");
            return null;
        }

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://firebaseremoteconfig.googleapis.com/v1/projects/{projectId}/remoteConfig");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await RemoteConfigHttp.SendAsync(request);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Remote Config isteği başarısız.");
            return null;
        }

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Remote Config okunamadı. status={StatusCode} body={Body}",
                (int)response.StatusCode,
                body);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("parameters", out var parameters))
            {
                logger.LogWarning("Remote Config içinde 'parameters' bulunamadı.");
                return null;
            }

            if (!parameters.TryGetProperty(ThresholdParameterKey, out var thresholdParameter))
            {
                logger.LogWarning(
                    "Remote Config parametresi bulunamadı: {ParameterKey}",
                    ThresholdParameterKey);
                return null;
            }

            var rawValue = ReadParameterValue(thresholdParameter);
            if (!TryParseThreshold(rawValue, out var threshold))
            {
                logger.LogWarning(
                    "Remote Config parametresi parse edilemedi. key={ParameterKey}, value={Value}",
                    ThresholdParameterKey,
                    rawValue);
                return null;
            }

            return threshold;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Remote Config cevabı parse edilemedi.");
            return null;
        }
    }

    private static string? ReadParameterValue(JsonElement parameter)
    {
        if (parameter.TryGetProperty("defaultValue", out var defaultValue)
            && defaultValue.TryGetProperty("value", out var defaultValueValue)
            && defaultValueValue.ValueKind == JsonValueKind.String)
            return defaultValueValue.GetString();

        if (parameter.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.String)
            return value.GetString();

        return null;
    }

    private static decimal ParseThreshold(string? raw, decimal fallback)
    {
        return TryParseThreshold(raw, out var value)
            ? Math.Max(0m, value)
            : fallback;
    }

    private static bool TryParseThreshold(string? raw, out decimal value)
    {
        var normalized = raw?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            value = 0m;
            return false;
        }

        normalized = normalized.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}