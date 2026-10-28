using System.Security.Claims;
using OnePondy.Application.Common;
using OnePondy.Application.TripPlanner;

namespace OnePondy.Api.Http;

public sealed record TripPlanBody(
    DateOnly? StartDate,
    DateOnly? EndDate,
    IReadOnlyList<string>? Interests,
    string? Pace,
    string? BudgetBand,
    string? MobilityNeeds,
    double? Lat,
    double? Lng);

/// <summary>The AI trip planner (spec §7, Phase 5): drafts an itinerary from verified places, which the user must still review and save.</summary>
public static class TripPlannerEndpoints
{
    public static IEndpointRouteBuilder MapTripPlannerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/trip-planner/plan", Plan).RequireAuthorization().WithTags("Trip planner");
        return app;
    }

    private static async Task<IResult> Plan(
        TripPlanBody body,
        ClaimsPrincipal user,
        string? lang,
        TripPlannerService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        if (body.StartDate is null)
        {
            errors.Add("startDate", "Required.");
        }

        if (body.EndDate is null)
        {
            errors.Add("endDate", "Required.");
        }

        if (!EndpointResults.TryParse<TripPace>(body.Pace ?? nameof(TripPace.Balanced), out var pace))
        {
            errors.Add("pace", "Choose a pace.");
        }

        if (!EndpointResults.TryParse<TripBudgetBand>(body.BudgetBand ?? nameof(TripBudgetBand.Medium), out var budgetBand))
        {
            errors.Add("budgetBand", "Choose a budget band.");
        }

        if (errors.Any)
        {
            return ProblemResults.From(errors.ToError(), httpContext);
        }

        var request = new TripPlanRequest(
            body.StartDate!.Value,
            body.EndDate!.Value,
            body.Interests ?? [],
            pace,
            budgetBand,
            body.MobilityNeeds,
            body.Lat,
            body.Lng);

        var result = await service.PlanAsync(user.GetUserId(), request, Language(lang, httpContext), cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/itineraries/{result.Value.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static string? Language(string? lang, HttpContext httpContext) => lang ?? EndpointResults.PreferredLanguage(httpContext);
}
