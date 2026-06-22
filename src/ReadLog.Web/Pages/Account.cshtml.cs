using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountModel(IReadLogService readLog, UserManager<ApplicationUser> userManager)
    {
        _readLog = readLog;
        _userManager = userManager;
    }

    public AccountStats Stats { get; private set; } = new(0, new Dictionary<Format, int>());

    public string? DisplayName { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string? ImageUrl { get; private set; }

    public string Initial =>
        (DisplayName ?? Email) is { Length: > 0 } name ? name[..1].ToUpperInvariant() : "?";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // The aggregate comes from the database; the profile comes from the user record.
        Stats = await _readLog.GetAccountStatsAsync(User.GetUserId(), cancellationToken);

        var user = await _userManager.GetUserAsync(User);
        DisplayName = user?.Name;
        Email = user?.Email ?? User.Identity?.Name ?? string.Empty;
        ImageUrl = user?.Image;
    }
}
