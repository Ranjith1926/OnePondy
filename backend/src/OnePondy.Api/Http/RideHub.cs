using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OnePondy.Application.Identity;
using OnePondy.Application.Rides;

namespace OnePondy.Api.Http;

/// <summary>
/// Live updates for rides (spec Phase 9: SignalR updates). A signed-in person connects and is put in a group of their own, so they can only ever hear
/// about their own rides. The hub has no methods a client can call and carries no personal data and no position: the only message is
/// <c>rideChanged</c> with a ride id, which tells the app to ask the API for the ride. A client that was disconnected asks anyway when it reconnects,
/// and polls while it is disconnected, so a missed message never leaves a screen out of date.
/// </summary>
[Authorize]
public sealed class RideHub : Hub
{
    public const string Path = "/hubs/rides";

    public const string RideChangedMessage = "rideChanged";

    public static string GroupFor(Guid userId) => $"user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        if (Context.User is { } user)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(user.GetUserId()));
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>Pushes "this ride changed" to the rider and the driver over the hub.</summary>
public sealed class SignalRRideNotifier(IHubContext<RideHub> hub) : IRideNotifier
{
    public async Task RideChangedAsync(Guid rideId, Guid riderUserId, Guid? driverUserId, CancellationToken cancellationToken)
    {
        var groups = new List<string> { RideHub.GroupFor(riderUserId) };
        if (driverUserId is { } driver)
        {
            groups.Add(RideHub.GroupFor(driver));
        }

        await hub.Clients.Groups(groups).SendAsync(RideHub.RideChangedMessage, new { rideId }, cancellationToken);
    }
}
