using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ReadLog.Web.Data;

namespace ReadLog.Tests.Infrastructure;

/// <summary>
/// A throwaway SQLite database held in memory for the lifetime of the instance.
/// The single connection is kept open so the schema (built from the real EF
/// migrations) survives across the multiple contexts a test creates. Foreign-key
/// enforcement is enabled so cascade/restrict behaviour matches the file database.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.Migrate();
    }

    /// <summary>A fresh context over the shared in-memory database.</summary>
    public ApplicationDbContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
