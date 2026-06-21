using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ReadLog.Web.Models;

namespace ReadLog.Web.Data;

/// <summary>
/// EF Core context for ReadLog. Inherits the ASP.NET Core Identity schema
/// (AspNetUsers, AspNetUserLogins, …) via <see cref="IdentityDbContext{TUser}"/> and
/// adds the app's <see cref="Book"/> and <see cref="ReadEntry"/> tables.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Book> Books => Set<Book>();
    public DbSet<ReadEntry> ReadEntries => Set<ReadEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // Identity tables first.

        builder.Entity<Book>(book =>
        {
            book.Property(b => b.Title).IsRequired();
            book.HasIndex(b => b.Title);
            book.HasIndex(b => b.OpenLibraryId).IsUnique();
        });

        builder.Entity<ReadEntry>(entry =>
        {
            // Persist the enum as its readable name ("Book" / "Audiobook" / "Ebook")
            // rather than an opaque ordinal.
            entry.Property(e => e.Format)
                .HasConversion<string>()
                .HasMaxLength(16);

            // One read per (user, book, finished-on date) — the original's
            // @@unique([userId, bookId, finishedAt]).
            entry.HasIndex(e => new { e.UserId, e.BookId, e.FinishedAt }).IsUnique();
            entry.HasIndex(e => e.UserId);
            entry.HasIndex(e => e.FinishedAt);

            // Defensive bound the original DB schema lacked: keep ratings within 0..5.
            entry.ToTable(t => t.HasCheckConstraint(
                "CK_ReadEntry_Rating",
                "[Rating] IS NULL OR ([Rating] >= 0 AND [Rating] <= 5)"));

            // Deleting a user removes their entries; a book referenced by entries
            // cannot be deleted (matches the original Prisma Cascade / Restrict).
            entry.HasOne(e => e.User)
                .WithMany(u => u.ReadEntries)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entry.HasOne(e => e.Book)
                .WithMany(b => b.ReadEntries)
                .HasForeignKey(e => e.BookId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges()
    {
        StampCreatedAt();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampCreatedAt();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Stamps <see cref="ICreatedAt.CreatedAt"/> on newly added entities.</summary>
    private void StampCreatedAt()
    {
        foreach (var entry in ChangeTracker.Entries<ICreatedAt>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
            }
        }
    }
}
