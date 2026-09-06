using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace FindUpTo.Pos.Server.Hubs;

[Authorize]
public sealed class PosHub : Hub
{
    public async Task JoinRoleGroup()
    {
        var role = Context.User?.FindFirstValue(ClaimTypes.Role);
        if (!string.IsNullOrWhiteSpace(role)) await Groups.AddToGroupAsync(Context.ConnectionId, $"role:{role}");
    }

    public async Task JoinUserGroup()
    {
        if (!string.IsNullOrWhiteSpace(Context.UserIdentifier)) await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{Context.UserIdentifier}");
    }

    public Task JoinConversationGroup(int conversationId) => Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
}
