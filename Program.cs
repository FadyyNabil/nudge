using System.Security.Claims;
using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// ---- Logging: console plus a rolling file (App_Data/logs/app-YYYYMMDD.log), warnings and worse by default ----
{
    var logPath = builder.Configuration["Logging:File:Path"] ?? "App_Data/logs";
    var logDir = Path.IsPathRooted(logPath) ? logPath : Path.Combine(builder.Environment.ContentRootPath, logPath);
    var minLevel = Enum.TryParse<LogLevel>(builder.Configuration["Logging:File:MinLevel"], true, out var lv) ? lv : LogLevel.Warning;
    builder.Logging.AddProvider(new Marketplace.Services.FileLoggerProvider(logDir, minLevel));
}

// ---- Upload ceiling: files are capped at 5 MB in code; reject oversized bodies early ----
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 7 * 1024 * 1024;
    o.ValueLengthLimit = 64 * 1024;
});

// ---- CORS: only our own public origins may call us from a browser on another site ----
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"]
        ?? "https://sho8lana.fady-n-gwargeous.workers.dev,http://sho8lana.runasp.net,https://sho8lana.runasp.net")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST")
    .WithHeaders("Content-Type", "RequestVerificationToken")));

// Keep the signing keys in the database, so a restart or deploy does not log everyone out
// or break forms that were already open.
builder.Services.AddDataProtection().SetApplicationName("Sho8lana");
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
    new ConfigureOptions<KeyManagementOptions>(o => o.XmlRepository = new DbXmlRepository(sp.GetRequiredService<IServiceScopeFactory>())));

// ---- Database: SQL Server by default, SQLite as a zero-install option ----
var pgConnection = builder.Configuration.GetConnectionString("Postgres");
var provider = !string.IsNullOrWhiteSpace(pgConnection) ? "Postgres" : (builder.Configuration["Database:Provider"] ?? "SqlServer");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider == "Postgres")
        options.UseNpgsql(pgConnection);
    else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite"));
    else
        options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"));
});

// ---- Password hashing (PBKDF2 with a random salt per password) ----
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<Marketplace.Services.PortfolioStorage>();
builder.Services.AddSingleton<Marketplace.Services.Mailer>();
builder.Services.AddSingleton<Marketplace.Services.EmailVerifier>();
builder.Services.AddHostedService<Marketplace.Services.AutoReleaseService>();

// ---- Cookie authentication ----
var auth = builder.Services.AddAuthentication("AuthCookie")
    .AddCookie("AuthCookie", options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";   // logged in but wrong role
        options.Cookie.Name = "Marketplace.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;

        // On every request, check the database: is this account still active and is the
        // role in the cookie still correct? This makes "suspend user" take effect at once.
        options.Events.OnValidatePrincipal = async ctx =>
        {
            var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = ctx.Principal?.FindFirstValue(ClaimTypes.Role);
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var ok = int.TryParse(id, out var userId) &&
                     await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive && u.Role == role && u.AccountStatus == ApprovalStatus.Approved);
            if (!ok)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync("AuthCookie");
            }
        };
    });

// Temporary cookie that carries the Google/LinkedIn result back to our callback page.
auth.AddCookie("ExternalCookie");

// ---- Social login: active only when the owner adds that provider's keys ----
// (Light Cloud environment variables, e.g. Authentication__Google__ClientId).
// Without keys the login page simply does not show the button.
var googleId = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret))
    auth.AddGoogle(options =>
    {
        options.ClientId = googleId;
        options.ClientSecret = googleSecret;
        options.SignInScheme = "ExternalCookie";
    });

