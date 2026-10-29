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
}
