using Microsoft.Extensions.Logging.Abstractions;
using ReadLog.Web.Dtos;
using ReadLog.Web.Services;
using ReadLog.Web.Services.External;

namespace ReadLog.Tests.Services;

public class BookSearchServiceTests
{
    private static BookSearchResult Result(
        string id, string title, string? author = null, string? cover = null, int? pages = null) =>
        new(id, title, Subtitle: null, author, FirstPublishYear: null, pages, cover);

    private static BookSearchService Build(IOpenLibraryClient openLibrary, IGoogleBooksClient google) =>
        new(openLibrary, google, NullLogger<BookSearchService>.Instance);

    [Fact]
    public async Task SearchAsync_concatenates_results_open_library_first()
    {
        var service = Build(
            new StubOpenLibrary([Result("/works/OL1W", "Dune")]),
            new StubGoogle([Result("google:g1", "Foundation")]));

        var results = await service.SearchAsync("sci-fi");

        Assert.Collection(results,
            r => Assert.Equal("/works/OL1W", r.OpenLibraryId),
            r => Assert.Equal("google:g1", r.OpenLibraryId));
    }

    [Fact]
    public async Task SearchAsync_deduplicates_keeping_the_richer_duplicate()
    {
        // Same normalised title+author; the Google copy has cover + pages (richer).
        var service = Build(
            new StubOpenLibrary([Result("/works/OL1W", "Dune")]),
            new StubGoogle([Result("google:g1", "Dune!", cover: "https://c", pages: 412)]));

        var book = Assert.Single(await service.SearchAsync("dune"));
        Assert.Equal("google:g1", book.OpenLibraryId);
        Assert.Equal("https://c", book.CoverUrl);
    }

    [Fact]
    public async Task SearchAsync_breaks_ties_in_favour_of_open_library()
    {
        // Equal scores (both have a cover) — first seen (Open Library) wins.
        var service = Build(
            new StubOpenLibrary([Result("/works/OL1W", "Dune", cover: "https://ol")]),
            new StubGoogle([Result("google:g1", "Dune", cover: "https://g")]));

        var book = Assert.Single(await service.SearchAsync("dune"));
        Assert.Equal("/works/OL1W", book.OpenLibraryId);
    }

    [Fact]
    public async Task SearchAsync_still_returns_google_results_when_open_library_throws()
    {
        var service = Build(
            new StubOpenLibrary(new HttpRequestException("Open Library down")),
            new StubGoogle([Result("google:g1", "Foundation")]));

        var book = Assert.Single(await service.SearchAsync("foundation"));
        Assert.Equal("google:g1", book.OpenLibraryId);
    }

    [Fact]
    public async Task SearchAsync_still_returns_open_library_results_when_google_throws()
    {
        var service = Build(
            new StubOpenLibrary([Result("/works/OL1W", "Dune")]),
            new StubGoogle(new HttpRequestException("Google down")));

        var book = Assert.Single(await service.SearchAsync("dune"));
        Assert.Equal("/works/OL1W", book.OpenLibraryId);
    }

    [Fact]
    public async Task SearchAsync_propagates_caller_cancellation_rather_than_degrading_to_empty()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var service = Build(
            new StubOpenLibrary(new OperationCanceledException(cts.Token)),
            new StubGoogle([Result("google:g1", "Foundation")]));

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchAsync("foundation", cts.Token));
    }

    private sealed class StubOpenLibrary : IOpenLibraryClient
    {
        private readonly IReadOnlyList<BookSearchResult>? _result;
        private readonly Exception? _exception;

        public StubOpenLibrary(IReadOnlyList<BookSearchResult> result) => _result = result;
        public StubOpenLibrary(Exception exception) => _exception = exception;

        public Task<IReadOnlyList<BookSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            _exception is not null
                ? Task.FromException<IReadOnlyList<BookSearchResult>>(_exception)
                : Task.FromResult(_result!);
    }

    private sealed class StubGoogle : IGoogleBooksClient
    {
        private readonly IReadOnlyList<BookSearchResult>? _result;
        private readonly Exception? _exception;

        public StubGoogle(IReadOnlyList<BookSearchResult> result) => _result = result;
        public StubGoogle(Exception exception) => _exception = exception;

        public Task<IReadOnlyList<BookSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            _exception is not null
                ? Task.FromException<IReadOnlyList<BookSearchResult>>(_exception)
                : Task.FromResult(_result!);

        public Task<BookDetails?> GetDetailsAsync(string title, string? author, CancellationToken cancellationToken = default) =>
            Task.FromResult<BookDetails?>(null);
    }
}
