namespace ReadLog.Web.Models;

/// <summary>A single "I finished this book" record, owned by one user.</summary>
public class ReadEntry : ICreatedAt
{
    public int Id { get; set; }

    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    public Format Format { get; set; } = Format.Book;

    /// <summary>The day the book was finished (date only — no time component).</summary>
    public DateOnly FinishedAt { get; set; }

    /// <summary>Optional 0–5 star rating. Null means "no rating"; 0 is a real value.</summary>
    public int? Rating { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
}
