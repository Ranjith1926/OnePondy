namespace OnePondy.Domain.Itineraries;

public enum ItineraryStatus
{
    /// <summary>Not yet committed by the user; only AI-assisted itineraries start here (spec Phase 5).</summary>
    Draft = 1,
    Saved = 2,
    Archived = 3,
}

public enum ItinerarySource
{
    Manual = 1,
    AiAssisted = 2,
}

public sealed record DateRange(DateOnly? Start, DateOnly? End)
{
    public bool IsValid => Start is null || End is null || End >= Start;
}
