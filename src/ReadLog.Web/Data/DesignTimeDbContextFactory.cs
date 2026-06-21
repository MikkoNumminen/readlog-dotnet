using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ReadLog.Web.Data;

/// <summary>
/// Lets <c>dotnet ef</c> construct the context at design time without booting the app
/// (so <c>migrations add</c> never runs Program's startup migration). The connection
/// string here is only used by the tooling to know the provider — SQLite.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=readlog.db")
            .Options;

        return new ApplicationDbContext(options);
    }
}
