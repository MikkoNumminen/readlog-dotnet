using Ganss.Xss;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReadLog.Web.Dtos;
using ReadLog.Web.Services;

namespace ReadLog.Web.Pages;

public class BookModel : PageModel
{
    private readonly IBookDetailsService _bookDetails;
    private readonly IHtmlSanitizer _sanitizer;

    public BookModel(IBookDetailsService bookDetails, IHtmlSanitizer sanitizer)
    {
        _bookDetails = bookDetails;
        _sanitizer = sanitizer;
    }

    public string Title { get; private set; } = string.Empty;
    public string? FallbackCoverUrl { get; private set; }
    public BookDetails? Details { get; private set; }

    /// <summary>The Google Books description, sanitised before it's rendered as raw HTML.</summary>
    public string? SafeDescriptionHtml { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string? title, string? author, string? cover, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return NotFound();
        }

        Title = title;
        FallbackCoverUrl = cover;
        Details = await _bookDetails.GetDetailsAsync(title, author, cancellationToken);

        if (Details?.Description is { Length: > 0 } description)
        {
            SafeDescriptionHtml = _sanitizer.Sanitize(description);
        }

        return Page();
    }
}
