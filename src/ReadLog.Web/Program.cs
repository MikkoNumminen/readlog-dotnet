using Microsoft.EntityFrameworkCore;
using ReadLog.Web.Data;
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

app.UseAuthorization();

app.MapRazorPages();

app.Run();

// Exposed so the integration-test host (WebApplicationFactory<Program>) can boot the app.
public partial class Program;
