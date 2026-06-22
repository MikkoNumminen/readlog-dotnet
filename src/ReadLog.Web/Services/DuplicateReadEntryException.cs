namespace ReadLog.Web.Services;

/// <summary>
/// Thrown by <see cref="IReadLogService.LogBookAsync"/> when a read entry for the same
/// (user, book, finished-on date) already exists — the domain-level signal for the unique
/// index violation, so callers don't have to inspect EF/SQLite exceptions.
/// </summary>
public class DuplicateReadEntryException : Exception
{
    public DuplicateReadEntryException()
        : base("A read entry for this book on this date already exists.")
    {
    }
}
