namespace ReadLog.Web.Dtos;

/// <summary>
/// Rich book metadata fetched from Google Books for the detail view. All fields are
/// best-effort: any of them may be missing depending on the volume.
/// </summary>
public record BookDetails(
    string Title,
    IReadOnlyList<string> Authors,
    string? Description,
    IReadOnlyList<string> Categories,
    string? Publisher,
    string? PublishedDate,
    int? PageCount,
    string? CoverUrl,
    string? Language,
    string? PreviewLink,
    string? InfoLink);
