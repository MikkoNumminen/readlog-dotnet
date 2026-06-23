using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ReadLog.Web.Data;
using ReadLog.Web.Dtos;
using ReadLog.Web.Models;

namespace ReadLog.Web.Services;

/// <summary>
/// The reading-log domain: logging finished books, the user's library, the
/// "have I read this?" lookup, edit/delete, account stats, and the public feed.
/// Methods take the acting <c>userId</c> explicitly — pages enforce authentication
/// and pass it in, keeping the service free of HTTP concerns.
/// </summary>
public interface IReadLogService
{
    Task LogBookAsync(string userId, LogBookRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibraryEntryDto>> GetMyBooksAsync(string userId, CancellationToken cancellationToken = default);

    /// <returns>The entry, or <c>null</c> if it doesn't exist or isn't owned by the user.</returns>
    Task<LibraryEntryDto?> GetEntryAsync(string userId, int entryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibraryEntryDto>> CheckIfReadAsync(string userId, string query, CancellationToken cancellationToken = default);

    /// <returns><c>false</c> if the entry doesn't exist or isn't owned by the user (treat as 404).</returns>
    Task<bool> UpdateReadEntryAsync(string userId, int entryId, UpdateReadEntryRequest request, CancellationToken cancellationToken = default);

    /// <returns><c>false</c> if the entry doesn't exist or isn't owned by the user (treat as 404).</returns>
    Task<bool> DeleteReadEntryAsync(string userId, int entryId, CancellationToken cancellationToken = default);

    Task<AccountStats> GetAccountStatsAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublicReadDto>> GetRecentPublicReadsAsync(CancellationToken cancellationToken = default);
}

public class ReadLogService : IReadLogService
{
    private const int PublicFeedSize = 20;
    private const string PublicFeedCacheKey = "public-feed";

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ReadLogService> _logger;

    public ReadLogService(ApplicationDbContext db, IMemoryCache cache, ILogger<ReadLogService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task LogBookAsync(string userId, LogBookRequest request, CancellationToken cancellationToken = default)
    {
        var book = await GetOrCreateBookAsync(request, cancellationToken);

        var entry = new ReadEntry
        {
            UserId = userId,
            Book = book,
            Format = request.Format,
            FinishedAt = request.FinishedAt,
            Rating = request.Rating,
        };
        _db.ReadEntries.Add(entry);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique (user, book, finished-on) index may have rejected a duplicate.
            // Confirm by re-querying; if it really is a duplicate, surface a domain error,
            // otherwise re-throw so a genuine failure (e.g. a locked DB) isn't mislabelled.
            _db.Entry(entry).State = EntityState.Detached;
            var alreadyLogged = await _db.ReadEntries.AnyAsync(
                e => e.UserId == userId && e.BookId == book.Id && e.FinishedAt == request.FinishedAt,
                cancellationToken);
            if (alreadyLogged)
            {
                throw new DuplicateReadEntryException();
            }

            throw;
        }

        _cache.Remove(PublicFeedCacheKey); // the public feed changed
    }

    public async Task<LibraryEntryDto?> GetEntryAsync(
        string userId, int entryId, CancellationToken cancellationToken = default) =>
        await _db.ReadEntries
            .Where(e => e.Id == entryId && e.UserId == userId)
            .Select(e => new LibraryEntryDto(
                e.Id, e.Format, e.FinishedAt, e.Rating,
                new BookSummaryDto(e.Book.Title, e.Book.Author, e.Book.CoverUrl)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LibraryEntryDto>> GetMyBooksAsync(
        string userId, CancellationToken cancellationToken = default) =>
        await _db.ReadEntries
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.FinishedAt)
            .ThenByDescending(e => e.CreatedAt)
            .Select(e => new LibraryEntryDto(
                e.Id, e.Format, e.FinishedAt, e.Rating,
                new BookSummaryDto(e.Book.Title, e.Book.Author, e.Book.CoverUrl)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LibraryEntryDto>> CheckIfReadAsync(
        string userId, string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Case-insensitive "contains": SQLite LIKE is ASCII case-insensitive by default.
        // Escape the user's % / _ / \ so they're matched literally, not as wildcards.
        var pattern = $"%{EscapeLike(query.Trim())}%";

        return await _db.ReadEntries
            .Where(e => e.UserId == userId && EF.Functions.Like(e.Book.Title, pattern, "\\"))
            .OrderByDescending(e => e.FinishedAt)
            .Select(e => new LibraryEntryDto(
                e.Id, e.Format, e.FinishedAt, e.Rating,
                new BookSummaryDto(e.Book.Title, e.Book.Author, e.Book.CoverUrl)))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> UpdateReadEntryAsync(
        string userId, int entryId, UpdateReadEntryRequest request, CancellationToken cancellationToken = default)
    {
        // Combine existence + ownership into one query: a non-owner sees the same
        // "not found" as a stranger (the original returns 404, not 403).
        var entry = await _db.ReadEntries
            .Include(e => e.Book)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        // The shared catalogue Book is intentionally NOT editable here: a title edit would
        // mutate the row for every user who logged that book. Only per-user fields change.
        entry.Format = request.Format;
        entry.FinishedAt = request.FinishedAt;
        entry.Rating = request.Rating; // null clears, 0 is a real rating

        await _db.SaveChangesAsync(cancellationToken);
        _cache.Remove(PublicFeedCacheKey);
        return true;
    }

    public async Task<bool> DeleteReadEntryAsync(
        string userId, int entryId, CancellationToken cancellationToken = default)
    {
        var entry = await _db.ReadEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        _db.ReadEntries.Remove(entry); // the shared Book row is left intact
        await _db.SaveChangesAsync(cancellationToken);
        _cache.Remove(PublicFeedCacheKey);
        return true;
    }

    public async Task<AccountStats> GetAccountStatsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var totalBooks = await _db.ReadEntries.CountAsync(e => e.UserId == userId, cancellationToken);

        var formats = await _db.ReadEntries
            .Where(e => e.UserId == userId)
            .GroupBy(e => e.Format)
            .Select(g => new { Format = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Format, g => g.Count, cancellationToken);

        return new AccountStats(totalBooks, formats);
    }

    public async Task<IReadOnlyList<PublicReadDto>> GetRecentPublicReadsAsync(
        CancellationToken cancellationToken = default)
    {
        // The feed is a global hot read; cache it briefly and evict on any write
        // (the mutations above call _cache.Remove) — the .NET take on updateTag.
        var feed = await _cache.GetOrCreateAsync(PublicFeedCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);

            // Don't bake one request's cancellation token into the shared cache entry —
            // the populating query serves every reader, so it shouldn't be cancellable
            // by whichever request happened to trigger it.
            return await _db.ReadEntries
                .OrderByDescending(e => e.CreatedAt)
                .Take(PublicFeedSize)
                .Select(e => new PublicReadDto(
                    e.Book.Title, e.Book.Author, e.Book.CoverUrl, e.Format, e.CreatedAt, e.Rating))
                .ToListAsync(CancellationToken.None);
        });

        return feed ?? [];
    }

    /// <summary>Escapes LIKE metacharacters so a search term is matched literally.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>
    /// Reuses the catalogue Book for this provider id, or creates it. Tolerates a
    /// concurrent creation losing the race on the unique <c>OpenLibraryId</c> index.
    /// </summary>
    private async Task<Book> GetOrCreateBookAsync(LogBookRequest request, CancellationToken cancellationToken)
    {
        var existing = await _db.Books
            .FirstOrDefaultAsync(b => b.OpenLibraryId == request.OpenLibraryId, cancellationToken);
        if (existing is not null)
        {
            return existing; // reuse as-is — the first logger's metadata wins
        }

        var book = new Book
        {
            OpenLibraryId = request.OpenLibraryId,
            Title = request.Title,
            Author = request.Author,
            CoverUrl = request.CoverUrl,
            PageCount = request.PageCount,
            FirstPublishYear = request.FirstPublishYear,
        };
        _db.Books.Add(book);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return book;
        }
        catch (DbUpdateException)
        {
            // We may have lost a race to create the shared book on the unique
            // OpenLibraryId index. Detach our failed insert and look for the winner.
            _db.Entry(book).State = EntityState.Detached;
            var winner = await _db.Books
                .FirstOrDefaultAsync(b => b.OpenLibraryId == request.OpenLibraryId, cancellationToken);
            if (winner is null)
            {
                // No winning row exists, so this wasn't the race we tolerate
                // (e.g. a locked database) — let the real failure surface.
                throw;
            }

            _logger.LogInformation("Lost a race creating book {OpenLibraryId}; reusing the existing row.",
                request.OpenLibraryId);
            return winner;
        }
    }
}
