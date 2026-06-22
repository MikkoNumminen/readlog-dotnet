using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReadLog.Web.Auth;
using ReadLog.Web.Dtos;
using ReadLog.Web.Models;
using ReadLog.Web.Services;

namespace ReadLog.Web.Pages;

[Authorize]
public class AccountModel : PageModel
{
    private readonly IReadLogService _readLog;

    public AccountModel(IReadLogService readLog)
    {
        _readLog = readLog;
    }

    public AccountStats Stats { get; private set; } = new(0, new Dictionary<Format, int>());

    public string? DisplayName { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string Initial =>
        (DisplayName ?? Email) is { Length: > 0 } name ? name[..1].ToUpperInvariant() : "?";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // The aggregate comes from the database; identity comes live from the cookie.
        Stats = await _readLog.GetAccountStatsAsync(User.GetUserId(), cancellationToken);
        DisplayName = User.FindFirstValue(DisplayNameClaimsPrincipalFactory.DisplayNameClaim);
        Email = User.Identity?.Name ?? string.Empty;
    }
}
