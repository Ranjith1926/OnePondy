using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Common;

namespace OnePondy.UnitTests.Bookings;

/// <summary>A save that lost a race is told apart from a genuine fault, however EF chooses to report it.</summary>
public class WriteConflictsTests
{
    [Fact]
    public void A_failed_save_is_a_lost_race()
    {
        Assert.True(WriteConflicts.Is(new DbUpdateException("duplicate key")));
        Assert.True(WriteConflicts.Is(new DbUpdateConcurrencyException("stale version")));
    }

    [Fact]
    public void A_deadlock_victim_wrapped_as_a_transient_failure_is_also_a_lost_race()
    {
        var wrapped = new InvalidOperationException("likely due to a transient failure", new DbUpdateException("deadlock detected"));

        Assert.True(WriteConflicts.Is(wrapped));
    }

    [Fact]
    public void Anything_else_is_a_real_fault()
    {
        Assert.False(WriteConflicts.Is(new InvalidOperationException("bug")));
        Assert.False(WriteConflicts.Is(new InvalidOperationException("wrapped", new ArgumentException("x"))));
        Assert.False(WriteConflicts.Is(new TimeoutException()));
    }
}
