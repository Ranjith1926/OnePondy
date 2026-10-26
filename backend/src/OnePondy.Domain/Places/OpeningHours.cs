namespace OnePondy.Domain.Places;

public sealed record OpeningPeriod(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);

/// <summary>
/// Weekly opening times. A day with no periods is closed. Periods end on the day they start;
/// overnight opening is not modelled yet because no Phase 3 place needs it.
/// </summary>
public sealed record OpeningHours
{
    public const int MaxPeriodsPerDay = 4;
    public const int MaxNotesLength = 500;

    private OpeningHours(bool alwaysOpen, IReadOnlyList<OpeningPeriod> periods, string? notes)
    {
        AlwaysOpen = alwaysOpen;
        Periods = periods;
        Notes = notes;
    }

    /// <summary>Open at all times, such as a beach or promenade.</summary>
    public bool AlwaysOpen { get; }

    /// <summary>Sorted by day, then opening time.</summary>
    public IReadOnlyList<OpeningPeriod> Periods { get; }

    /// <summary>Free text such as "Closed on public holidays".</summary>
    public string? Notes { get; }

    /// <summary>Validates and normalises opening hours; returns problems instead of throwing.</summary>
    public static (OpeningHours? Hours, IReadOnlyList<string> Problems) Create(
        bool alwaysOpen,
        IEnumerable<OpeningPeriod> periods,
        string? notes)
    {
        var sorted = periods.OrderBy(p => (int)p.Day).ThenBy(p => p.Opens).ToList();
        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        var problems = new List<string>();

        if (alwaysOpen && sorted.Count > 0)
        {
            problems.Add("A place that is always open cannot also have opening periods.");
        }

        if (trimmedNotes is { Length: > MaxNotesLength })
        {
            problems.Add($"Notes must be {MaxNotesLength} characters or fewer.");
        }

        foreach (var day in sorted.GroupBy(p => p.Day))
        {
            var dayPeriods = day.ToList();
            if (dayPeriods.Count > MaxPeriodsPerDay)
            {
                problems.Add($"{day.Key} has more than {MaxPeriodsPerDay} opening periods.");
            }

            for (var i = 0; i < dayPeriods.Count; i++)
            {
                if (dayPeriods[i].Closes <= dayPeriods[i].Opens)
                {
                    problems.Add($"{day.Key}: closing time must be after opening time.");
                }
                else if (i > 0 && dayPeriods[i].Opens < dayPeriods[i - 1].Closes)
                {
                    problems.Add($"{day.Key}: opening periods overlap.");
                }
            }
        }

        return problems.Count > 0
            ? (null, problems)
            : (new OpeningHours(alwaysOpen, sorted, trimmedNotes), problems);
    }

    public bool Equals(OpeningHours? other) =>
        other is not null
        && AlwaysOpen == other.AlwaysOpen
        && Notes == other.Notes
        && Periods.SequenceEqual(other.Periods);

    public override int GetHashCode() => HashCode.Combine(AlwaysOpen, Notes, Periods.Count);
}
