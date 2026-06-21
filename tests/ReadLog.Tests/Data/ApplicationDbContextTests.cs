using Microsoft.EntityFrameworkCore;
using ReadLog.Tests.Infrastructure;
using ReadLog.Web.Data;
using ReadLog.Web.Models;

namespace ReadLog.Tests.Data;

public class ApplicationDbContextTests
{
    private static ApplicationUser SeedUser(ApplicationDbContext db, string email = "reader@example.com")
    {
        var user = new ApplicationUser { UserName = email, Email = email };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Book SeedBook(ApplicationDbContext db, string title = "Dune", string? openLibraryId = "/works/OL1W")
    {
        var book = new Book { Title = title, OpenLibraryId = openLibraryId };
        db.Books.Add(book);
        db.SaveChanges();
        return book;
    }

    [Fact]
    public void Format_is_persisted_as_its_string_name()
    {
        using var sqlite = new SqliteTestDatabase();
        int entryId;

        using (var db = sqlite.CreateContext())
        {
            var user = SeedUser(db);
            var book = SeedBook(db);
            var entry = new ReadEntry
            {
                UserId = user.Id,
                BookId = book.Id,
                Format = Format.Audiobook,
                FinishedAt = new DateOnly(2024, 1, 2),
            };
            db.ReadEntries.Add(entry);
            db.SaveChanges();
            entryId = entry.Id;
        }

        using (var db = sqlite.CreateContext())
        {
            var stored = db.Database
                .SqlQueryRaw<string>("SELECT Format AS Value FROM ReadEntries WHERE Id = {0}", entryId)
                .Single();
            Assert.Equal("Audiobook", stored);

            var reloaded = db.ReadEntries.Single(e => e.Id == entryId);
            Assert.Equal(Format.Audiobook, reloaded.Format);
        }
    }

    [Fact]
    public void CreatedAt_is_stamped_automatically_on_insert()
    {
        using var sqlite = new SqliteTestDatabase();
        using var db = sqlite.CreateContext();

        var before = DateTime.UtcNow.AddSeconds(-1);
        var book = new Book { Title = "Neuromancer" };
        db.Books.Add(book);
        db.SaveChanges();

        Assert.NotEqual(default, book.CreatedAt);
        Assert.InRange(book.CreatedAt, before, DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Same_user_book_and_finished_date_cannot_be_logged_twice()
    {
        using var sqlite = new SqliteTestDatabase();
        using var db = sqlite.CreateContext();
        var user = SeedUser(db);
        var book = SeedBook(db);
        var finishedAt = new DateOnly(2024, 5, 1);

        db.ReadEntries.Add(new ReadEntry { UserId = user.Id, BookId = book.Id, FinishedAt = finishedAt });
        db.SaveChanges();

        db.ReadEntries.Add(new ReadEntry { UserId = user.Id, BookId = book.Id, FinishedAt = finishedAt });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Theory]
    [InlineData(0)] // lower bound
    [InlineData(5)] // upper bound
    public void Rating_within_0_to_5_is_accepted(int rating)
    {
        using var sqlite = new SqliteTestDatabase();
        using var db = sqlite.CreateContext();
        var user = SeedUser(db);
        var book = SeedBook(db);

        db.ReadEntries.Add(new ReadEntry
        {
            UserId = user.Id,
            BookId = book.Id,
            FinishedAt = new DateOnly(2024, 6, 1),
            Rating = rating,
        });

        db.SaveChanges();
        Assert.Equal(rating, db.ReadEntries.Single().Rating);
    }

    [Theory]
    [InlineData(-1)] // just below the lower bound
    [InlineData(6)]  // just above the upper bound
    public void Rating_outside_0_to_5_violates_the_check_constraint(int rating)
    {
        using var sqlite = new SqliteTestDatabase();
        using var db = sqlite.CreateContext();
        var user = SeedUser(db);
        var book = SeedBook(db);

        db.ReadEntries.Add(new ReadEntry
        {
            UserId = user.Id,
            BookId = book.Id,
            FinishedAt = new DateOnly(2024, 6, 1),
            Rating = rating,
        });

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Deleting_a_user_cascades_to_their_entries_but_keeps_the_book()
    {
        using var sqlite = new SqliteTestDatabase();
        string userId;
        using (var db = sqlite.CreateContext())
        {
            var user = SeedUser(db);
            var book = SeedBook(db);
            db.ReadEntries.Add(new ReadEntry { UserId = user.Id, BookId = book.Id, FinishedAt = new DateOnly(2024, 7, 1) });
            db.SaveChanges();
            userId = user.Id;
        }

        // Act in a fresh context so the cascade is exercised at the database level,
        // not via EF's client-side cascade over tracked entities.
        using (var db = sqlite.CreateContext())
        {
            var user = db.Users.Single(u => u.Id == userId);
            db.Users.Remove(user);
            db.SaveChanges();
        }

        using (var db = sqlite.CreateContext())
        {
            Assert.Empty(db.ReadEntries);
            Assert.Single(db.Books);
        }
    }

    [Fact]
    public void Deleting_a_book_that_has_entries_is_restricted()
    {
        using var sqlite = new SqliteTestDatabase();
        int bookId;
        using (var db = sqlite.CreateContext())
        {
            var user = SeedUser(db);
            var book = SeedBook(db);
            db.ReadEntries.Add(new ReadEntry { UserId = user.Id, BookId = book.Id, FinishedAt = new DateOnly(2024, 8, 1) });
            db.SaveChanges();
            bookId = book.Id;
        }

        // Fresh context: the dependent entry is not tracked, so removal reaches the
        // database, where the Restrict foreign key rejects it.
        using (var db = sqlite.CreateContext())
        {
            var book = db.Books.Single(b => b.Id == bookId);
            db.Books.Remove(book);
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }

        // The rejected delete must have left both the book and its entry intact.
        using (var db = sqlite.CreateContext())
        {
            Assert.Single(db.Books);
            Assert.Single(db.ReadEntries);
        }
    }

    [Fact]
    public void OpenLibraryId_must_be_unique()
    {
        using var sqlite = new SqliteTestDatabase();
        using var db = sqlite.CreateContext();

        db.Books.Add(new Book { Title = "First", OpenLibraryId = "google:abc" });
        db.SaveChanges();

        db.Books.Add(new Book { Title = "Second", OpenLibraryId = "google:abc" });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }
}
