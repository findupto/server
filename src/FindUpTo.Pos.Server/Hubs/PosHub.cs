using FindUpTo.Pos.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FindUpTo.Pos.Server.Hubs;

[Authorize]
public sealed class PosHub(CoreDbContext db) : Hub
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
    public async Task JoinDeviceGroup()
    {
        var id = Context.User?.FindFirstValue("device_id");
        if (Context.User?.IsInRole("Device") == true && int.TryParse(id, out var deviceId)) await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}");
    }
    public async Task JoinConversationGroup(int conversationId)
    {
        var username = Context.User?.Identity?.Name ?? "";
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == conversationId);
        if (conversation is not null && conversation.Participants.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(username, StringComparer.OrdinalIgnoreCase)) await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
    }
    public async Task Typing(int conversationId, bool isTyping)
    {
        var username = Context.User?.Identity?.Name ?? "";
        var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == conversationId);
        if (conversation is null || !conversation.Participants.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(username, StringComparer.OrdinalIgnoreCase)) return;
        await Clients.Group($"conversation:{conversationId}").SendAsync("typing.changed", new { conversationId, username, isTyping });
    }
    public async Task CallSignal(string targetUsername, string callId, string signalType, string payload)
    {
        var sender = Context.User?.Identity?.Name ?? "";
        if (string.IsNullOrWhiteSpace(sender) || string.IsNullOrWhiteSpace(targetUsername) || string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(signalType)) return;
        await Clients.Group($"user:{targetUsername}").SendAsync("call.signal", new { callId, fromUsername = sender, signalType, payload });
    }
}
