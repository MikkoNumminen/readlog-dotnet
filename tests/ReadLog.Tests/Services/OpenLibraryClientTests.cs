using System.Net;
using ReadLog.Tests.Infrastructure;
using ReadLog.Web.Services.External;

namespace ReadLog.Tests.Services;

public class OpenLibraryClientTests
{
    private const string OneDoc = """
    {
      "docs": [
        {
          "key": "/works/OL1W",
          "title": "Dune",
          "subtitle": "A sci-fi classic",
          "author_name": ["Frank Herbert", "Someone Else"],
          "first_publish_year": 1965,
          "number_of_pages_median": 412,
          "cover_i": 12345
        }
      ]
    }
    """;

    [Fact]
    public async Task SearchAsync_maps_fields_and_builds_the_cover_url()
    {
        var handler = StubHttpMessageHandler.Json(OneDoc);
        var client = new OpenLibraryClient(handler.CreateClient("https://openlibrary.org/"));

        var results = await client.SearchAsync("dune");

        var book = Assert.Single(results);
        Assert.Equal("/works/OL1W", book.OpenLibraryId);
        Assert.Equal("Dune", book.Title);
        Assert.Equal("A sci-fi classic", book.Subtitle);
        Assert.Equal("Frank Herbert", book.Author); // first author only
        Assert.Equal(1965, book.FirstPublishYear);
        Assert.Equal(412, book.PageCount);
        Assert.Equal("https://covers.openlibrary.org/b/id/12345-M.jpg", book.CoverUrl);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_for_a_blank_query_without_calling_http()
    {
        var handler = StubHttpMessageHandler.Json(OneDoc);
        var client = new OpenLibraryClient(handler.CreateClient("https://openlibrary.org/"));

        var results = await client.SearchAsync("   ");

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_throws_on_a_non_success_response()
    {
        var handler = StubHttpMessageHandler.Status(HttpStatusCode.InternalServerError);
        var client = new OpenLibraryClient(handler.CreateClient("https://openlibrary.org/"));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync("dune"));
    }

    [Fact]
    public async Task SearchAsync_tolerates_missing_optional_fields()
    {
        var handler = StubHttpMessageHandler.Json("""{ "docs": [ { "key": "/works/OL9W", "title": "Sparse" } ] }""");
        var client = new OpenLibraryClient(handler.CreateClient("https://openlibrary.org/"));

        var book = Assert.Single(await client.SearchAsync("sparse"));
        Assert.Equal("Sparse", book.Title);
        Assert.Null(book.Author);
        Assert.Null(book.CoverUrl);
        Assert.Null(book.PageCount);
        Assert.Null(book.FirstPublishYear);
    }
}
