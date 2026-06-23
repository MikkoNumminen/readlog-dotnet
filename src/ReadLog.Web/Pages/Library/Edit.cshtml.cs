using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReadLog.Web.Auth;
using ReadLog.Web.Dtos;
using ReadLog.Web.Services;

namespace ReadLog.Web.Pages.Library;

[Authorize]
public class EditModel : PageModel
{
    private readonly IReadLogService _readLog;

    public EditModel(IReadLogService readLog)
    {
        _readLog = readLog;
    }

    [BindProperty]
    public UpdateReadEntryRequest Input { get; set; } = new();

    public BookSummaryDto? Book { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var entry = await _readLog.GetEntryAsync(User.GetUserId(), id, cancellationToken);
        if (entry is null)
        {
            return NotFound();
        }

        Book = entry.Book;
        Input = new UpdateReadEntryRequest
        {
            Format = entry.Format,
            FinishedAt = entry.FinishedAt,
            Rating = entry.Rating,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Book = (await _readLog.GetEntryAsync(User.GetUserId(), id, cancellationToken))?.Book;
            return Book is null ? NotFound() : Page();
        }

        var updated = await _readLog.UpdateReadEntryAsync(User.GetUserId(), id, Input, cancellationToken);
        return updated ? RedirectToPage("/Library") : NotFound();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var deleted = await _readLog.DeleteReadEntryAsync(User.GetUserId(), id, cancellationToken);
        return deleted ? RedirectToPage("/Library") : NotFound();
    }
}
