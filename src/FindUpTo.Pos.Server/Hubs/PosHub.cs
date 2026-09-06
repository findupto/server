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

    public async Task Typing(int conversationId, bool isTyping)
    {
        var username = Context.User?.Identity?.Name ?? "";
        if (string.IsNullOrWhiteSpace(username)) return;
        await Clients.Group($"conversation:{conversationId}").SendAsync("typing.changed", new { conversationId, username, isTyping });
    }

    public async Task CallSignal(string targetUsername, string callId, string signalType, string payload)
    {
        var sender = Context.User?.Identity?.Name ?? "";
        if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(targetUsername) || string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(signalType)) return;
        await Clients.Group($"user:{targetUsername}").SendAsync("call.signal", new { callId, fromUsername = sender, signalType, payload });
    }
}
