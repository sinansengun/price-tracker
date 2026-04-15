using System.Text;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PriceTracker.Data;
using PriceTracker.Models;
using PriceTracker.Services;
using PriceTracker.Services.Scrapers;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' not found.");

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key not found.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;

// EF Core + PostgreSQL
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(connectionString));

// ASP.NET Core Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(opt =>
{
    opt.Password.RequireDigit = false;
    opt.Password.RequireLowercase = false;
    opt.Password.RequireUppercase = false;
    opt.Password.RequireNonAlphanumeric = false;
    opt.Password.RequiredLength = 6;
    opt.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// JWT Authentication
builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(opt =>
{
    opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddAuthorization();

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

// Site scrapers
builder.Services.AddSingleton<PlaywrightService>();
builder.Services.AddScoped<ISiteScraper, HepsiburadaScraper>();
builder.Services.AddScoped<ISiteScraper, AmazonScraper>();
builder.Services.AddScoped<ISiteScraper, AbtSaatScraper>();
builder.Services.AddScoped<ISiteScraper, AydinSaatScraper>();
builder.Services.AddScoped<ISiteScraper, AslanSaatScraper>();
builder.Services.AddScoped<ISiteScraper, EdipSaatScraper>();

// App services
builder.Services.AddScoped<ScraperService>();
builder.Services.AddScoped<PriceCheckJob>();

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddControllers();

// Firebase Admin SDK
var firebaseCredPath = builder.Configuration["Firebase:CredentialPath"];
var firebaseCredJson = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIAL_JSON");
try
{
    GoogleCredential googleCredential;
    if (!string.IsNullOrEmpty(firebaseCredJson))
    {
        // Railway env variable'da private_key içindeki \n hem literal hem gerçek newline olabilir
        // Önce gerçek newline'ları kaldır (JSON string içinde olmamalı)
        firebaseCredJson = firebaseCredJson.Replace("\r\n", "").Replace("\r", "");
        // private_key dışındaki gerçek newline'ları temizle ama JSON yapısını koru
        firebaseCredJson = firebaseCredJson.Replace("\n", "");
        // Şimdi literal \n'ler private_key içinde doğru şekilde kalıyor
        googleCredential = GoogleCredential.FromJson(firebaseCredJson);
    }
    else if (!string.IsNullOrEmpty(firebaseCredPath) && File.Exists(firebaseCredPath))
        googleCredential = GoogleCredential.FromFile(firebaseCredPath);
    else
        googleCredential = GoogleCredential.GetApplicationDefault();

    FirebaseApp.Create(new AppOptions { Credential = googleCredential });
}
catch (Exception ex)
{
    var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
    startupLogger.LogWarning("Firebase başlatılamadı, push bildirimleri devre dışı: {Message}", ex.Message);
}

var app = builder.Build();

// Geçici debug endpoint – Firebase credential durumu
app.MapGet("/api/debug/firebase", () =>
{
    var json = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIAL_JSON");
    var hasKey = json?.Contains("private_key") ?? false;
    var hasRealNewline = json?.Contains('\n') ?? false; // gerçek newline
    var hasLiteralNewline = json?.Contains("\\n") ?? false; // literal \n
    var firebaseReady = FirebaseAdmin.FirebaseApp.DefaultInstance != null;
    return Results.Ok(new
    {
        envVarSet = !string.IsNullOrEmpty(json),
        envVarLength = json?.Length ?? 0,
        hasPrivateKey = hasKey,
        hasRealNewline,
        hasLiteralNewline,
        firebaseAppReady = firebaseReady
    });
});

// Migration'ları uygula
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Hangfire Dashboard (sadece development)
if (app.Environment.IsDevelopment())
    app.UseHangfireDashboard("/hangfire");

// Her 5 dakikada bir tüm ürünlerin fiyatını kontrol et
var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobManager.AddOrUpdate<PriceCheckJob>(
    "check-all-prices",
    job => job.CheckAllProductsAsync(),
    "0 8,12,16,20 * * *");

await app.RunAsync();
