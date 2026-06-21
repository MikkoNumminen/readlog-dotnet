using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ReadLog.Web.Dtos;
using ReadLog.Web.Options;

namespace ReadLog.Web.Services.External;

/// <summary>Searches Google Books and fetches rich volume details.</summary>
public interface IGoogleBooksClient
{
    Task<IReadOnlyList<BookSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);

    Task<BookDetails?> GetDetailsAsync(string title, string? author, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed <see cref="HttpClient"/> for the Google Books volumes API. Requires an API
/// key; when it's missing (or the query is empty) the call is skipped and an empty
/// result / null is returned. Unlike Open Library, a non-success response degrades to
/// empty/null rather than throwing.
/// </summary>
public class GoogleBooksClient : IGoogleBooksClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public GoogleBooksClient(HttpClient http, IOptions<GoogleBooksOptions> options)
    {
        _http = http;
        _apiKey = options.Value.ApiKey;
    }

    public async Task<IReadOnlyList<BookSearchResult>> SearchAsync(
        string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = $"volumes?q={Uri.EscapeDataString(query)}&maxResults=15&key={Uri.EscapeDataString(_apiKey)}";

        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var payload = await response.Content
            .ReadFromJsonAsync<GoogleBooksResponse>(JsonOptions, cancellationToken);

        var items = payload?.Items ?? [];
        return items.Select(MapSearchResult).ToList();
    }

    public async Task<BookDetails?> GetDetailsAsync(
        string title, string? author, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var query = string.Join(" ", new[] { title, author }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var url = $"volumes?q={Uri.EscapeDataString(query)}&maxResults=1&key={Uri.EscapeDataString(_apiKey)}";

        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content
            .ReadFromJsonAsync<GoogleBooksResponse>(JsonOptions, cancellationToken);

        if (payload?.Items is not [var volume, ..])
        {
            return null;
        }

        var info = volume.VolumeInfo;
        return new BookDetails(
            Title: info?.Title ?? title,
            Authors: info?.Authors ?? (author is not null ? [author] : []),
            Description: info?.Description,
            Categories: info?.Categories ?? [],
            Publisher: info?.Publisher,
            PublishedDate: info?.PublishedDate,
            PageCount: info?.PageCount,
            CoverUrl: ToHttps(info?.ImageLinks?.Thumbnail),
            Language: info?.Language,
            PreviewLink: info?.PreviewLink,
            InfoLink: info?.InfoLink);
    }

    private static BookSearchResult MapSearchResult(GoogleVolume item)
    {
        var info = item.VolumeInfo;
        var subtitle = info?.Subtitle;

        // If the volume is part of a series, surface that as the subtitle.
        var seriesNumber = info?.SeriesInfo?.BookDisplayNumber;
        if (!string.IsNullOrEmpty(seriesNumber))
        {
            subtitle = string.IsNullOrEmpty(subtitle)
                ? $"Book {seriesNumber}"
                : $"Book {seriesNumber} — {subtitle}";
        }

        return new BookSearchResult(
            OpenLibraryId: $"google:{item.Id}",
            Title: info?.Title ?? string.Empty,
            Subtitle: subtitle,
            Author: info?.Authors is [var author, ..] ? author : null,
            FirstPublishYear: ParseYear(info?.PublishedDate),
            PageCount: info?.PageCount,
            CoverUrl: ToHttps(info?.ImageLinks?.Thumbnail));
    }

    /// <summary>Google often serves cover thumbnails over http; upgrade to https.</summary>
    private static string? ToHttps(string? url) =>
        url is null ? null : url.Replace("http:", "https:", StringComparison.Ordinal);

    /// <summary>Takes the leading year out of a Google "publishedDate" (e.g. "2014-09-01").</summary>
    private static int? ParseYear(string? publishedDate)
    {
        if (string.IsNullOrEmpty(publishedDate))
        {
            return null;
        }

        // Mirror the original's `parseInt(date.slice(0, 4), 10) || null`: from the first
        // four characters, take the leading run of digits (after any whitespace) — so
        // MARC-style fuzzy dates like "198?" / "19uu" still yield 198 / 19 — and treat
        // no-digits or 0 as null.
        var slice = publishedDate.Length >= 4 ? publishedDate[..4] : publishedDate;
        var digits = new string(slice.TrimStart().TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out var year) && year != 0 ? year : null;
    }

    // --- Wire DTOs (Google Books volumes shape) ---------------------------
    private sealed record GoogleBooksResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<GoogleVolume>? Items);

    private sealed record GoogleVolume(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("volumeInfo")] GoogleVolumeInfo? VolumeInfo);

    private sealed record GoogleVolumeInfo(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("subtitle")] string? Subtitle,
        [property: JsonPropertyName("authors")] IReadOnlyList<string>? Authors,
        [property: JsonPropertyName("publishedDate")] string? PublishedDate,
        [property: JsonPropertyName("pageCount")] int? PageCount,
        [property: JsonPropertyName("categories")] IReadOnlyList<string>? Categories,
        [property: JsonPropertyName("publisher")] string? Publisher,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("previewLink")] string? PreviewLink,
        [property: JsonPropertyName("infoLink")] string? InfoLink,
        [property: JsonPropertyName("imageLinks")] GoogleImageLinks? ImageLinks,
        [property: JsonPropertyName("seriesInfo")] GoogleSeriesInfo? SeriesInfo);

    private sealed record GoogleImageLinks(
        [property: JsonPropertyName("thumbnail")] string? Thumbnail,
        [property: JsonPropertyName("smallThumbnail")] string? SmallThumbnail);

    private sealed record GoogleSeriesInfo(
        [property: JsonPropertyName("bookDisplayNumber")] string? BookDisplayNumber);
}
