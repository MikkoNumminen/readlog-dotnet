using System.Net;
using Microsoft.Extensions.Options;
using ReadLog.Tests.Infrastructure;
using ReadLog.Web.Options;
using ReadLog.Web.Services.External;

namespace ReadLog.Tests.Services;

public class GoogleBooksClientTests
{
    private const string BaseAddress = "https://www.googleapis.com/books/v1/";

    private static IOptions<GoogleBooksOptions> WithKey(string? apiKey) =>
        Options.Create(new GoogleBooksOptions { ApiKey = apiKey });

    [Fact]
    public async Task SearchAsync_maps_fields_upgrades_cover_to_https_and_parses_the_year()
    {
        const string json = """
        {
          "items": [
            {
              "id": "abc",
              "volumeInfo": {
                "title": "Dune Messiah",
                "authors": ["Frank Herbert"],
                "publishedDate": "1969-10-15",
                "pageCount": 256,
                "imageLinks": { "thumbnail": "http://books.google.com/cover.jpg" }
              }
            }
          ]
        }
        """;
        var handler = StubHttpMessageHandler.Json(json);
        var client = new GoogleBooksClient(handler.CreateClient(BaseAddress), WithKey("test-key"));

        var book = Assert.Single(await client.SearchAsync("dune messiah"));
        Assert.Equal("google:abc", book.OpenLibraryId);
        Assert.Equal("Dune Messiah", book.Title);
        Assert.Equal("Frank Herbert", book.Author);
        Assert.Equal(1969, book.FirstPublishYear);
        Assert.Equal(256, book.PageCount);
        Assert.Equal("https://books.google.com/cover.jpg", book.CoverUrl);
    }

    [Fact]
    public async Task SearchAsync_builds_a_series_subtitle_combining_with_an_existing_subtitle()
    {
        const string json = """
        {
          "items": [
            { "id": "1", "volumeInfo": { "title": "A", "subtitle": "Origins", "seriesInfo": { "bookDisplayNumber": "2" } } },
            { "id": "2", "volumeInfo": { "title": "B", "seriesInfo": { "bookDisplayNumber": "3" } } }
          ]
        }
        """;
        var handler = StubHttpMessageHandler.Json(json);
        var client = new GoogleBooksClient(handler.CreateClient(BaseAddress), WithKey("k"));

        var results = await client.SearchAsync("series");
        Assert.Equal("Book 2 — Origins", results[0].Subtitle);
        Assert.Equal("Book 3", results[1].Subtitle);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_when_the_api_key_is_missing_without_calling_http()
    {
        var handler = StubHttpMessageHandler.Json("""{ "items": [] }""");
        var client = new GoogleBooksClient(handler.CreateClient(BaseAddress), WithKey(""));

        var results = await client.SearchAsync("anything");

        Assert.Empty(results);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_on_a_non_success_response()
    {
        var handler = StubHttpMessageHandler.Status(HttpStatusCode.TooManyRequests);
        var client = new GoogleBooksClient(handler.CreateClient(BaseAddress), WithKey("k"));

        Assert.Empty(await client.SearchAsync("anything"));
    }

    [Fact]
    public async Task GetDetailsAsync_maps_the_first_volume()
    {
        const string json = """
        {
          "items": [
            {
              "id": "x",
              "volumeInfo": {
                "title": "Dune",
                "authors": ["Frank Herbert"],
                "description": "<p>Spice.</p>",
                "categories": ["Fiction"],
                "publisher": "Ace",
                "publishedDate": "1965",
                "pageCount": 412,
                "language": "en",
                "imageLinks": { "thumbnail": "http://example.com/c.jpg" },
                "previewLink": "https://preview",
                "infoLink": "https://info"
              }
            }
          ]
        }
        """;
        var handler = StubHttpMessageHandler.Json(json);
        var client = new GoogleBooksClient(handler.CreateClient(BaseAddress), WithKey("k"));

        var details = await client.GetDetailsAsync("Dune", "Frank Herbert");

        Assert.NotNull(details);
        Assert.Equal("Dune", details.Title);
        Assert.Equal(["Frank Herbert"], details.Authors);
        Assert.Equal("<p>Spice.</p>", details.Description);
        Assert.Equal(["Fiction"], details.Categories);
        Assert.Equal("Ace", details.Publisher);
        Assert.Equal("https://example.com/c.jpg", details.CoverUrl);
        Assert.Equal("https://info", details.InfoLink);
    }

    [Fact]
    public async Task GetDetailsAsync_returns_null_when_no_items_are_found()
    {
        var handler = StubHttpMessageHandler.Json("""{ "items": [] }""");
        var client = new GoogleBooksClient(handler.CreateClient(BaseAddress), WithKey("k"));

        Assert.Null(await client.GetDetailsAsync("Nope", null));
    }

    [Fact]
    public async Task GetDetailsAsync_returns_null_without_calling_http_when_key_is_missing()
    {
        var handler = StubHttpMessageHandler.Json("""{ "items": [] }""");
        var client = new GoogleBooksClient(handler.CreateClient(BaseAddress), WithKey(null));

        Assert.Null(await client.GetDetailsAsync("Dune", null));
        Assert.Equal(0, handler.CallCount);
    }
}
