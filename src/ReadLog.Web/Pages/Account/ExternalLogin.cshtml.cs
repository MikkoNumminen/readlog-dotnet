using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ReadLog.Web.Models;

namespace ReadLog.Web.Pages.Account;

/// <summary>
/// Callback target for an external (e.g. Google) sign-in. Signs the user in if the
/// external login is already linked; otherwise provisions a local account from the
/// provider's claims and links the login.
/// </summary>
public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ExternalLoginModel> _logger;

    public ExternalLoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<ExternalLoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    public IActionResult OnGet() => RedirectToPage("/Account/Login");

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/");

        if (remoteError is not null)
        {
            return FailToLogin($"Error from the external provider: {remoteError}", returnUrl);
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return FailToLogin("Could not load external login information.", returnUrl);
        }

        // Already linked → just sign in.
        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);
        if (signInResult.Succeeded)
        {
            return LocalRedirect(returnUrl);
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
        {
            return FailToLogin("The external provider did not supply an email address.", returnUrl);
        }

        // If a local account already exists for this email, do NOT silently link the
        // external login to it: an email match alone is not proof of ownership, so
        // auto-linking would be an account-takeover path. Require the user to sign in
        // locally first (the external login can then be linked from their account).
        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            return FailToLogin(
                "An account with this email already exists. Sign in with your password first, then link this provider.",
                returnUrl);
        }

        // No local account yet: provision one from the provider's claims and link the login.
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            Name = info.Principal.FindFirstValue(ClaimTypes.Name),
        };

        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            return FailToLogin("Could not create an account from the external login.", returnUrl);
        }

        var linkResult = await _userManager.AddLoginAsync(user, info);
        if (!linkResult.Succeeded)
        {
            return FailToLogin("Could not link the external login to your account.", returnUrl);
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        _logger.LogInformation("User signed in with {Provider} external login.", info.LoginProvider);
        return LocalRedirect(returnUrl);
    }

    private RedirectToPageResult FailToLogin(string message, string returnUrl)
    {
        TempData["LoginError"] = message;
        return RedirectToPage("/Account/Login", new { returnUrl });
    }
}
