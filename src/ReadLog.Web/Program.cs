using Microsoft.EntityFrameworkCore;
using ReadLog.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// --- Services -------------------------------------------------------------
builder.Services.AddRazorPages();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

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
