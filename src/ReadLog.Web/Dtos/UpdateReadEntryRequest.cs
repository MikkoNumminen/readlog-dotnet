using System.ComponentModel.DataAnnotations;
using ReadLog.Web.Models;

namespace ReadLog.Web.Dtos;

/// <summary>Input for editing a read entry. The edit form posts the full current state.</summary>
public class UpdateReadEntryRequest
{
    [Required]
    [StringLength(500)]
    public string Title { get; set; } = string.Empty;

    public Format Format { get; set; } = Format.Book;

    [DataType(DataType.Date)]
    public DateOnly FinishedAt { get; set; }

    /// <summary>0–5 stars; <c>null</c> clears the rating, <c>0</c> is a real value.</summary>
    [Range(0, 5)]
    public int? Rating { get; set; }
}
