namespace ReadLog.Web.Dtos;

/// <summary>
/// A single normalised search hit, merged from Open Library and Google Books.
/// <paramref name="OpenLibraryId"/> is the provider-namespaced natural key used to
/// find-or-create the catalogue <see cref="Models.Book"/>: an Open Library work key
/// (<c>/works/OL1W</c>), a <c>google:&lt;id&gt;</c>, or a <c>manual:&lt;timestamp&gt;</c>.
/// </summary>
public record BookSearchResult(
    string OpenLibraryId,
    string Title,
    string? Subtitle,
    string? Author,
    int? FirstPublishYear,
    int? PageCount,
    string? CoverUrl);
