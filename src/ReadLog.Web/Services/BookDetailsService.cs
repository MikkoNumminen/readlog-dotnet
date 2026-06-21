using Microsoft.Extensions.Caching.Memory;
using ReadLog.Web.Dtos;
using ReadLog.Web.Services.External;

namespace ReadLog.Web.Services;

/// <summary>Fetches (and caches) rich book details for the detail view.</summary>
public interface IBookDetailsService
{
    Task<BookDetails?> GetDetailsAsync(string title, string? author, CancellationToken cancellationToken = default);
}

/// <summary>
/// Wraps <see cref="IGoogleBooksClient.GetDetailsAsync"/> with an in-memory cache.
/// Details are effectively immutable, so a long (30-day) TTL mirrors the original's
/// <c>unstable_cache</c>. Only successful (non-null) results are cached, so a missing
/// API key or a transient failure is retried rather than cached for a month.
/// </summary>
public class BookDetailsService : IBookDetailsService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);

    private readonly IGoogleBooksClient _googleBooks;
    private readonly IMemoryCache _cache;

    public BookDetailsService(IGoogleBooksClient googleBooks, IMemoryCache cache)
    {
        _googleBooks = googleBooks;
        _cache = cache;
    }

    public async Task<BookDetails?> GetDetailsAsync(
        string title, string? author, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"book-details:{title}|{author}";
        if (_cache.TryGetValue(cacheKey, out BookDetails? cached))
        {
            return cached;
        }

        var details = await _googleBooks.GetDetailsAsync(title, author, cancellationToken);
        if (details is not null)
        {
            _cache.Set(cacheKey, details, CacheDuration);
        }

        return details;
    }
}
