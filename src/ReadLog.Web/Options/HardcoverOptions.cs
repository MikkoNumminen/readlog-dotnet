namespace ReadLog.Web.Options;

/// <summary>
/// Configuration for the Hardcover (hardcover.app) GraphQL API, bound from the
/// "Hardcover" section. When <see cref="ApiToken"/> is absent the Hardcover integration
/// is simply skipped (search falls back to the other providers).
/// </summary>
public class HardcoverOptions
{
    public const string SectionName = "Hardcover";

    /// <summary>
    /// The personal API token from the Hardcover account page. The token Hardcover issues
    /// already includes the "Bearer " scheme; the client tolerates it with or without.
    /// </summary>
    public string? ApiToken { get; set; }
}
