using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ReadLog.Web.Dtos;
using ReadLog.Web.Options;

namespace ReadLog.Web.Services.External;

/// <summary>
/// Searches Hardcover (hardcover.app) — a community book catalogue with better coverage
/// of indie / LitRPG / audiobook titles than Open Library or Google Books.
/// </summary>
public interface IHardcoverClient
{
    Task<IReadOnlyList<BookSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed <see cref="HttpClient"/> for Hardcover's GraphQL API. Requires an API token
/// (account → Hardcover API); when it's missing (or the query is empty) the call is
/// skipped and an empty result returned. Like Google Books — and unlike Open Library —
/// a non-success response (or a non-JSON body) degrades to an empty list rather than
/// throwing, so a Hardcover failure never sinks the other providers in
/// <see cref="BookSearchService"/>.
/// </summary>
public class HardcoverClient : IHardcoverClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Hardcover's search() is Typesense-backed and returns a JSON "results" blob.
    // per_page is kept at 15, equal to the other providers, so the merge in
    // BookSearchService stays balanced. Only the user query is a GraphQL variable.
    private const string SearchGraphQl =
        "query Search($q: String!) { " +
        "search(query: $q, query_type: \"Book\", per_page: 15, page: 1) { results } }";

    private readonly HttpClient _http;
    private readonly string? _token;

    public HardcoverClient(HttpClient http, IOptions<HardcoverOptions> options)
    {
        _http = http;
        _token = options.Value.ApiToken;
    }

    public async Task<IReadOnlyList<BookSearchResult>> SearchAsync(
        string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_token) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var body = JsonSerializer.Serialize(new
        {
            query = SearchGraphQl,
            variables = new { q = query },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/graphql")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        // Hardcover's account token already carries the "Bearer " scheme; tolerate either form.
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            _token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? _token : $"Bearer {_token}");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        HardcoverResponse? payload;
        try
        {
            payload = await response.Content
                .ReadFromJsonAsync<HardcoverResponse>(JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A 200 with a non-JSON body (a CDN/HTML interstitial, or `results` returned as a
            // JSON-encoded string instead of an object) — degrade to empty rather than throwing.
            return [];
        }

        var hits = payload?.Data?.Search?.Results?.Hits ?? [];
        return hits
            .Select(h => h.Document)
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Title)
                        && (d.Slug is not null || d.Id is not null))
            .Select(d => MapSearchResult(d!))
            .ToList();
    }

    private static BookSearchResult MapSearchResult(HardcoverDocument doc)
    {
        // Typesense search docs expose a flat author_names[]; fall back to the relational
        // contributions[].author.name shape if that's what comes back.
        var author = doc.AuthorNames?.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
            ?? doc.Contributions?
                .Select(c => c.Author?.Name)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        return new BookSearchResult(
            OpenLibraryId: $"hardcover:{doc.Slug ?? doc.Id}",
            Title: doc.Title ?? string.Empty,
            Subtitle: null,
            Author: author,
            FirstPublishYear: doc.ReleaseYear,
            PageCount: doc.Pages,
            CoverUrl: doc.Image?.Url);
    }

    // --- Wire DTOs (Hardcover GraphQL search → Typesense "results" blob) ---
    private sealed record HardcoverResponse(
        [property: JsonPropertyName("data")] HardcoverData? Data);

    private sealed record HardcoverData(
        [property: JsonPropertyName("search")] HardcoverSearch? Search);

    private sealed record HardcoverSearch(
        [property: JsonPropertyName("results")] HardcoverResults? Results);

    private sealed record HardcoverResults(
        [property: JsonPropertyName("hits")] IReadOnlyList<HardcoverHit>? Hits);

    private sealed record HardcoverHit(
        [property: JsonPropertyName("document")] HardcoverDocument? Document);

    private sealed record HardcoverDocument(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("slug")] string? Slug,
        [property: JsonPropertyName("release_year")] int? ReleaseYear,
        [property: JsonPropertyName("pages")] int? Pages,
        [property: JsonPropertyName("image")] HardcoverImage? Image,
        [property: JsonPropertyName("author_names")] IReadOnlyList<string>? AuthorNames,
        [property: JsonPropertyName("contributions")] IReadOnlyList<HardcoverContribution>? Contributions);

    private sealed record HardcoverImage(
        [property: JsonPropertyName("url")] string? Url);

    private sealed record HardcoverContribution(
        [property: JsonPropertyName("author")] HardcoverAuthor? Author);

    private sealed record HardcoverAuthor(
        [property: JsonPropertyName("name")] string? Name);
}
