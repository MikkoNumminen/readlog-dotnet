using Microsoft.AspNetCore.Identity;
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

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

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
});

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication().AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
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
        "ReadLog/1.0 (+https://github.com/MikkoNumminen/Readlog-c-.net)");
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

var app = builder.Build();

// Apply pending EF Core migrations at startup so a clean database just works.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// --- HTTP pipeline --------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();

// Exposed so the integration-test host (WebApplicationFactory<Program>) can boot the app.
public partial class Program;
