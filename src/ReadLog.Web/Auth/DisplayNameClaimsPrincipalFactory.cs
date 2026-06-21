using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ReadLog.Web.Models;

namespace ReadLog.Web.Auth;

/// <summary>
/// Adds a <c>display_name</c> claim from <see cref="ApplicationUser.Name"/> at sign-in,
/// so the UI can greet the user by name without a per-request database lookup.
/// </summary>
public class DisplayNameClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public const string DisplayNameClaim = "display_name";

    public DisplayNameClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (!string.IsNullOrWhiteSpace(user.Name))
        {
            identity.AddClaim(new Claim(DisplayNameClaim, user.Name));
        }

        return identity;
    }
}
