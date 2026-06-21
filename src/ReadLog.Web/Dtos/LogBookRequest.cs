using System.ComponentModel.DataAnnotations;
using ReadLog.Web.Models;

namespace ReadLog.Web.Dtos;

/// <summary>Input for logging a finished book. Bound from the log form and validated.</summary>
public class LogBookRequest
{
    /// <summary>Provider key: an Open Library work id, a <c>google:…</c>, or a <c>manual:…</c>.</summary>
    [Required]
    public string OpenLibraryId { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    public string Title { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Author { get; set; }

    [Url]
    public string? CoverUrl { get; set; }

    [Range(1, 100_000)]
    public int? PageCount { get; set; }

    [Range(1, 3000)]
    public int? FirstPublishYear { get; set; }

    public Format Format { get; set; } = Format.Book;

    [DataType(DataType.Date)]
    public DateOnly FinishedAt { get; set; }

    [Range(0, 5)]
    public int? Rating { get; set; }
}
