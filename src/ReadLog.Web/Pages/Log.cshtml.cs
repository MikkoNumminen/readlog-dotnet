using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReadLog.Web.Auth;
using ReadLog.Web.Dtos;
using ReadLog.Web.Models;
using ReadLog.Web.Services;

namespace ReadLog.Web.Pages;

[Authorize]
public class LogModel : PageModel
{
    private readonly IBookSearchService _search;
    private readonly IReadLogService _readLog;

    public LogModel(IBookSearchService search, IReadLogService readLog)
    {
        _search = search;
        _readLog = readLog;
    }

    // Search box (GET).
    [BindProperty(SupportsGet = true)]
    public string? Title { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Author { get; set; }

    public IReadOnlyList<BookSearchResult> Results { get; private set; } = [];
    public bool Searched { get; private set; }
    public string? ManualOpenLibraryId { get; private set; }

    // The log form (POST) once a book is chosen.
    [BindProperty]
    public LogBookRequest Input { get; set; } = new();

    public bool HasSelection { get; private set; }

    public async Task OnGetAsync(
        string? olid, string? selTitle, string? selAuthor, string? cover, int? pages, int? year,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(olid))
        {
            // A book has been chosen — show the log form prefilled from the query.
            HasSelection = true;
            Input = new LogBookRequest
            {
                OpenLibraryId = olid,
                Title = selTitle ?? string.Empty,
                Author = selAuthor,
                CoverUrl = cover,
                PageCount = pages,
                FirstPublishYear = year,
                Format = Format.Book,
                FinishedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            };
            return;
        }

        var query = string.Join(" ", new[] { Title?.Trim(), Author?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
        if (!string.IsNullOrEmpty(query))
        {
            Searched = true;
            Results = await _search.SearchAsync(query, cancellationToken);
            if (Results.Count == 0)
            {
                ManualOpenLibraryId = $"manual:{Guid.NewGuid():N}";
            }
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        HasSelection = true;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await _readLog.LogBookAsync(User.GetUserId(), Input, cancellationToken);
        }
        catch (DuplicateReadEntryException)
        {
            ModelState.AddModelError(string.Empty, "You've already logged this book with that finished date.");
            return Page();
        }

        return RedirectToPage("/Library");
    }
}
