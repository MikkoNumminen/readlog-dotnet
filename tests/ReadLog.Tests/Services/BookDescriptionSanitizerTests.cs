using ReadLog.Web.Services;

namespace ReadLog.Tests.Services;

public class BookDescriptionSanitizerTests
{
    private static readonly Ganss.Xss.IHtmlSanitizer Sanitizer = BookDescriptionSanitizer.Create();

    [Fact]
    public void Strips_script_tags()
    {
        var clean = Sanitizer.Sanitize("<p>Hello</p><script>alert('xss')</script>");

        Assert.DoesNotContain("<script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", clean);
    }

    [Fact]
    public void Strips_inline_event_handlers()
    {
        var clean = Sanitizer.Sanitize("<img src=\"x\" onerror=\"alert(1)\" />");

        Assert.DoesNotContain("onerror", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Strips_javascript_uris()
    {
        var clean = Sanitizer.Sanitize("<a href=\"javascript:alert(1)\">click</a>");

        Assert.DoesNotContain("javascript:", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Drops_target_attribute_to_prevent_tabnabbing()
    {
        var clean = Sanitizer.Sanitize("<a href=\"https://example.com\" target=\"_blank\">link</a>");

        Assert.DoesNotContain("target", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://example.com", clean); // the safe link itself survives
    }

    [Fact]
    public void Keeps_safe_formatting_markup()
    {
        var clean = Sanitizer.Sanitize("<p>A <b>bold</b> and <i>italic</i> blurb.</p>");

        Assert.Contains("<b>bold</b>", clean);
        Assert.Contains("<i>italic</i>", clean);
    }
}
