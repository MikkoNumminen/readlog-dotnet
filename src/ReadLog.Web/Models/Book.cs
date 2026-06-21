namespace ReadLog.Web.Models;

/// <summary>
/// A book in the shared catalogue: one row per real-world work, reused across every
/// user's read entries and keyed for idempotent find-or-create by <see cref="OpenLibraryId"/>.
/// </summary>
public class Book : ICreatedAt
{
    public int Id { get; set; }

    public required string Title { get; set; }

    public string? Author { get; set; }

    public string? CoverUrl { get; set; }

    /// <summary>
    /// Provider id used as the natural key for find-or-create. Holds an Open Library
    /// work key (e.g. <c>/works/OL1W</c>), a <c>google:&lt;id&gt;</c>, or a
    /// <c>manual:&lt;timestamp&gt;</c> for hand-entered books. Unique, nullable.
    /// </summary>
    public string? OpenLibraryId { get; set; }

    public int? PageCount { get; set; }

    public int? FirstPublishYear { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<ReadEntry> ReadEntries { get; set; } = new List<ReadEntry>();
}
