using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReadLog.Web.Dtos;

namespace ReadLog.Web.Services.External;

/// <summary>Searches the Open Library catalogue.</summary>
public interface IOpenLibraryClient
{
    Task<IReadOnlyList<BookSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed <see cref="HttpClient"/> for Open Library's search API. Mirrors the original
/// behaviour: an empty query is a no-op, and a non-success response <b>throws</b>
/// (the search service degrades that to "no Open Library results").
/// </summary>
public class OpenLibraryClient : IOpenLibraryClient
{
    // Only the fields the app needs, to keep the response small.
    private const string Fields =
        "key,title,subtitle,author_name,first_publish_year,number_of_pages_median,cover_i";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public OpenLibraryClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<BookSearchResult>> SearchAsync(
        string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = $"search.json?q={Uri.EscapeDataString(query)}&limit=15&fields={Fields}";

        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Open Library API error ({(int)response.StatusCode}).");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<OpenLibrarySearchResponse>(JsonOptions, cancellationToken);

        var docs = payload?.Docs ?? [];
        return docs.Select(Map).ToList();
    }

    private static BookSearchResult Map(OpenLibraryDoc doc) => new(
        OpenLibraryId: doc.Key ?? string.Empty,
        Title: doc.Title ?? string.Empty,
        Subtitle: doc.Subtitle,
        Author: doc.AuthorName is [var author, ..] ? author : null,
        FirstPublishYear: doc.FirstPublishYear,
        PageCount: doc.NumberOfPagesMedian,
        CoverUrl: doc.CoverI is int coverId
            ? $"https://covers.openlibrary.org/b/id/{coverId}-M.jpg"
            : null);

    // --- Wire DTOs (Open Library search.json shape) -----------------------
    private sealed record OpenLibrarySearchResponse(
        [property: JsonPropertyName("docs")] IReadOnlyList<OpenLibraryDoc>? Docs);

    private sealed record OpenLibraryDoc(
        [property: JsonPropertyName("key")] string? Key,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("subtitle")] string? Subtitle,
        [property: JsonPropertyName("author_name")] IReadOnlyList<string>? AuthorName,
        [property: JsonPropertyName("first_publish_year")] int? FirstPublishYear,
        [property: JsonPropertyName("number_of_pages_median")] int? NumberOfPagesMedian,
        [property: JsonPropertyName("cover_i")] int? CoverI);
}
