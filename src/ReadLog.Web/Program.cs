var builder = WebApplication.CreateBuilder(args);

// --- Services -------------------------------------------------------------
// Razor Pages is the UI model for ReadLog. Data, integrations, auth and the
// domain services are registered in later chunks of the port.
builder.Services.AddRazorPages();

var app = builder.Build();

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
