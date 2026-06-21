using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReadLog.Web.Data;

namespace ReadLog.Tests.Infrastructure;

/// <summary>
/// Boots the real application for integration tests against an isolated, temporary
/// SQLite file (so tests never touch the developer's <c>readlog.db</c>). The app's
/// own wiring — including the startup migration — runs unchanged; only the
/// <see cref="ApplicationDbContext"/> registration is repointed at the temp file.
/// </summary>
public class ReadLogAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"readlog-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Replace the app's SQLite options with one pointed at the isolated temp file.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite($"Data Source={_dbPath}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            // Release pooled handles so the temp file can be removed.
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best-effort cleanup; the OS reclaims %TEMP% regardless.
                }
            }
        }
    }
}
