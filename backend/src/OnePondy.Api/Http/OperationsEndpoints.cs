using OnePondy.Application.Operations;

namespace OnePondy.Api.Http;

/// <summary>What an administrator should look at (spec Phase 10, step 10.5). Administrators only: it shows the state of the whole platform.</summary>
public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var operations = app.MapGroup("/api/v1/admin/operations").RequireAuthorization(StaffPolicies.Administration).WithTags("Console operations");
        operations.MapGet("/", async (OperationsService service, CancellationToken ct) => Results.Ok(await service.SnapshotAsync(ct)));
        return app;
    }
}
