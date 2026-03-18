using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Services;
using PriceTracker.Services.Scrapers;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' not found.");

// EF Core + PostgreSQL
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(connectionString));

// Hangfire (job storage olarak PostgreSQL)
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(opt => opt.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

// HttpClient - HTML scraper (browser-like headers + auto decompression)
builder.Services.AddHttpClient("Scraper")
    .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip
            | System.Net.DecompressionMethods.Deflate
            | System.Net.DecompressionMethods.Brotli,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        UseCookies = true,
        CookieContainer = new System.Net.CookieContainer()
    });

// Site scrapers — yeni bir site eklemek için buraya ISiteScraper implementasyonu kaydet
builder.Services.AddSingleton<PlaywrightService>();
builder.Services.AddScoped<ISiteScraper, HepsiburadaScraper>();
builder.Services.AddScoped<ISiteScraper, AmazonScraper>();
builder.Services.AddScoped<ISiteScraper, AbtSaatScraper>();
builder.Services.AddScoped<ISiteScraper, AydinSaatScraper>();

// App services
builder.Services.AddScoped<ScraperService>();
builder.Services.AddScoped<PriceCheckJob>();

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers();

var app = builder.Build();

// Migration'ları uygula
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();

// Hangfire Dashboard (sadece development)
if (app.Environment.IsDevelopment())
    app.UseHangfireDashboard("/hangfire");

// Her 6 saatte bir tüm ürünlerin fiyatını kontrol et
RecurringJob.AddOrUpdate<PriceCheckJob>(
    "check-all-prices",
    job => job.CheckAllProductsAsync(),
    "0 */6 * * *");

await app.RunAsync();
