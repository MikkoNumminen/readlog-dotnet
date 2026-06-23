using System.ComponentModel.DataAnnotations;
using ReadLog.Web.Models;
using ReadLog.Web.Validation;

namespace ReadLog.Web.Dtos;

/// <summary>Input for editing a read entry. The edit form posts the full current state.</summary>
public class UpdateReadEntryRequest
{
    public Format Format { get; set; } = Format.Book;

    [DataType(DataType.Date)]
    [NotInFuture]
    [Display(Name = "Finished on")]
    public DateOnly FinishedAt { get; set; }

    /// <summary>0–5 stars; <c>null</c> clears the rating, <c>0</c> is a real value.</summary>
    [Range(0, 5)]
    public int? Rating { get; set; }
}
