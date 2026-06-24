using System.Text.RegularExpressions;
using ReadLog.Web.Dtos;
using ReadLog.Web.Services.External;

namespace ReadLog.Web.Services;

/// <summary>Searches all book providers and returns a merged, de-duplicated list.</summary>
public interface IBookSearchService
{
    Task<IReadOnlyList<BookSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fans out to Open Library, Google Books and Hardcover concurrently, tolerates any
/// provider failing (the original's <c>Promise.allSettled</c>), concatenates the results
/// (Open Library first, Hardcover last) and de-duplicates by normalised title+author,
/// keeping the richer of any duplicates.
/// </summary>
public partial class BookSearchService : IBookSearchService
{
    private readonly IOpenLibraryClient _openLibrary;
    private readonly IGoogleBooksClient _googleBooks;
    private readonly IHardcoverClient _hardcover;
    private readonly ILogger<BookSearchService> _logger;

    public BookSearchService(
        IOpenLibraryClient openLibrary,
        IGoogleBooksClient googleBooks,
        IHardcoverClient hardcover,
        ILogger<BookSearchService> logger)
    {
        _openLibrary = openLibrary;
        _googleBooks = googleBooks;
        _hardcover = hardcover;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BookSearchResult>> SearchAsync(
        string query, CancellationToken cancellationToken = default)
    {
        // Start all providers, then await together — a failure in one must not sink the others.
        var openLibraryTask = SearchSafelyAsync("Open Library", () => _openLibrary.SearchAsync(query, cancellationToken), cancellationToken);
        var googleTask = SearchSafelyAsync("Google Books", () => _googleBooks.SearchAsync(query, cancellationToken), cancellationToken);
        var hardcoverTask = SearchSafelyAsync("Hardcover", () => _hardcover.SearchAsync(query, cancellationToken), cancellationToken);

        var results = await Task.WhenAll(openLibraryTask, googleTask, hardcoverTask);

        // Order matters for the de-dup tie-break: Open Library, then Google Books, then Hardcover.
        return Deduplicate(results[0].Concat(results[1]).Concat(results[2]));
    }

    private async Task<IReadOnlyList<BookSearchResult>> SearchSafelyAsync(
        string source, Func<Task<IReadOnlyList<BookSearchResult>>> search, CancellationToken cancellationToken)
    {
        try
        {
            return await search();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller-initiated cancellation must surface, not degrade to empty results
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Source} search failed; continuing without its results.", source);
            return [];
        }
    }

    /// <summary>
    /// Collapses duplicates that share a normalised title+author key, keeping the entry
    /// with the most metadata (cover + page count). Ties keep the first seen, so the
    /// Open-Library-first ordering wins.
    /// </summary>
    private static List<BookSearchResult> Deduplicate(IEnumerable<BookSearchResult> results)
    {
        var ordered = new List<BookSearchResult>();
        var indexByKey = new Dictionary<string, int>();

        foreach (var book in results)
        {
            var key = $"{Normalize(book.Title)}|{Normalize(book.Author ?? string.Empty)}";

            if (!indexByKey.TryGetValue(key, out var index))
            {
                indexByKey[key] = ordered.Count;
                ordered.Add(book);
            }
            else if (Score(book) > Score(ordered[index]))
            {
                ordered[index] = book; // upgrade in place, keeping original position
            }
        }

        return ordered;
    }

    private static int Score(BookSearchResult book) =>
        (book.CoverUrl is not null ? 1 : 0) + (book.PageCount is not null ? 1 : 0);

    private static string Normalize(string value) =>
        NonAlphanumeric().Replace(value.ToLowerInvariant(), string.Empty);

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
