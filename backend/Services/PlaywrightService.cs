using Microsoft.Playwright;

namespace PriceTracker.Services;

/// <summary>
/// Gerçek bir Chromium browser instance'ı üzerinden HTML fetch eder.
/// TLS fingerprint / bot koruması olan siteleri (ör. abtsaat.com) atlatmak için kullanılır.
/// Singleton olarak kayıtlıdır; browser instance uygulama boyunca yeniden kullanılır.
/// </summary>
public sealed class PlaywrightService : IAsyncDisposable
{
    private readonly ILogger<PlaywrightService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser?    _browser;

    public PlaywrightService(ILogger<PlaywrightService> logger) => _logger = logger;

    private async Task<IBrowser> EnsureBrowserAsync()
    {
        if (_browser is { IsConnected: true }) return _browser;

        await _lock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true }) return _browser;

            _playwright ??= await Playwright.CreateAsync();
            _logger.LogInformation("Playwright: Chromium başlatılıyor...");

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args =
                [
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-features=IsolateOrigins,site-per-process"
                ]
            });

            _logger.LogInformation("Playwright: Chromium hazır.");
            return _browser;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Verilen URL'yi gerçek Chromium ile açar; DOM yüklendikten sonra
    /// sayfa HTML'ini döndürür. Başarısız olursa null döner.
    /// </summary>
    public async Task<string?> FetchHtmlAsync(string url, WaitUntilState waitUntil = WaitUntilState.DOMContentLoaded)
    {
        try
        {
            var browser = await EnsureBrowserAsync();

            await using var context = await CreateStealthContextAsync(browser);
            var page = await context.NewPageAsync();
            try
            {
                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = waitUntil,
                    Timeout   = 30_000
                });

                if (response == null || !response.Ok)
                {
                    _logger.LogWarning("Playwright: HTTP {Status} → {Url}", response?.Status, url);
                    return null;
                }

                var html = await page.ContentAsync();
                _logger.LogInformation("Playwright: {Len} karakter alındı ← {Url}", html.Length, url);
                return html;
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playwright: fetch başarısız → {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Sayfayı Chromium ile açar, belirli bir CSS selector'ın DOM'a eklenmesini bekler,
    /// ardından bir JS ifadesi çalıştırarak string sonuç döndürür.
    /// Kampanya fiyatları gibi istemcide render edilen veriler için kullanılır.
    /// </summary>
    public async Task<string?> EvaluateAfterSelectorAsync(string url, string waitSelector, string jsExpression, int timeoutMs = 15_000)
    {
        try
        {
            var browser = await EnsureBrowserAsync();
            await using var context = await CreateStealthContextAsync(browser);
            var page = await context.NewPageAsync();
            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout   = 30_000
                });

                // Belirtilen element'in DOM'a eklenmesini bekle
                await page.WaitForSelectorAsync(waitSelector, new PageWaitForSelectorOptions
                {
                    State   = WaitForSelectorState.Attached,
                    Timeout = timeoutMs
                });

                var result = await page.EvaluateAsync<string?>(jsExpression);
                _logger.LogInformation("Playwright JS eval sonucu: {Result} ← {Url}", result, url);
                return result;
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playwright: EvaluateAfterSelector başarısız → {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Önce warmupUrl'ye gidip cookie/session oluşturur, ardından targetUrl'ye navigate edip
    /// selector bekler ve JS çalıştırır. 403 dönen sitelerde cookie warming ile bypass sağlar.
    /// </summary>
    public async Task<string?> EvaluateWithWarmupAsync(
        string warmupUrl, string targetUrl,
        string waitSelector, string jsExpression,
        int timeoutMs = 15_000)
    {
        try
        {
            var browser = await EnsureBrowserAsync();
            await using var context = await CreateStealthContextAsync(browser);
            var page = await context.NewPageAsync();
            try
            {
                // 1. Cookie warming — anasayfayı ziyaret et
                _logger.LogInformation("Playwright warmup: {Warmup}", warmupUrl);
                await page.GotoAsync(warmupUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout   = 15_000
                });
                await Task.Delay(Random.Shared.Next(800, 1500));

                // 2. Ürün sayfasına git
                var response = await page.GotoAsync(targetUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout   = 30_000
                });

                if (response == null || !response.Ok)
                {
                    _logger.LogWarning("Playwright warmup: HTTP {Status} → {Url}", response?.Status, targetUrl);
                    return null;
                }

                // 3. Selector bekle
                await page.WaitForSelectorAsync(waitSelector, new PageWaitForSelectorOptions
                {
                    State   = WaitForSelectorState.Attached,
                    Timeout = timeoutMs
                });

                // 4. JS çalıştır
                var result = await page.EvaluateAsync<string?>(jsExpression);
                _logger.LogInformation("Playwright warmup JS eval: {Result} ← {Url}", result, targetUrl);
                return result;
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Playwright warmup: başarısız → {Url}", targetUrl);
            return null;
        }
    }

    /// <summary>
    /// Bot algılamasını atlatmak için stealth ayarlı bir BrowserContext oluşturur.
    /// navigator.webdriver = false, gerçekçi viewport, WebGL vendor vb.
    /// </summary>
    private static async Task<IBrowserContext> CreateStealthContextAsync(IBrowser browser)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                        "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            Locale       = "tr-TR",
            TimezoneId   = "Europe/Istanbul",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7",
                ["sec-ch-ua"]       = "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
                ["sec-ch-ua-mobile"]   = "?0",
                ["sec-ch-ua-platform"] = "\"Windows\""
            }
        });

        // navigator.webdriver = true → false; headless sinyallerini gizle
        await context.AddInitScriptAsync(@"
            Object.defineProperty(navigator, 'webdriver', { get: () => false });
            Object.defineProperty(navigator, 'languages', { get: () => ['tr-TR', 'tr', 'en-US', 'en'] });
            Object.defineProperty(navigator, 'plugins',   { get: () => [1, 2, 3, 4, 5] });
            window.chrome = { runtime: {} };
        ");

        return context;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.DisposeAsync();
            _browser = null;
        }
        _playwright?.Dispose();
        _playwright = null;
    }
}
