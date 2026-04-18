using System.Text;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
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
builder.Services.AddScoped<ISiteScraper, TrabzonsporScraper>();

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
string? firebaseProjectId = null;
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
        using var doc = JsonDocument.Parse(firebaseCredJson);
        if (doc.RootElement.TryGetProperty("project_id", out var projectIdElement))
            firebaseProjectId = projectIdElement.GetString();
    }
    else if (!string.IsNullOrEmpty(firebaseCredPath) && File.Exists(firebaseCredPath))
    {
        googleCredential = GoogleCredential.FromFile(firebaseCredPath);
        var fileJson = await File.ReadAllTextAsync(firebaseCredPath);
        using var doc = JsonDocument.Parse(fileJson);
        if (doc.RootElement.TryGetProperty("project_id", out var projectIdElement))
            firebaseProjectId = projectIdElement.GetString();
    }
    else
        googleCredential = GoogleCredential.GetApplicationDefault();

    // FCM v1 endpoint requires OAuth access token with proper scopes.
    googleCredential = googleCredential.CreateScoped(
        "https://www.googleapis.com/auth/firebase.messaging",
        "https://www.googleapis.com/auth/cloud-platform");

    FirebaseApp.Create(new AppOptions
    {
        Credential = googleCredential,
        ProjectId = firebaseProjectId
    });
}
catch (Exception ex)
{
    var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
    startupLogger.LogWarning("Firebase başlatılamadı, push bildirimleri devre dışı: {Message}", ex.Message);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Geçici debug endpoint – Firebase credential durumu
    app.MapGet("/api/debug/firebase", async () =>
    {
        var json = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIAL_JSON");
        var hasKey = json?.Contains("private_key") ?? false;
        var hasRealNewline = json?.Contains('\n') ?? false; // gerçek newline
        var hasLiteralNewline = json?.Contains("\\n") ?? false; // literal \n
        var firebaseReady = FirebaseAdmin.FirebaseApp.DefaultInstance != null;
        var appProjectId = FirebaseAdmin.FirebaseApp.DefaultInstance?.Options?.ProjectId;
        bool accessTokenAcquired = false;
        string? accessTokenError = null;

        if (firebaseReady)
        {
            try
            {
                var tokenProvider = FirebaseAdmin.FirebaseApp.DefaultInstance!.Options.Credential.UnderlyingCredential as ITokenAccess;
                var token = tokenProvider == null ? null : await tokenProvider.GetAccessTokenForRequestAsync();
                accessTokenAcquired = !string.IsNullOrWhiteSpace(token);
            }
            catch (Exception ex)
            {
                accessTokenError = ex.Message;
            }
        }

        return Results.Ok(new
        {
            envVarSet = !string.IsNullOrEmpty(json),
            envVarLength = json?.Length ?? 0,
            hasPrivateKey = hasKey,
            hasRealNewline,
            hasLiteralNewline,
            firebaseAppReady = firebaseReady,
            projectId = appProjectId,
            accessTokenAcquired,
            accessTokenError
        });
    });

    // Geçici debug endpoint – access token scope doğrulaması
    app.MapGet("/api/debug/firebase-tokeninfo", async () =>
    {
        if (FirebaseAdmin.FirebaseApp.DefaultInstance == null)
            return Results.BadRequest(new { error = "FirebaseApp not initialized" });

        try
        {
            var tokenProvider = FirebaseAdmin.FirebaseApp.DefaultInstance.Options.Credential.UnderlyingCredential as ITokenAccess;
            var accessToken = tokenProvider == null ? null : await tokenProvider.GetAccessTokenForRequestAsync();
            if (string.IsNullOrWhiteSpace(accessToken))
                return Results.BadRequest(new { error = "Access token could not be acquired" });

            using var http = new HttpClient();
            var tokenInfoUrl = $"https://oauth2.googleapis.com/tokeninfo?access_token={Uri.EscapeDataString(accessToken)}";
            var tokenInfoResponse = await http.GetAsync(tokenInfoUrl);
            var tokenInfoBody = await tokenInfoResponse.Content.ReadAsStringAsync();

            return Results.Ok(new
            {
                tokenInfoStatus = (int)tokenInfoResponse.StatusCode,
                tokenInfoBody
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    });

    // Geçici debug endpoint – kayıtlı tüm FCM token'lara test bildirimi gönder
    app.MapPost("/api/debug/push-test-all", async (AppDbContext db) =>
    {
        if (FirebaseApp.DefaultInstance == null)
            return Results.BadRequest(new { error = "FirebaseApp not initialized" });

        var tokens = await db.Users
            .Where(u => u.FcmToken != null && u.FcmToken != "")
            .Select(u => u.FcmToken!)
            .Distinct()
            .ToListAsync();

        if (tokens.Count == 0)
            return Results.NotFound(new { error = "No registered FCM tokens found" });

        var message = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification
            {
                Title = "Price Tracker Test",
                Body = $"Test push at {DateTime.UtcNow:HH:mm:ss} UTC"
            },
            Data = new Dictionary<string, string>
            {
                ["type"] = "debug_test",
                ["sentAtUtc"] = DateTime.UtcNow.ToString("O")
            }
        };

        var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);

        var failures = response.Responses
            .Select((r, i) => new { r, i })
            .Where(x => !x.r.IsSuccess)
            .Select(x => new
            {
                tokenPreview = tokens[x.i][..Math.Min(20, tokens[x.i].Length)],
                error = x.r.Exception?.Message
            })
            .ToList();

        return Results.Ok(new
        {
            attempted = tokens.Count,
            success = response.SuccessCount,
            failure = response.FailureCount,
            failures
        });
    });
}

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
