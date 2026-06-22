using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ReadLog.Tests.Infrastructure;
using ReadLog.Web.Data;
using ReadLog.Web.Dtos;
using ReadLog.Web.Models;
using ReadLog.Web.Services;

namespace ReadLog.Tests.Services;

public class ReadLogServiceTests
{
    private static ReadLogService Service(ApplicationDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<ReadLogService>.Instance);

    private static async Task<string> SeedUserAsync(ApplicationDbContext db, string email)
    {
        var user = new ApplicationUser { UserName = email, Email = email };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static LogBookRequest Request(
        string openLibraryId, string title, DateOnly finishedAt,
        Format format = Format.Book, int? rating = null, string? author = null) =>
        new()
        {
            OpenLibraryId = openLibraryId,
            Title = title,
            FinishedAt = finishedAt,
            Format = format,
            Rating = rating,
            Author = author,
        };

    [Fact]
    public async Task LogBookAsync_creates_the_book_and_the_entry()
    {
        using var sqlite = new SqliteTestDatabase();
        string userId;
        using (var db = sqlite.CreateContext())
        {
            userId = await SeedUserAsync(db, "a@example.com");
            await Service(db).LogBookAsync(userId, Request("/works/OL1W", "Dune", new DateOnly(2024, 1, 1), rating: 5));
        }

        using (var db = sqlite.CreateContext())
        {
            var book = Assert.Single(db.Books);
            Assert.Equal("Dune", book.Title);
            var entry = Assert.Single(db.ReadEntries);
            Assert.Equal(userId, entry.UserId);
            Assert.Equal(book.Id, entry.BookId);
            Assert.Equal(5, entry.Rating);
        }
    }

    [Fact]
    public async Task LogBookAsync_reuses_an_existing_book_and_keeps_the_first_metadata()
    {
        using var sqlite = new SqliteTestDatabase();
        using (var db = sqlite.CreateContext())
        {
            var userId = await SeedUserAsync(db, "a@example.com");
            var svc = Service(db);
            await svc.LogBookAsync(userId, Request("/works/OL1W", "Dune", new DateOnly(2024, 1, 1)));
            await svc.LogBookAsync(userId, Request("/works/OL1W", "A different title", new DateOnly(2024, 2, 2)));
        }

        using (var db = sqlite.CreateContext())
        {
            var book = Assert.Single(db.Books); // one catalogue row reused
            Assert.Equal("Dune", book.Title);   // first logger's metadata wins
            Assert.Equal(2, await db.ReadEntries.CountAsync());
        }
    }

    [Fact]
    public async Task GetMyBooksAsync_returns_only_the_users_entries_newest_finished_first()
    {
        using var sqlite = new SqliteTestDatabase();
        string mine;
        using (var db = sqlite.CreateContext())
        {
            mine = await SeedUserAsync(db, "me@example.com");
            var other = await SeedUserAsync(db, "other@example.com");
            var svc = Service(db);
            await svc.LogBookAsync(mine, Request("ol:1", "Older", new DateOnly(2024, 1, 1)));
            await svc.LogBookAsync(mine, Request("ol:2", "Newer", new DateOnly(2024, 6, 1)));
            await svc.LogBookAsync(other, Request("ol:3", "Theirs", new DateOnly(2024, 7, 1)));
        }

        using (var db = sqlite.CreateContext())
        {
            var books = await Service(db).GetMyBooksAsync(mine);
            Assert.Collection(books,
                b => Assert.Equal("Newer", b.Book.Title),
                b => Assert.Equal("Older", b.Book.Title));
        }
    }

    [Fact]
    public async Task CheckIfReadAsync_matches_titles_case_insensitively_for_the_user_only()
    {
        using var sqlite = new SqliteTestDatabase();
        string mine;
        using (var db = sqlite.CreateContext())
        {
            mine = await SeedUserAsync(db, "me@example.com");
            var other = await SeedUserAsync(db, "other@example.com");
            var svc = Service(db);
            await svc.LogBookAsync(mine, Request("ol:1", "Dune", new DateOnly(2024, 1, 1)));
            await svc.LogBookAsync(other, Request("ol:2", "Dune Messiah", new DateOnly(2024, 2, 1)));
        }

        using (var db = sqlite.CreateContext())
        {
            var results = await Service(db).CheckIfReadAsync(mine, "dUnE");
            var hit = Assert.Single(results);
            Assert.Equal("Dune", hit.Book.Title);
        }
    }

    [Fact]
    public async Task CheckIfReadAsync_returns_empty_for_a_blank_query()
    {
        using var sqlite = new SqliteTestDatabase();
        using var db = sqlite.CreateContext();
        var userId = await SeedUserAsync(db, "me@example.com");
        await Service(db).LogBookAsync(userId, Request("ol:1", "Dune", new DateOnly(2024, 1, 1)));

        Assert.Empty(await Service(db).CheckIfReadAsync(userId, "   "));
    }

    [Fact]
    public async Task CheckIfReadAsync_treats_like_wildcards_literally()
    {
        using var sqlite = new SqliteTestDatabase();
        using var db = sqlite.CreateContext();
        var userId = await SeedUserAsync(db, "me@example.com");
        await Service(db).LogBookAsync(userId, Request("ol:1", "Dune", new DateOnly(2024, 1, 1)));

        // "%" must not behave as a wildcard that matches everything.
        Assert.Empty(await Service(db).CheckIfReadAsync(userId, "%"));
    }

    [Fact]
    public async Task UpdateReadEntryAsync_updates_the_entry_and_the_book_title()
    {
        using var sqlite = new SqliteTestDatabase();
        string userId;
        int entryId;
        using (var db = sqlite.CreateContext())
        {
            userId = await SeedUserAsync(db, "me@example.com");
            await Service(db).LogBookAsync(userId, Request("ol:1", "Dune", new DateOnly(2024, 1, 1), rating: 3));
            entryId = (await db.ReadEntries.SingleAsync()).Id;
        }

        bool updated;
        using (var db = sqlite.CreateContext())
        {
            updated = await Service(db).UpdateReadEntryAsync(userId, entryId, new UpdateReadEntryRequest
            {
                Title = "Dune (revised)",
                Format = Format.Audiobook,
                FinishedAt = new DateOnly(2024, 3, 3),
                Rating = 5,
            });
        }

        Assert.True(updated);
        using (var db = sqlite.CreateContext())
        {
            var entry = await db.ReadEntries.Include(e => e.Book).SingleAsync();
            Assert.Equal(Format.Audiobook, entry.Format);
            Assert.Equal(new DateOnly(2024, 3, 3), entry.FinishedAt);
            Assert.Equal(5, entry.Rating);
            Assert.Equal("Dune (revised)", entry.Book.Title);
        }
    }

    [Fact]
    public async Task UpdateReadEntryAsync_can_clear_the_rating_with_null()
    {
        using var sqlite = new SqliteTestDatabase();
        string userId;
        int entryId;
        using (var db = sqlite.CreateContext())
        {
            userId = await SeedUserAsync(db, "me@example.com");
            await Service(db).LogBookAsync(userId, Request("ol:1", "Dune", new DateOnly(2024, 1, 1), rating: 4));
            entryId = (await db.ReadEntries.SingleAsync()).Id;
        }

        using (var db = sqlite.CreateContext())
        {
            await Service(db).UpdateReadEntryAsync(userId, entryId, new UpdateReadEntryRequest
            {
                Title = "Dune",
                Format = Format.Book,
                FinishedAt = new DateOnly(2024, 1, 1),
                Rating = null,
            });
        }

        using (var db = sqlite.CreateContext())
        {
            Assert.Null((await db.ReadEntries.SingleAsync()).Rating);
        }
    }

    [Fact]
    public async Task UpdateReadEntryAsync_returns_false_for_another_users_entry()
    {
        using var sqlite = new SqliteTestDatabase();
        string owner;
        string stranger;
        int entryId;
        using (var db = sqlite.CreateContext())
        {
            owner = await SeedUserAsync(db, "owner@example.com");
            stranger = await SeedUserAsync(db, "stranger@example.com");
            await Service(db).LogBookAsync(owner, Request("ol:1", "Dune", new DateOnly(2024, 1, 1), rating: 3));
            entryId = (await db.ReadEntries.SingleAsync()).Id;
        }

        bool updated;
        using (var db = sqlite.CreateContext())
        {
            updated = await Service(db).UpdateReadEntryAsync(stranger, entryId, new UpdateReadEntryRequest
            {
                Title = "Hijacked",
                Format = Format.Ebook,
                FinishedAt = new DateOnly(2024, 9, 9),
                Rating = 0,
            });
        }

        Assert.False(updated); // 404, not 403 — and unchanged
        using (var db = sqlite.CreateContext())
        {
            var entry = await db.ReadEntries.Include(e => e.Book).SingleAsync();
            Assert.Equal("Dune", entry.Book.Title);
            Assert.Equal(3, entry.Rating);
        }
    }

    [Fact]
    public async Task DeleteReadEntryAsync_removes_the_entry_but_keeps_the_book()
    {
        using var sqlite = new SqliteTestDatabase();
        string userId;
        int entryId;
        using (var db = sqlite.CreateContext())
        {
            userId = await SeedUserAsync(db, "me@example.com");
            await Service(db).LogBookAsync(userId, Request("ol:1", "Dune", new DateOnly(2024, 1, 1)));
            entryId = (await db.ReadEntries.SingleAsync()).Id;
        }

        bool deleted;
        using (var db = sqlite.CreateContext())
        {
            deleted = await Service(db).DeleteReadEntryAsync(userId, entryId);
        }

        Assert.True(deleted);
        using (var db = sqlite.CreateContext())
        {
            Assert.Empty(db.ReadEntries);
            Assert.Single(db.Books);
        }
    }

    [Fact]
    public async Task DeleteReadEntryAsync_returns_false_for_another_users_entry()
    {
        using var sqlite = new SqliteTestDatabase();
        string owner;
        string stranger;
        int entryId;
        using (var db = sqlite.CreateContext())
        {
            owner = await SeedUserAsync(db, "owner@example.com");
            stranger = await SeedUserAsync(db, "stranger@example.com");
            await Service(db).LogBookAsync(owner, Request("ol:1", "Dune", new DateOnly(2024, 1, 1)));
            entryId = (await db.ReadEntries.SingleAsync()).Id;
        }

        using (var db = sqlite.CreateContext())
        {
            Assert.False(await Service(db).DeleteReadEntryAsync(stranger, entryId));
        }

        using (var db = sqlite.CreateContext())
        {
            Assert.Single(db.ReadEntries); // untouched
        }
    }

    [Fact]
    public async Task GetAccountStatsAsync_counts_total_and_per_format()
    {
        using var sqlite = new SqliteTestDatabase();
        string userId;
        using (var db = sqlite.CreateContext())
        {
            userId = await SeedUserAsync(db, "me@example.com");
            var other = await SeedUserAsync(db, "other@example.com");
            var svc = Service(db);
            await svc.LogBookAsync(userId, Request("ol:1", "A", new DateOnly(2024, 1, 1), Format.Book));
            await svc.LogBookAsync(userId, Request("ol:2", "B", new DateOnly(2024, 2, 1), Format.Book));
            await svc.LogBookAsync(userId, Request("ol:3", "C", new DateOnly(2024, 3, 1), Format.Audiobook));
            await svc.LogBookAsync(other, Request("ol:4", "D", new DateOnly(2024, 4, 1), Format.Ebook));
        }

        using (var db = sqlite.CreateContext())
        {
            var stats = await Service(db).GetAccountStatsAsync(userId);
            Assert.Equal(3, stats.TotalBooks);
            Assert.Equal(2, stats.Formats[Format.Book]);
            Assert.Equal(1, stats.Formats[Format.Audiobook]);
            Assert.False(stats.Formats.ContainsKey(Format.Ebook)); // other user's only
        }
    }

    [Fact]
    public async Task GetRecentPublicReadsAsync_returns_the_20_newest_across_all_users()
    {
        using var sqlite = new SqliteTestDatabase();
        using (var db = sqlite.CreateContext())
        {
            var userId = await SeedUserAsync(db, "me@example.com");
            var book = new Book { Title = "Catalogue", OpenLibraryId = "ol:shared" };
            db.Books.Add(book);
            await db.SaveChangesAsync();

            var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < 25; i++)
            {
                db.ReadEntries.Add(new ReadEntry
                {
                    UserId = userId,
                    BookId = book.Id,
                    FinishedAt = new DateOnly(2024, 1, 1).AddDays(i),
                    CreatedAt = baseTime.AddMinutes(i), // explicit so ordering is deterministic
                });
            }
            await db.SaveChangesAsync();
        }

        using (var db = sqlite.CreateContext())
        {
            var feed = await Service(db).GetRecentPublicReadsAsync();
            Assert.Equal(20, feed.Count);
            // Newest first: the i=24 entry leads, the i=5 entry is last in the top-20.
            Assert.Equal(new DateTime(2024, 1, 1, 0, 24, 0, DateTimeKind.Utc), feed[0].CreatedAt);
            Assert.All(feed, r => Assert.Equal("Catalogue", r.Title));
        }
    }

