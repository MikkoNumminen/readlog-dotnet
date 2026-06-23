using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ReadLog.Tests.Infrastructure;
using ReadLog.Web.Data;

namespace ReadLog.Tests.Pages;

/// <summary>
/// Drives the Razor Pages over HTTP against an isolated app + temp database
/// (a fresh factory per test, so entry ids are deterministic).
/// </summary>
public class UiPagesTests
{
    private static string Today =>
        DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("/library")]
    [InlineData("/log")]
    [InlineData("/account")]
    public async Task Protected_pages_redirect_an_anonymous_user_to_signin(string path)
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.Contains("/signin", location);
        Assert.Contains("ReturnUrl", location); // the requested page round-trips back after login
    }

    [Fact]
    public async Task Home_feed_renders()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Recently Read", html);
    }

    [Fact]
    public async Task Logging_a_book_then_visiting_the_library_shows_it()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAsync("logger@example.com");

        var logUrl = "/log?olid=manual:t1&selTitle=The%20Test%20Book";
        var response = await client.PostFormAsync(logUrl, new Dictionary<string, string>
        {
            ["Input.OpenLibraryId"] = "manual:t1",
            ["Input.Title"] = "The Test Book",
            ["Input.Format"] = "Audiobook",
            ["Input.FinishedAt"] = Today,
            ["Input.Rating"] = "4",
        });
        response.EnsureSuccessStatusCode(); // followed the redirect to /library

        var library = await client.GetStringAsync("/library");
        Assert.Contains("The Test Book", library);
    }

    [Fact]
    public async Task Editing_an_entry_updates_it()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAsync("editor@example.com");
        await LogBookAsync(client, "manual:e1", "Original Title");
        var id = await SingleEntryIdAsync(factory);

        var response = await client.PostFormAsync($"/library/edit/{id}", new Dictionary<string, string>
        {
            ["Input.Format"] = "Ebook",
            ["Input.FinishedAt"] = Today,
            ["Input.Rating"] = "5",
        });
        response.EnsureSuccessStatusCode();

        await AssertEntryAsync(factory, e =>
        {
            Assert.Equal("Original Title", e.Book.Title); // shared-catalogue title is not editable
            Assert.Equal(5, e.Rating);
        });
    }

    [Fact]
    public async Task Deleting_an_entry_removes_it()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAsync("deleter@example.com");
        await LogBookAsync(client, "manual:d1", "Doomed Book");
        var id = await SingleEntryIdAsync(factory);

        var token = HtmlFormHelper.ExtractAntiforgeryToken(await client.GetStringAsync($"/library/edit/{id}"));
        var response = await client.PostAsync($"/library/edit/{id}?handler=Delete",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.ReadEntries.AnyAsync());
        Assert.True(await db.Books.AnyAsync()); // the catalogue book is kept
    }

    [Fact]
    public async Task Editing_a_nonexistent_entry_returns_404()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAsync("nobody@example.com");

        var response = await client.GetAsync("/library/edit/99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Account_page_reflects_the_logged_count()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAsync("counter@example.com", name: "Count Reader");
        await LogBookAsync(client, "manual:a1", "Stat Book");

        var html = await client.GetStringAsync("/account");

        Assert.Contains("Count Reader", html);
        Assert.Contains("Reading stats", html);
        Assert.Contains("book logged", html);   // singular total label for exactly one
        Assert.Contains("1 Books", html);        // the per-format chip
    }

    [Fact]
    public async Task Logging_the_same_book_on_the_same_date_twice_shows_a_conflict_message()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAsync("dup@example.com");
        await LogBookAsync(client, "manual:dup", "Repeat Book");

        var response = await client.PostFormAsync("/log?olid=manual:dup&selTitle=Repeat%20Book",
            new Dictionary<string, string>
            {
                ["Input.OpenLibraryId"] = "manual:dup",
                ["Input.Title"] = "Repeat Book",
                ["Input.Format"] = "Book",
                ["Input.FinishedAt"] = Today,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // re-rendered, not redirected
        Assert.Contains("already logged", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Library_search_reports_matches_and_misses()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAsync("searcher@example.com");
        await LogBookAsync(client, "manual:s1", "Searchable Saga");

        var match = await client.GetStringAsync("/library?q=saga");
        Assert.Contains("Yes! Found", match);
        Assert.Contains("Searchable Saga", match);

        var miss = await client.GetStringAsync("/library?q=nothinghere");
        Assert.Contains("Not in your library", miss);
    }

    [Fact]
    public async Task Library_list_view_renders()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();
        await client.RegisterAsync("lister@example.com");
        await LogBookAsync(client, "manual:l1", "Listed Book");

        var html = await client.GetStringAsync("/library?view=list");

        Assert.Contains("Listed Book", html);
        Assert.Contains("Edit", html); // the list view's per-row edit link
    }

    [Fact]
    public async Task Book_detail_renders_without_an_api_key()
    {
        using var factory = new ReadLogAppFactory();
        var client = factory.CreateClient();

        // No Google Books API key is configured in tests, so details come back null.
        var html = await client.GetStringAsync("/Book?title=Dune&author=Frank+Herbert");

        Assert.Contains("Dune", html);
        Assert.Contains("No details available", html);
    }

    private static async Task LogBookAsync(HttpClient client, string olid, string title)
    {
        var response = await client.PostFormAsync($"/log?olid={olid}&selTitle={Uri.EscapeDataString(title)}",
            new Dictionary<string, string>
            {
                ["Input.OpenLibraryId"] = olid,
                ["Input.Title"] = title,
                ["Input.Format"] = "Book",
                ["Input.FinishedAt"] = Today,
            });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<int> SingleEntryIdAsync(ReadLogAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ReadEntries.Select(e => e.Id).SingleAsync();
    }

    private static async Task AssertEntryAsync(ReadLogAppFactory factory, Action<Web.Models.ReadEntry> assert)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entry = await db.ReadEntries.Include(e => e.Book).SingleAsync();
        assert(entry);
    }
}
