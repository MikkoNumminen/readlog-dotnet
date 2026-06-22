using Ganss.Xss;

namespace ReadLog.Web.Services;

/// <summary>
/// Builds the <see cref="IHtmlSanitizer"/> used to clean third-party (Google Books)
/// description HTML before it's rendered. On top of the default allowlist (which strips
/// scripts, event handlers, javascript: URIs, etc.) it also drops the <c>target</c>
/// attribute, so a sanitised link can't open <c>target="_blank"</c> without
/// <c>rel="noopener"</c> (reverse-tabnabbing).
/// </summary>
public static class BookDescriptionSanitizer
{
    public static IHtmlSanitizer Create()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedAttributes.Remove("target");
        return sanitizer;
    }
}
