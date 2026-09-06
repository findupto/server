using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FindUpTo.Pos.Server.Hubs;

[Authorize]
public sealed class PosHub : Hub
{
    public Task JoinRoleGroup() => Groups.AddToGroupAsync(Context.ConnectionId, $"role:{Context.User?.FindFirst("role")?.Value}");

    public Task JoinUserGroup() => Groups.AddToGroupAsync(Context.ConnectionId, $"user:{Context.UserIdentifier}");
}
