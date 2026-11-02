namespace OnePondy.Application.Reviews;

/// <summary>Settings for reviews (spec §7 and Phase 10, decisions R2, R5, R7 and R8 in docs/phase-10-plan.md).</summary>
public sealed class ReviewOptions
{
    public const string SectionName = "Reviews";

    /// <summary>How many days after a booking completes or a food order is delivered the customer may still review it.</summary>
    public int WindowDays { get; set; } = 30;

    /// <summary>How long after posting a review its author may change it (once).</summary>
    public TimeSpan EditWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// A business shows an average rating only once it has this many published reviews, because one or two reviews are not a
    /// trustworthy average.
    /// </summary>
    public int MinCountToShow { get; set; } = 3;

    /// <summary>
    /// Words the automatic check flags for the moderator, matched as whole words without regard to case. Empty by default: the list is the
    /// operator's to write, in the languages their customers use.
    /// </summary>
    public List<string> BlockedWords { get; set; } = [];
}
