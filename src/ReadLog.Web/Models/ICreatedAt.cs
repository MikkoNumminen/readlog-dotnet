namespace ReadLog.Web.Models;

/// <summary>
/// Marks an entity whose <see cref="CreatedAt"/> is stamped automatically on insert
/// by <see cref="Data.ApplicationDbContext"/>.
/// </summary>
public interface ICreatedAt
{
    DateTime CreatedAt { get; set; }
}
