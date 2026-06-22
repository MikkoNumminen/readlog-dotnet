using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReadLog.Web.Auth;
using ReadLog.Web.Dtos;
using ReadLog.Web.Services;

namespace ReadLog.Web.Pages;

[Authorize]
public class LibraryModel : PageModel
{
    private readonly IReadLogService _readLog;

    public LibraryModel(IReadLogService readLog)
    {
        _readLog = readLog;
    }

    public IReadOnlyList<LibraryEntryDto> Entries { get; private set; } = [];

    public string View { get; private set; } = "grid";

    // "Have I read this?" lookup.
    public string? Query { get; private set; }
    public bool Searched { get; private set; }
    public IReadOnlyList<LibraryEntryDto> SearchResults { get; private set; } = [];

    public async Task OnGetAsync(string? q, string? view, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        View = view == "list" ? "list" : "grid";
        Entries = await _readLog.GetMyBooksAsync(userId, cancellationToken);

        if (q is not null)
        {
            Query = q;
            Searched = !string.IsNullOrWhiteSpace(q);
            SearchResults = await _readLog.CheckIfReadAsync(userId, q, cancellationToken);
        }
    }
}
