using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace OnePondy.Application.Common;

/// <summary>
/// Recognises a save that lost a race with another request. EF reports it as a <see cref="DbUpdateException"/> (a stale row
/// version, a duplicate key), or, when PostgreSQL picked this request as the loser of a deadlock, as an
/// <see cref="InvalidOperationException"/> that wraps one ("likely due to a transient failure"). Either way the answer is the
/// same: nothing was saved, and the caller should reload and try again.
/// </summary>
public static class WriteConflicts
{
    public static bool Is(Exception exception) =>
        exception is DbUpdateException or InvalidOperationException { InnerException: DbUpdateException };

    /// <summary>
    /// Recognises a rule in the database (a trigger that raises an exception, SQLSTATE P0001) refusing a save. Rules that are checked when a
    /// transaction commits surface as the database's own exception rather than as EF's <see cref="DbUpdateException"/>, so
    /// <see cref="Is"/> does not see them. Only for a caller that knows what rule it is up against (such as a balance that must not go below
    /// nothing), and can say so; anything else should still fail loudly.
    /// </summary>
    public static bool IsDatabaseRule(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: "P0001" })
            {
                return true;
            }
        }

        return false;
    }
}
