using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ReadLog.Web.Auth;
using ReadLog.Web.Data;
using ReadLog.Web.Models;
using ReadLog.Web.Options;
using ReadLog.Web.Services;
using ReadLog.Web.Services.External;

var builder = WebApplication.CreateBuilder(args);

// --- Services -------------------------------------------------------------
builder.Services.AddRazorPages();
builder.Services.AddHealthChecks();

// Behind a TLS-terminating reverse proxy (Azure App Service), honour the original
// scheme/host so HTTPS redirection and auth cookies behave. The platform is the only
// ingress, so all proxies are trusted (KnownNetworks/Proxies cleared).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

// Wait out brief SQLite locks (the App Service /home SMB share can momentarily hold one)
// rather than throwing: Microsoft.Data.Sqlite retries SQLITE_BUSY until the command
// timeout elapses, so a generous default timeout behaves as a busy-timeout.
var sqliteConnectionString = new SqliteConnectionStringBuilder(connectionString)
{
    DefaultTimeout = 30,
}.ToString();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(sqliteConnectionString));

// Persist Data Protection keys next to the SQLite DB (the persistent /home share on App
// Service). Without this they default to ephemeral container storage and regenerate on
// every cold start — silently signing everyone out and breaking in-flight OAuth
// correlation. SetApplicationName pins the key ring so it survives restarts/redeploys.
var dataProtectionKeysDir = Path.Combine(
    Path.GetDirectoryName(Path.GetFullPath(
        new SqliteConnectionStringBuilder(connectionString).DataSource)) ?? ".",
    "keys");
Directory.CreateDirectory(dataProtectionKeysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir))
    .SetApplicationName("ReadLog");

// Authentication: ASP.NET Core Identity (local accounts) over the EF Core stores,
// plus an optional Google external login that registers only when configured.
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false; // demo: no email-confirmation step
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;

        // Throttle online password guessing: lock an account after repeated failures.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddClaimsPrincipalFactory<DisplayNameClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/signin";
    options.LogoutPath = "/signout";
    options.AccessDeniedPath = "/signin";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;

    // Production is HTTPS-only (Azure "HTTPS Only" + UseHttpsRedirection + forwarded
    // proto), so SameAsRequest issues the cookie Secure there while keeping it usable on
    // the plain-HTTP local dev/test host. HttpOnly and SameSite=Lax are the secure defaults.
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication().AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;

        // Capture the Google profile photo so the account page can show it.
        options.Events.OnCreatingTicket = context =>
        {
            if (context.Identity is not null
                && context.User.TryGetProperty("picture", out var picture)
                && picture.ValueKind == JsonValueKind.String)
            {
                context.Identity.AddClaim(new Claim("picture", picture.GetString()!));
            }

            return Task.CompletedTask;
        };
    });
}

// Book-search integrations: typed HttpClients + the search/details services.
builder.Services.Configure<GoogleBooksOptions>(
    builder.Configuration.GetSection(GoogleBooksOptions.SectionName));
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<IOpenLibraryClient, OpenLibraryClient>(client =>
{
    client.BaseAddress = new Uri("https://openlibrary.org/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "ReadLog/1.0 (+https://github.com/MikkoNumminen/readlog-dotnet)");
});
builder.Services.AddHttpClient<IGoogleBooksClient, GoogleBooksClient>(client =>
{
    client.BaseAddress = new Uri("https://www.googleapis.com/books/v1/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<IBookSearchService, BookSearchService>();
builder.Services.AddScoped<IBookDetailsService, BookDetailsService>();

// The reading-log domain service (logging, library, stats, public feed).
builder.Services.AddScoped<IReadLogService, ReadLogService>();

// Sanitises the (untrusted) Google Books description HTML before it's rendered.
builder.Services.AddSingleton(BookDescriptionSanitizer.Create());

var app = builder.Build();

// Make sure the SQLite database directory exists (e.g. /home/data on Azure App
// Service) before migrating, then apply pending migrations so a clean DB just works.
var dbDataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
var dbDirectory = Path.GetDirectoryName(Path.GetFullPath(dbDataSource));
if (!string.IsNullOrEmpty(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Log the resolved path so an operator can confirm from the log stream that the DB
    // is on the persistent /home share (and not ephemeral container storage).
    app.Logger.LogInformation("Applying EF Core migrations to SQLite database at {DatabasePath}",
        Path.GetFullPath(dbDataSource));

    // Cap the migration command timeout below the 30s connection default so a wedged share
    // surfaces a crash promptly (≈3×10s) instead of after 3×30s.
    db.Database.SetCommandTimeout(10);

    // First-boot on the SMB share can hit a transient lock/permission hiccup; retry a few
    // times, logging each failure, then rethrow on the last attempt (never swallow).
    const int maxMigrationAttempts = 3;
    for (var attempt = 1; attempt <= maxMigrationAttempts; attempt++)
    {
        try
        {
            db.Database.Migrate();
            app.Logger.LogInformation("Database migrations applied successfully.");
            break;
        }
        catch (Exception ex) when (attempt < maxMigrationAttempts)
        {
            app.Logger.LogWarning(ex,
                "Migration attempt {Attempt}/{Max} failed (transient lock or permission?); retrying.",
                attempt, maxMigrationAttempts);
            Thread.Sleep(TimeSpan.FromSeconds(2));
        }
    }
}

// --- HTTP pipeline --------------------------------------------------------
app.UseForwardedHeaders();

// Security response headers on every response. The UI loads only same-origin scripts and
// styles, so script-src is a strict 'self'; book covers come from external https hosts
// (img-src https:). 'unsafe-inline' is permitted for styles only, never for scripts.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Frame-Options"] = "DENY";
    headers["Content-Security-Policy"] =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
        "img-src 'self' https: data:; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        // form-action is enforced on the redirect target too: the "Sign in with Google" form
        // POSTs to /signin (self), which 302-redirects to accounts.google.com — so that host
        // must be allowed or the browser blocks the OAuth handoff.
        "form-action 'self' https://accounts.google.com";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Render the friendly Error page for status codes too (e.g. 404).
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// Cheap, DB-free liveness probe (kept off the migration/boot path).
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

// Exposed so the integration-test host (WebApplicationFactory<Program>) can boot the app.
public partial class Program;
