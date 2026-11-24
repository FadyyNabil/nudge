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

// Keep the signing keys in the database, so a restart or deploy does not log everyone out
// or break forms that were already open.
builder.Services.AddDataProtection().SetApplicationName("Sho8lana");
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
    new ConfigureOptions<KeyManagementOptions>(o => o.XmlRepository = new DbXmlRepository(sp.GetRequiredService<IServiceScopeFactory>())));

// ---- Database: SQL Server by default, SQLite as a zero-install option ----
var provider = builder.Configuration["Database:Provider"] ?? "SqlServer";
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite"));
    else
        options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"));
});

// ---- Password hashing (PBKDF2 with a random salt per password) ----
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<Marketplace.Services.PortfolioStorage>();

// ---- Cookie authentication ----
builder.Services.AddAuthentication("AuthCookie")
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

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // EnsureCreated does not alter an existing database, so add columns that were
    // introduced later to databases created before they existed. Each statement fails
    // harmlessly when the column is already there.
    var additions = provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)
        ? new[]
        {
            "ALTER TABLE Jobs ADD COLUMN PaymentMethod TEXT NULL",
            "ALTER TABLE Jobs ADD COLUMN Escrow TEXT NOT NULL DEFAULT 'None'",
            "ALTER TABLE Jobs ADD COLUMN PaidAtUtc TEXT NULL",
            "ALTER TABLE Jobs ADD COLUMN ReleasedAtUtc TEXT NULL",
            "ALTER TABLE Users ADD COLUMN AccountStatus TEXT NOT NULL DEFAULT 'Approved'",
            "ALTER TABLE Users ADD COLUMN PortfolioUrl TEXT NULL",
            "CREATE TABLE IF NOT EXISTS DataProtectionKeys (Id INTEGER PRIMARY KEY AUTOINCREMENT, FriendlyName TEXT NOT NULL, Xml TEXT NOT NULL)"
        }
        : new[]
        {
            "ALTER TABLE Jobs ADD PaymentMethod nvarchar(20) NULL",
            "ALTER TABLE Jobs ADD Escrow nvarchar(20) NOT NULL DEFAULT 'None'",
            "ALTER TABLE Jobs ADD PaidAtUtc datetime2 NULL",
            "ALTER TABLE Jobs ADD ReleasedAtUtc datetime2 NULL",
            "ALTER TABLE Users ADD AccountStatus nvarchar(20) NOT NULL DEFAULT 'Approved'",
            "ALTER TABLE Users ADD PortfolioUrl nvarchar(500) NULL",
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
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