var linkedInId = builder.Configuration["Authentication:LinkedIn:ClientId"];
var linkedInSecret = builder.Configuration["Authentication:LinkedIn:ClientSecret"];
if (!string.IsNullOrWhiteSpace(linkedInId) && !string.IsNullOrWhiteSpace(linkedInSecret))
    auth.AddLinkedIn(options =>
    {
        options.ClientId = linkedInId;
        options.ClientSecret = linkedInSecret;
        options.SignInScheme = "ExternalCookie";
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Behind the Light Cloud / Cloudflare proxy the app sees the internal host, which
// breaks OAuth redirect URIs. When PublicHost is configured, force the public
// scheme and host so providers build callbacks on the public URL.
var publicHost = app.Configuration["PublicHost"];
if (!string.IsNullOrEmpty(publicHost))
{
    app.Use(async (context, next) =>
    {
        context.Request.Scheme = "https";
        context.Request.Host = new Microsoft.AspNetCore.Http.HostString(publicHost);
        await next();
    });
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // EnsureCreated does not alter an existing database, so add columns that were
    // introduced later to databases created before they existed. Each statement fails
    // harmlessly when the column is already there.
    var additions = provider == "Postgres"
        ? new[]
        {
            "ALTER TABLE \"Users\" ADD COLUMN \"InstaPayNumber\" varchar(100) NULL",
            "ALTER TABLE \"Users\" ADD COLUMN \"CvFileName\" varchar(260) NULL",
            "ALTER TABLE \"Users\" ADD COLUMN \"CvContentType\" varchar(100) NULL",
            "ALTER TABLE \"Users\" ADD COLUMN \"CvData\" bytea NULL",
            "ALTER TABLE \"Jobs\" ADD COLUMN \"DeliveredAtUtc\" timestamptz NULL",
            "ALTER TABLE \"Jobs\" ADD COLUMN \"ChangeRequestNote\" varchar(1000) NULL",
            "CREATE TABLE IF NOT EXISTS \"DeliveryFiles\" (\"Id\" serial PRIMARY KEY, \"JobId\" integer NOT NULL, \"FreelancerId\" integer NOT NULL, \"OriginalFileName\" varchar(260) NOT NULL, \"ContentType\" varchar(100) NOT NULL, \"Data\" bytea NOT NULL, \"UploadedAtUtc\" timestamptz NOT NULL)"
        }
        : provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)
        ? new[]
        {
            "ALTER TABLE Jobs ADD COLUMN PaymentMethod TEXT NULL",
            "ALTER TABLE Jobs ADD COLUMN Escrow TEXT NOT NULL DEFAULT 'None'",
            "ALTER TABLE Jobs ADD COLUMN PaidAtUtc TEXT NULL",
            "ALTER TABLE Jobs ADD COLUMN ReleasedAtUtc TEXT NULL",
            "ALTER TABLE Jobs ADD COLUMN DeliveredAtUtc TEXT NULL",
            "ALTER TABLE Jobs ADD COLUMN ChangeRequestNote TEXT NULL",
            "ALTER TABLE Users ADD COLUMN AccountStatus TEXT NOT NULL DEFAULT 'Approved'",
            "ALTER TABLE Users ADD COLUMN PortfolioUrl TEXT NULL",
            "ALTER TABLE Users ADD COLUMN InstaPayNumber TEXT NULL",
            "ALTER TABLE Users ADD COLUMN CvFileName TEXT NULL",
            "ALTER TABLE Users ADD COLUMN CvContentType TEXT NULL",
            "ALTER TABLE Users ADD COLUMN CvData BLOB NULL",
            "CREATE TABLE IF NOT EXISTS DeliveryFiles (Id INTEGER PRIMARY KEY AUTOINCREMENT, JobId INTEGER NOT NULL, FreelancerId INTEGER NOT NULL, OriginalFileName TEXT NOT NULL, ContentType TEXT NOT NULL, Data BLOB NOT NULL, UploadedAtUtc TEXT NOT NULL)",
            "CREATE TABLE IF NOT EXISTS DataProtectionKeys (Id INTEGER PRIMARY KEY AUTOINCREMENT, FriendlyName TEXT NOT NULL, Xml TEXT NOT NULL)"
        }
        : new[]
        {
            "ALTER TABLE Jobs ADD PaymentMethod nvarchar(20) NULL",
            "ALTER TABLE Jobs ADD Escrow nvarchar(20) NOT NULL DEFAULT 'None'",
            "ALTER TABLE Jobs ADD PaidAtUtc datetime2 NULL",
            "ALTER TABLE Jobs ADD ReleasedAtUtc datetime2 NULL",
            "ALTER TABLE Jobs ADD DeliveredAtUtc datetime2 NULL",
            "ALTER TABLE Jobs ADD ChangeRequestNote nvarchar(1000) NULL",
            "ALTER TABLE Users ADD AccountStatus nvarchar(20) NOT NULL DEFAULT 'Approved'",
            "ALTER TABLE Users ADD PortfolioUrl nvarchar(500) NULL",
            "ALTER TABLE Users ADD InstaPayNumber nvarchar(100) NULL",
            "ALTER TABLE Users ADD CvFileName nvarchar(260) NULL",
            "ALTER TABLE Users ADD CvContentType nvarchar(100) NULL",
            "ALTER TABLE Users ADD CvData varbinary(max) NULL",
            "IF OBJECT_ID(N'DeliveryFiles') IS NULL CREATE TABLE DeliveryFiles (Id int IDENTITY(1,1) PRIMARY KEY, JobId int NOT NULL, FreelancerId int NOT NULL, OriginalFileName nvarchar(260) NOT NULL, ContentType nvarchar(100) NOT NULL, Data varbinary(max) NOT NULL, UploadedAtUtc datetime2 NOT NULL)",
            "IF OBJECT_ID(N'DataProtectionKeys') IS NULL CREATE TABLE DataProtectionKeys (Id int IDENTITY(1,1) PRIMARY KEY, FriendlyName nvarchar(200) NOT NULL, Xml nvarchar(max) NOT NULL)"
        };
    foreach (var sql in additions)
    {
        try { db.Database.ExecuteSqlRaw(sql); }
        catch { }
    }

    // ---- Seed the owner (admin) account ----
    // Credentials come from configuration (user-secrets or environment variables),
    // never from source code. Nobody can become admin through the sign-up form.
    var adminEmail = builder.Configuration["Admin:Email"]?.Trim().ToLowerInvariant();
    var adminPassword = builder.Configuration["Admin:Password"];
    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrEmpty(adminPassword)
        && !db.Users.Any(u => u.Email == adminEmail))
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var admin = new User
        {
            FullName = builder.Configuration["Admin:FullName"] ?? "Site Owner",
            Email = adminEmail,
            Role = Roles.Admin
        };
        admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
        db.Users.Add(admin);
        db.SaveChanges();
    }
}

// ---- Standard security headers on every response ----
app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    h["Content-Security-Policy"] = "frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/Error/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseAuthentication();   // who are you?
app.UseAuthorization();    // what may you open?
app.MapRazorPages();
app.Run();

/// <summary>Stores ASP.NET Core data-protection keys as rows in the app database.</summary>
class DbXmlRepository : IXmlRepository
{
    private readonly IServiceScopeFactory _scopes;
    public DbXmlRepository(IServiceScopeFactory scopes) => _scopes = scopes;

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.DataProtectionKeys.AsNoTracking().OrderBy(k => k.Id).ToList()
            .Select(k => XElement.Parse(k.Xml)).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = friendlyName, Xml = element.ToString(SaveOptions.DisableFormatting) });
        db.SaveChanges();
    }
}
