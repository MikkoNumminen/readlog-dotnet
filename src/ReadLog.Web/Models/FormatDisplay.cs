namespace ReadLog.Web.Models;

/// <summary>Display strings for the <see cref="Format"/> enum, used across the UI.</summary>
public static class FormatDisplay
{
    public static string Label(this Format format) => format switch
    {
        Format.Audiobook => "Audiobook",
        Format.Ebook => "E-book",
        _ => "Book",
    };

    public static string PluralLabel(this Format format) => format switch
    {
        Format.Audiobook => "Audiobooks",
        Format.Ebook => "E-books",
        _ => "Books",
    };

    public static string Icon(this Format format) => format switch
    {
        Format.Audiobook => "🎧",
        Format.Ebook => "📱",
        _ => "📖",
    };
}
