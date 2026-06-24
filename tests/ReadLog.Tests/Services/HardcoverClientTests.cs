using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using ReadLog.Tests.Infrastructure;
using ReadLog.Web.Options;
using ReadLog.Web.Services.External;

namespace ReadLog.Tests.Services;

public class HardcoverClientTests
{
    private const string BaseAddress = "https://api.hardcover.app/";
    private const string EmptyHits = """{ "data": { "search": { "results": { "hits": [] } } } }""";

    private static IOptions<HardcoverOptions> WithToken(string? token) =>
        Options.Create(new HardcoverOptions { ApiToken = token });

    [Fact]
    public async Task SearchAsync_maps_a_typesense_hit_to_a_book_result()
    {
        const string json = """
        { "data": { "search": { "results": { "found": 1, "hits": [
          { "document": {
              "id": "123", "title": "Magical Fusion", "slug": "magical-fusion",
              "release_year": 2022, "pages": 410,
              "image": { "url": "https://assets.hardcover.app/cover.jpg" },
              "author_names": ["Jonathan Brooks"] } }
        ] } } } }
        """;
        var handler = StubHttpMessageHandler.Json(json);
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("Bearer test"));

        var book = Assert.Single(await client.SearchAsync("magical fusion"));
        Assert.Equal("hardcover:magical-fusion", book.OpenLibraryId);
        Assert.Equal("Magical Fusion", book.Title);
        Assert.Equal("Jonathan Brooks", book.Author);
        Assert.Equal(2022, book.FirstPublishYear);
        Assert.Equal(410, book.PageCount);
        Assert.Equal("https://assets.hardcover.app/cover.jpg", book.CoverUrl);
    }

    [Fact]
    public async Task SearchAsync_falls_back_to_contributions_for_the_author()
    {
        const string json = """
        { "data": { "search": { "results": { "hits": [
          { "document": { "title": "T", "slug": "t", "contributions": [ { "author": { "name": "Jane Doe" } } ] } }
        ] } } } }
        """;
        var handler = StubHttpMessageHandler.Json(json);
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("t"));

        var book = Assert.Single(await client.SearchAsync("t"));
        Assert.Equal("Jane Doe", book.Author);
    }

    [Fact]
    public async Task SearchAsync_skips_hits_without_a_title()
    {
        const string json = """
        { "data": { "search": { "results": { "hits": [
          { "document": { "slug": "no-title" } },
          { "document": { "title": "Has Title", "slug": "has-title" } }
        ] } } } }
        """;
        var handler = StubHttpMessageHandler.Json(json);
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("t"));

        var book = Assert.Single(await client.SearchAsync("t"));
        Assert.Equal("hardcover:has-title", book.OpenLibraryId);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_without_http_when_the_token_is_missing()
    {
        var handler = StubHttpMessageHandler.Json(EmptyHits);
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken(""));

        Assert.Empty(await client.SearchAsync("anything"));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_without_http_for_a_blank_query()
    {
        var handler = StubHttpMessageHandler.Json(EmptyHits);
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("t"));

        Assert.Empty(await client.SearchAsync("   "));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_on_a_non_success_response()
    {
        var handler = StubHttpMessageHandler.Status(HttpStatusCode.Unauthorized);
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("t"));

        Assert.Empty(await client.SearchAsync("anything"));
    }

    [Fact]
    public async Task SearchAsync_posts_graphql_to_the_endpoint_with_a_bearer_token_and_the_query_variable()
    {
        string body = "";
        string auth = "";
        var handler = new StubHttpMessageHandler(req =>
        {
            auth = req.Headers.GetValues("Authorization").Single();
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyHits, Encoding.UTF8, "application/json"),
            };
        });
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("raw-token"));

        await client.SearchAsync("dune");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://api.hardcover.app/v1/graphql", req.RequestUri!.ToString());
        Assert.Equal("Bearer raw-token", auth); // token without a scheme gets "Bearer " added
        Assert.Contains("query_type", body);
        Assert.Contains("\"q\":\"dune\"", body);
    }

    [Fact]
    public async Task SearchAsync_does_not_double_prefix_a_token_that_already_has_bearer()
    {
        string auth = "";
        var handler = new StubHttpMessageHandler(req =>
        {
            auth = req.Headers.GetValues("Authorization").Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(EmptyHits, Encoding.UTF8, "application/json"),
            };
        });
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("Bearer abc"));

        await client.SearchAsync("x");

        Assert.Equal("Bearer abc", auth);
    }

    [Fact]
    public async Task SearchAsync_prefers_author_names_over_contributions()
    {
        const string json = """
        { "data": { "search": { "results": { "hits": [
          { "document": { "title": "T", "slug": "t",
              "author_names": ["Primary"],
              "contributions": [ { "author": { "name": "Secondary" } } ] } }
        ] } } } }
        """;
        var handler = StubHttpMessageHandler.Json(json);
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("t"));

        var book = Assert.Single(await client.SearchAsync("t"));
        Assert.Equal("Primary", book.Author);
    }

    [Fact]
    public async Task SearchAsync_falls_back_to_contributions_when_author_names_is_blank()
    {
        const string json = """
        { "data": { "search": { "results": { "hits": [
          { "document": { "title": "T", "slug": "t",
              "author_names": ["   "],
              "contributions": [ { "author": { "name": "Real Author" } } ] } }
        ] } } } }
        """;
        var handler = StubHttpMessageHandler.Json(json);
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("t"));

        var book = Assert.Single(await client.SearchAsync("t"));
        Assert.Equal("Real Author", book.Author);
    }

    [Fact]
    public async Task SearchAsync_skips_a_hit_with_no_slug_or_id()
    {
        const string json = """
        { "data": { "search": { "results": { "hits": [
          { "document": { "title": "No Key" } },
          { "document": { "title": "Has Key", "slug": "has-key" } }
        ] } } } }
        """;
        var handler = StubHttpMessageHandler.Json(json);
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("t"));

        var book = Assert.Single(await client.SearchAsync("t"));
        Assert.Equal("hardcover:has-key", book.OpenLibraryId);
    }

    [Fact]
    public async Task SearchAsync_returns_empty_on_a_non_json_200_body()
    {
        // api.hardcover.app is behind Cloudflare; an HTML interstitial with status 200
        // must degrade to empty, not throw.
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Just a moment...</html>", Encoding.UTF8, "text/html"),
        });
        var client = new HardcoverClient(handler.CreateClient(BaseAddress), WithToken("t"));

        Assert.Empty(await client.SearchAsync("anything"));
    }
}
