using ITEquipmentInventory.Data;
using ITEquipmentInventory.Configuration;
using ITEquipmentInventory.Services;
using ITEquipmentInventory.Services.Search;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// Ne koristi dijeljeni korisnički Data Protection spremnik. Ključevi iz njega
// mogu biti šifrirani DPAPI profilom drugog Windows korisnika, što sprječava
// pokretanje aplikacije u dotnet watchu.
builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));

var storageOptions = builder.Configuration
    .GetSection(StorageOptions.SectionName)
    .Get<StorageOptions>() ?? new StorageOptions();
var storagePaths = new StoragePaths(builder.Environment, Options.Create(storageOptions));
builder.Services.AddSingleton(storagePaths);

Directory.CreateDirectory(storagePaths.DataProtectionKeysPath);

var dataProtectionBuilder = builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(storagePaths.DataProtectionKeysPath))
    .SetApplicationName("ITEquipmentInventory");

if (OperatingSystem.IsWindows())
    dataProtectionBuilder.ProtectKeysWithDpapi();

// Vrijeme pokretanja aplikacije.
// Cookie koji je napravljen prije ovog pokretanja više neće vrijediti.
var appStartedAt = DateTimeOffset.UtcNow;

builder.Services.AddControllersWithViews(options =>
{
    // Globalna CSRF zaštita za sve POST/PUT/DELETE forme.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";

        // Sesija traje 3 sata.
        options.ExpireTimeSpan = TimeSpan.FromHours(3);
        options.SlidingExpiration = true;

        // Sigurnost cookieja.
        options.Cookie.Name = "ITEquipmentInventory.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.Cookie.SecurePolicy = builder.Environment.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;

        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var issuedUtc = context.Properties.IssuedUtc;

                // Ako je aplikacija ugašena i ponovno pokrenuta,
                // stari login cookie se odbacuje i korisnik mora opet na login.
                if (!issuedUtc.HasValue || issuedUtc.Value < appStartedAt)
                {
                    context.RejectPrincipal();

                    await context.HttpContext.SignOutAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme
                    );
                    return;
                }

                var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var securityStampClaim = context.Principal?.FindFirstValue(AppUser.SecurityStampClaimType);
                if (!int.TryParse(userIdClaim, out var userId) ||
                    string.IsNullOrWhiteSpace(securityStampClaim))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme
                    );
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (user is null || !user.IsActive ||
                    !string.Equals(user.SecurityStamp, securityStampClaim, StringComparison.Ordinal))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme
                    );
                }
            },

            OnRedirectToAccessDenied = context =>
            {
                context.Response.Redirect("/Account/AccessDenied");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Sve stranice po defaultu traže login,
    // osim onih koje imaju [AllowAnonymous].
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection nije konfiguriran.");

var sqliteConnection = new SqliteConnectionStringBuilder(connectionString);
if (string.IsNullOrWhiteSpace(sqliteConnection.DataSource))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection mora sadržavati Data Source.");

var dbPath = sqliteConnection.DataSource;
if (!string.Equals(dbPath, ":memory:", StringComparison.OrdinalIgnoreCase) &&
    !dbPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
{
    dbPath = storagePaths.ResolveFromContentRoot(dbPath);
    sqliteConnection.DataSource = dbPath;
}

var databaseDirectory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrWhiteSpace(databaseDirectory))
    Directory.CreateDirectory(databaseDirectory);

Directory.CreateDirectory(storagePaths.ProfileImagesPath);
Directory.CreateDirectory(storagePaths.ConsumableImagesPath);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(sqliteConnection.ConnectionString));

// Potrebno za sigurnosno logiranje.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SecurityAuditService>();
builder.Services.AddScoped<RecycleBinService>();
builder.Services.AddSingleton<ISearchNormalizer, SearchNormalizer>();
builder.Services.AddSingleton<ISearchSynonymProvider, SearchSynonymProvider>();
builder.Services.AddSingleton<ISearchQueryService, SearchQueryService>();
builder.Services.AddSingleton<ISearchQueryBuilder, SearchQueryBuilder>();
builder.Services.AddSingleton<ISearchFuzzyMatcher, SearchFuzzyMatcher>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Automatski primijeni migracije pri pokretanju.
    // Ako želiš još strože za produkciju, ovo možeš maknuti
    // i migracije pokretati ručno kroz terminal.
    db.Database.Migrate();

    // Stari zapisi vrste "Bubanj" više se ne nude u aplikaciji.
    // Ako ih je bilo, pretvaraju se u "Ostalo" nakon nove migracije.
    db.Database.ExecuteSqlRaw("UPDATE PrinterConsumables SET Type = 'Ostalo' WHERE Type = 'Bubanj';");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Sigurnosni HTTP headeri.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=(), payment=()";

    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self';";

    await next();
});

app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storagePaths.ProfileImagesPath),
    RequestPath = "/profile-images"
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/consumable-images/{fileName}", [Authorize] (string fileName) =>
{
    var safeName = Path.GetFileName(fileName);
    if (!string.Equals(fileName, safeName, StringComparison.Ordinal) ||
        !System.Text.RegularExpressions.Regex.IsMatch(
            safeName,
            @"^consumable-[0-9]+\.(jpg|jpeg|png|webp)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        return Results.NotFound();

    var fullPath = Path.Combine(storagePaths.ConsumableImagesPath, safeName);
    if (!File.Exists(fullPath))
        return Results.NotFound();

    var contentType = Path.GetExtension(safeName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };
    return Results.File(fullPath, contentType, enableRangeProcessing: false);
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
