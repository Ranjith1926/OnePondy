using System.Security.Claims;
using OnePondy.Application.Support;

namespace OnePondy.Api.Http;

public sealed record CreateTicketBody(string? Topic, string? Subject, string? Message, Guid? BookingId, Guid? FoodOrderId, Guid? BusinessId);

public sealed record TicketMessageBody(string? Body);

public sealed record AgentTicketMessageBody(uint Version, string? Body);

public sealed record TicketVersionBody(uint Version);

public sealed record EscalateTicketBody(uint Version, string? Reason);

/// <summary>
/// Support requests (spec Phase 10, step 10.3). A customer, or a business owner about their business, writes and follows their own requests;
/// support agents and administrators work the queue in the console. Anyone can read who the grievance officer is.
/// </summary>
public static class SupportEndpoints
{
    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        var support = app.MapGroup("/api/v1/support").WithTags("Support");
        support.MapGet("/grievance", (SupportService service) => Results.Ok(service.Grievance()));
        support.MapPost("/tickets", Create).RequireAuthorization();
        support.MapGet("/tickets", ListMine).RequireAuthorization();
        support.MapGet("/tickets/{id:guid}", GetMine).RequireAuthorization();
        support.MapPost("/tickets/{id:guid}/messages", Reply).RequireAuthorization();

        var admin = app.MapGroup("/api/v1/admin/support").RequireAuthorization(StaffPolicies.Support).WithTags("Console support");
        admin.MapGet("/tickets", AgentList);
        admin.MapGet("/counts", async (SupportService service, CancellationToken ct) => Results.Ok(await service.CountsAsync(ct)));
        admin.MapGet("/tickets/{id:guid}", AgentGet);
        admin.MapGet("/tickets/{id:guid}/contact", Contact);
        admin.MapPost("/tickets/{id:guid}/messages", AgentReply);
        admin.MapPost("/tickets/{id:guid}/assign", Assign);
        admin.MapPost("/tickets/{id:guid}/release", Release);
        admin.MapPost("/tickets/{id:guid}/resolve", Resolve);
        admin.MapPost("/tickets/{id:guid}/escalate", Escalate);

        return app;
    }

    private static async Task<IResult> Create(CreateTicketBody body, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct)
    {
        var result = await service.CreateAsync(
            user.GetUserId(),
            new NewTicketInput(body.Topic, body.Subject, body.Message, body.BookingId, body.FoodOrderId, body.BusinessId),
            ct);
        return result.IsSuccess
            ? Results.Created($"/api/v1/support/tickets/{result.Value.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> ListMine(ClaimsPrincipal user, SupportService service, CancellationToken ct) =>
        Results.Ok(await service.ListMineAsync(user.GetUserId(), ct));

    private static async Task<IResult> GetMine(Guid id, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetMineAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reply(Guid id, TicketMessageBody body, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ReplyAsync(id, user.GetUserId(), new MessageInput(body.Body), ct), httpContext);

    private static async Task<IResult> AgentList(string? view, int? page, int? pageSize, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AgentListAsync(new AgentTicketQuery(view, page, pageSize), user.GetUserId(), ct), httpContext);

    private static async Task<IResult> AgentGet(Guid id, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AgentGetAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Contact(Guid id, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ContactAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> AgentReply(Guid id, AgentTicketMessageBody body, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AgentReplyAsync(id, new AgentMessageInput(body.Version, body.Body), user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Assign(Guid id, TicketVersionBody body, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AssignAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Release(Guid id, TicketVersionBody body, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ReleaseAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Resolve(Guid id, TicketVersionBody body, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ResolveAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Escalate(Guid id, EscalateTicketBody body, ClaimsPrincipal user, SupportService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.EscalateAsync(id, new EscalateInput(body.Version, body.Reason), user.GetUserId(), ct), httpContext);
}
