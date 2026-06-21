using Microsoft.Extensions.Caching.Memory;
using ReadLog.Web.Dtos;
using ReadLog.Web.Services;
using ReadLog.Web.Services.External;

namespace ReadLog.Tests.Services;

public class BookDetailsServiceTests
{
    private static BookDetails SampleDetails(string title) =>
        new(title, Authors: ["A"], Description: null, Categories: [], Publisher: null,
            PublishedDate: null, PageCount: null, CoverUrl: null, Language: null,
            PreviewLink: null, InfoLink: null);

    private static BookDetailsService Build(IGoogleBooksClient google) =>
        new(google, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetDetailsAsync_caches_a_successful_result()
    {
        var google = new CountingGoogle(SampleDetails("Dune"));
        var service = Build(google);

        var first = await service.GetDetailsAsync("Dune", "Frank Herbert");
        var second = await service.GetDetailsAsync("Dune", "Frank Herbert");

        Assert.Same(first, second);
        Assert.Equal(1, google.DetailsCalls); // second call served from cache
    }

    [Fact]
    public async Task GetDetailsAsync_does_not_cache_a_null_result()
    {
        var google = new CountingGoogle(details: null);
        var service = Build(google);

        await service.GetDetailsAsync("Nope", null);
        await service.GetDetailsAsync("Nope", null);

        Assert.Equal(2, google.DetailsCalls); // a miss is retried, not cached
    }

    [Fact]
    public async Task GetDetailsAsync_keys_the_cache_by_title_and_author()
    {
        var google = new CountingGoogle(SampleDetails("Shared"));
        var service = Build(google);

        await service.GetDetailsAsync("Shared", "Author One");
        await service.GetDetailsAsync("Shared", "Author Two");

        Assert.Equal(2, google.DetailsCalls); // different author ⇒ different cache key
    }

    private sealed class CountingGoogle : IGoogleBooksClient
    {
        private readonly BookDetails? _details;

        public CountingGoogle(BookDetails? details) => _details = details;

        public int DetailsCalls { get; private set; }

        public Task<IReadOnlyList<BookSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BookSearchResult>>([]);

        public Task<BookDetails?> GetDetailsAsync(string title, string? author, CancellationToken cancellationToken = default)
        {
            DetailsCalls++;
            return Task.FromResult(_details);
        }
    }
}
