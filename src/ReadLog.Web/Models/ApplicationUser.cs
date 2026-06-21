using Microsoft.AspNetCore.Identity;

namespace ReadLog.Web.Models;

/// <summary>
/// The application user. Extends <see cref="IdentityUser"/> (string GUID key, email,
/// password hash, external logins) with the profile fields ReadLog displays.
/// </summary>
public class ApplicationUser : IdentityUser, ICreatedAt
{
    public string? Name { get; set; }

    public string? Image { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<ReadEntry> ReadEntries { get; set; } = new List<ReadEntry>();
}
