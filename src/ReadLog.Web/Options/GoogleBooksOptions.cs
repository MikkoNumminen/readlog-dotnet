namespace ReadLog.Web.Options;

/// <summary>
/// Configuration for the Google Books API, bound from the "GoogleBooks" section.
/// When <see cref="ApiKey"/> is absent the Google Books integration is simply
/// skipped (search falls back to Open Library only).
/// </summary>
public class GoogleBooksOptions
{
    public const string SectionName = "GoogleBooks";

    public string? ApiKey { get; set; }
}