    [Fact]
    public async Task GetMyBooksAsync_breaks_a_finished_date_tie_by_newest_created()
    {
        using var sqlite = new SqliteTestDatabase();
        var sameDay = new DateOnly(2024, 5, 5);
        string userId;
        using (var db = sqlite.CreateContext())
        {
            userId = await SeedUserAsync(db, "me@example.com");
            var earlier = new Book { Title = "Earlier-created", OpenLibraryId = "ol:1" };
            var later = new Book { Title = "Later-created", OpenLibraryId = "ol:2" };
            db.Books.AddRange(earlier, later);
            await db.SaveChangesAsync();
            db.ReadEntries.Add(new ReadEntry { UserId = userId, BookId = earlier.Id, FinishedAt = sameDay, CreatedAt = new DateTime(2024, 5, 5, 9, 0, 0, DateTimeKind.Utc) });
            db.ReadEntries.Add(new ReadEntry { UserId = userId, BookId = later.Id, FinishedAt = sameDay, CreatedAt = new DateTime(2024, 5, 5, 10, 0, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }

        using (var db = sqlite.CreateContext())
        {
            var books = await Service(db).GetMyBooksAsync(userId);
            Assert.Collection(books,
                b => Assert.Equal("Later-created", b.Book.Title),
                b => Assert.Equal("Earlier-created", b.Book.Title));
        }
    }

    [Fact]
    public async Task CheckIfReadAsync_finds_a_title_containing_a_literal_percent()
    {
        using var sqlite = new SqliteTestDatabase();
        using var db = sqlite.CreateContext();
        var userId = await SeedUserAsync(db, "me@example.com");
        var svc = Service(db);
        await svc.LogBookAsync(userId, Request("ol:1", "50% Off", new DateOnly(2024, 1, 1)));
        await svc.LogBookAsync(userId, Request("ol:2", "Dune", new DateOnly(2024, 2, 1)));

        var hit = Assert.Single(await svc.CheckIfReadAsync(userId, "50%"));
        Assert.Equal("50% Off", hit.Book.Title);
    }

    [Fact]
    public async Task CheckIfReadAsync_treats_underscore_literally()
    {
        using var sqlite = new SqliteTestDatabase();
        using var db = sqlite.CreateContext();
        var userId = await SeedUserAsync(db, "me@example.com");
        var svc = Service(db);
        await svc.LogBookAsync(userId, Request("ol:1", "A_B", new DateOnly(2024, 1, 1)));

        Assert.Single(await svc.CheckIfReadAsync(userId, "A_B")); // literal underscore matches
        Assert.Empty(await svc.CheckIfReadAsync(userId, "AxB"));  // underscore is not a wildcard
    }

    [Fact]
    public async Task GetRecentPublicReadsAsync_caches_until_a_write_evicts_it()
    {
        using var sqlite = new SqliteTestDatabase();
        var cache = new MemoryCache(new MemoryCacheOptions());

        string userId;
        using (var seed = sqlite.CreateContext())
        {
            userId = await SeedUserAsync(seed, "feed@example.com");
        }

        // One service instance sharing one cache, mirroring the app's singleton cache.
        using var serviceDb = sqlite.CreateContext();
        var service = new ReadLogService(serviceDb, cache, NullLogger<ReadLogService>.Instance);

        await service.LogBookAsync(userId, Request("ol:1", "First", new DateOnly(2024, 1, 1)));
        Assert.Single(await service.GetRecentPublicReadsAsync()); // populates the cache

        // Insert another entry out of band (a second context, so no eviction happens).
        using (var outOfBand = sqlite.CreateContext())
        {
            var book = new Book { Title = "Sneaked In", OpenLibraryId = "ol:2" };
            outOfBand.Books.Add(book);
            await outOfBand.SaveChangesAsync();
            outOfBand.ReadEntries.Add(new ReadEntry { UserId = userId, BookId = book.Id, FinishedAt = new DateOnly(2024, 2, 1) });
            await outOfBand.SaveChangesAsync();
        }

        // Still served from cache — the out-of-band row is invisible.
        Assert.Single(await service.GetRecentPublicReadsAsync());

        // A write through the service evicts the cache, so the next read sees everything.
        await service.LogBookAsync(userId, Request("ol:3", "Third", new DateOnly(2024, 3, 1)));
        Assert.Equal(3, (await service.GetRecentPublicReadsAsync()).Count);
    }

    [Fact]
    public async Task LogBookAsync_throws_DuplicateReadEntryException_for_a_same_date_relog()
    {
        using var sqlite = new SqliteTestDatabase();
        using var db = sqlite.CreateContext();
        var userId = await SeedUserAsync(db, "dup@example.com");
        var svc = Service(db);
        var date = new DateOnly(2024, 1, 1);
        await svc.LogBookAsync(userId, Request("ol:1", "Dune", date));

        await Assert.ThrowsAsync<DuplicateReadEntryException>(
            () => svc.LogBookAsync(userId, Request("ol:1", "Dune", date)));
    }

    [Fact]
    public async Task LogBookAsync_handles_a_concurrent_first_log_of_the_same_book()
    {
        // A file database gives the two tasks independent connections (real contention),
        // which the SqliteTestDatabase's single shared in-memory connection cannot.
        var dbPath = Path.Combine(Path.GetTempPath(), $"readlog-race-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        try
        {
            string user1, user2;
            using (var db = new ApplicationDbContext(options))
            {
                db.Database.Migrate();
                user1 = await SeedUserAsync(db, "u1@example.com");
                user2 = await SeedUserAsync(db, "u2@example.com");
            }

            async Task Log(string userId)
            {
                using var db = new ApplicationDbContext(options);
                await Service(db).LogBookAsync(userId, Request("ol:race", "Dune", new DateOnly(2024, 1, 1)));
            }

            await Task.WhenAll(Log(user1), Log(user2));

            using (var db = new ApplicationDbContext(options))
            {
                // Exactly one shared book row, one entry per user, and no exception escaped.
                Assert.Equal(1, await db.Books.CountAsync(b => b.OpenLibraryId == "ol:race"));
                Assert.Equal(2, await db.ReadEntries.CountAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
