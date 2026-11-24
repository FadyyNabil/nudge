using System.Security.Claims;
using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

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
                     await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive && u.Role == role);
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

    // EnsureCreated does not alter an existing database, so add the PaymentMethod column
    // to databases created before it existed. Fails harmlessly when the column is already there.
    try
    {
        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            db.Database.ExecuteSqlRaw("ALTER TABLE Jobs ADD COLUMN PaymentMethod TEXT NULL");
        else
            db.Database.ExecuteSqlRaw("ALTER TABLE Jobs ADD PaymentMethod nvarchar(20) NULL");
    }
    catch { }

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
