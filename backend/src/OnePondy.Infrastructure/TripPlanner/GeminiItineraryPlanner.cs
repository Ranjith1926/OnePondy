using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnePondy.Application.Common;
using OnePondy.Application.TripPlanner;

namespace OnePondy.Infrastructure.TripPlanner;

/// <summary>
/// Calls Gemini's <c>generateContent</c> endpoint with a response schema that restricts each
/// suggested stop's place id to the candidate set, so the model cannot even syntactically name a
/// place it was not given. <see cref="TripPlannerService"/> still re-validates every id: a schema
/// only shapes the response, it does not guarantee the model respected it.
/// </summary>
public sealed class GeminiItineraryPlanner(HttpClient httpClient, IOptions<GeminiOptions> options, ILogger<GeminiItineraryPlanner> logger)
    : IItineraryPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly GeminiOptions _options = options.Value;

    public async Task<Result<IReadOnlyList<PlannerSuggestion>>> SuggestAsync(
        TripPlanRequest request,
        IReadOnlyList<PlannerCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return TripPlannerErrors.ProviderUnavailable;
        }

        var dayCount = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
        var body = BuildRequest(request, candidates, dayCount);

        GeminiResponse? response;
        try
        {
            using var httpResponse = await httpClient.PostAsJsonAsync(
                $"{_options.BaseUrl}/models/{_options.Model}:generateContent?key={_options.ApiKey}",
                body,
                JsonOptions,
                cancellationToken);
            if (!httpResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Gemini returned {StatusCode} for a trip plan request", (int)httpResponse.StatusCode);
                return TripPlannerErrors.ProviderUnavailable;
            }

            response = await httpResponse.Content.ReadFromJsonAsync<GeminiResponse>(JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Could not reach Gemini for a trip plan request");
            return TripPlannerErrors.ProviderUnavailable;
        }

        var text = response?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("Gemini returned no content for a trip plan request");
            return TripPlannerErrors.ProviderUnavailable;
        }

        GeminiPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<GeminiPlan>(text, JsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Could not parse Gemini's trip plan response");
            return TripPlannerErrors.ProviderUnavailable;
        }

        if (plan?.Stops is null)
        {
            return TripPlannerErrors.ProviderUnavailable;
        }

        IReadOnlyList<PlannerSuggestion> suggestions =
        [
            .. plan.Stops
                .Where(s => Guid.TryParse(s.PlaceId, out _))
                .Select(s => new PlannerSuggestion(Guid.Parse(s.PlaceId!), s.DayNumber, s.StopOrder)),
        ];
        return Result<IReadOnlyList<PlannerSuggestion>>.Success(suggestions);
    }

    private static GeminiRequest BuildRequest(TripPlanRequest request, IReadOnlyList<PlannerCandidate> candidates, int dayCount)
    {
        var candidateLines = candidates.Select(c => $"- id={c.PlaceId}, name=\"{c.Name}\", category={c.CategoryCode}");
        var prompt = string.Join(
            '\n',
            "You are planning a multi-day visitor itinerary for Puducherry, India, using ONLY the candidate places listed below.",
            "Select and order a subset of these candidates into a day-by-day plan.",
            $"Trip length: {dayCount} day(s), numbered 1 to {dayCount}.",
            $"Interests: {(request.Interests.Count == 0 ? "no particular preference" : string.Join(", ", request.Interests))}.",
            $"Pace: {request.Pace} (relaxed = 2-3 stops/day, balanced = 4-5, packed = 6 or more).",
            $"Budget band: {request.BudgetBand}.",
            string.IsNullOrWhiteSpace(request.MobilityNeeds) ? null : $"Mobility needs: {request.MobilityNeeds}.",
            "Candidates:",
            string.Join('\n', candidateLines),
            "Every stop's placeId must be exactly one of the candidate ids above. Do not invent ids, names or places.");

        var candidateIds = candidates.Select(c => c.PlaceId.ToString()).ToList();
        return new GeminiRequest(
            [new GeminiContent("user", [new GeminiPart(prompt)])],
            new GeminiGenerationConfig(
                "application/json",
                new GeminiSchema(
                    "OBJECT",
                    new Dictionary<string, GeminiSchema>
                    {
                        ["stops"] = new GeminiSchema(
                            "ARRAY",
                            Items: new GeminiSchema(
                                "OBJECT",
                                new Dictionary<string, GeminiSchema>
                                {
                                    ["placeId"] = new GeminiSchema("STRING", Enum: candidateIds),
                                    ["dayNumber"] = new GeminiSchema("INTEGER"),
                                    ["stopOrder"] = new GeminiSchema("INTEGER"),
                                },
                                Required: ["placeId", "dayNumber", "stopOrder"])),
                    },
                    Required: ["stops"])));
    }

    private sealed record GeminiRequest(
        [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);

    private sealed record GeminiContent(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart([property: JsonPropertyName("text")] string Text);

    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("responseMimeType")] string ResponseMimeType,
        [property: JsonPropertyName("responseSchema")] GeminiSchema ResponseSchema);

    private sealed record GeminiSchema(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, GeminiSchema>? Properties = null,
        [property: JsonPropertyName("items")] GeminiSchema? Items = null,
        [property: JsonPropertyName("enum")] IReadOnlyList<string>? Enum = null,
        [property: JsonPropertyName("required")] IReadOnlyList<string>? Required = null);

    private sealed record GeminiResponse([property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates);

    private sealed record GeminiCandidate([property: JsonPropertyName("content")] GeminiContent? Content);

    private sealed record GeminiPlan([property: JsonPropertyName("stops")] IReadOnlyList<GeminiStop>? Stops);

    private sealed record GeminiStop(
        [property: JsonPropertyName("placeId")] string? PlaceId,
        [property: JsonPropertyName("dayNumber")] int DayNumber,
        [property: JsonPropertyName("stopOrder")] int StopOrder);
}
