namespace OnePondy.Infrastructure.TripPlanner;

/// <summary>Settings for the AI trip planner's provider (spec D5: a hosted LLM API with structured output, called server-only).</summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Ai:Gemini";

    /// <summary>Null (the default) means the provider is not configured; the planner then reports itself unavailable.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gemini-2.5-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}
