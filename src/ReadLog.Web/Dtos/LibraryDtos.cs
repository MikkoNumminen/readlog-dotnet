using ReadLog.Web.Models;

namespace ReadLog.Web.Dtos;

/// <summary>The catalogue fields shown for a book in a list.</summary>
public record BookSummaryDto(string Title, string? Author, string? CoverUrl);

/// <summary>A read entry as shown in the user's library (and the "have I read this?" lookup).</summary>
public record LibraryEntryDto(int Id, Format Format, DateOnly FinishedAt, int? Rating, BookSummaryDto Book);

/// <summary>
/// An entry on the public "recently read" feed. Deliberately carries <b>no</b> user
/// fields — the feed never exposes who read what.
/// </summary>
public record PublicReadDto(
    string Title, string? Author, string? CoverUrl, Format Format, DateTime CreatedAt, int? Rating);

/// <summary>Aggregate reading stats for a user (the live profile is merged in by the page).</summary>
public record AccountStats(int TotalBooks, IReadOnlyDictionary<Format, int> Formats);
