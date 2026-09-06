using FindUpTo.Pos.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FindUpTo.Pos.Server.Hubs;

[Authorize]
public sealed class PosHub(CoreDbContext db) : Hub
{
    public async Task JoinRoleGroup(){var role=Context.User?.FindFirstValue(ClaimTypes.Role);if(!string.IsNullOrWhiteSpace(role))await Groups.AddToGroupAsync(Context.ConnectionId,$"role:{role}");}
    public async Task JoinUserGroup(){var username=Context.User?.Identity?.Name;if(!string.IsNullOrWhiteSpace(username))await Groups.AddToGroupAsync(Context.ConnectionId,$"user:{username}");}
    public async Task JoinDeviceGroup(){var id=Context.User?.FindFirstValue("device_id");if(Context.User?.IsInRole("Device")==true&&int.TryParse(id,out var deviceId))await Groups.AddToGroupAsync(Context.ConnectionId,$"device:{deviceId}");}
    public async Task JoinTrackingGroup(string trackingCode)
    {
        if (string.IsNullOrWhiteSpace(trackingCode) || trackingCode.Length > 64) return;
        var tracking = await db.DeliveryTrackings.AsNoTracking().SingleOrDefaultAsync(x => x.TrackingCode == trackingCode.Trim()); if (tracking is null) return;
        var customerId = Context.User?.FindFirstValue("customer_id");
        if (Context.User?.IsInRole("Customer") == true && int.TryParse(customerId, out var id)) { var owns = await db.Orders.AsNoTracking().AnyAsync(x => x.Id == tracking.OrderId && x.CustomerId == id); if (!owns) return; }
        else if (Context.User?.IsInRole("Rider") != true && Context.User?.IsInRole("Owner") != true && Context.User?.IsInRole("Manager") != true && Context.User?.IsInRole("Admin") != true && Context.User?.IsInRole("Counter") != true) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tracking:{tracking.TrackingCode}");
    }
    public async Task JoinConversationGroup(int conversationId){if(conversationId<=0)return;var username=Context.User?.Identity?.Name;if(string.IsNullOrWhiteSpace(username))return;var conversation=await db.Conversations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==conversationId);if(conversation is null)return;if(conversation.Participants.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Contains(username,StringComparer.OrdinalIgnoreCase))await Groups.AddToGroupAsync(Context.ConnectionId,$"conversation:{conversationId}");}
    public async Task Typing(int conversationId,bool isTyping){if(conversationId<=0)return;var username=Context.User?.Identity?.Name;if(string.IsNullOrWhiteSpace(username))return;var conversation=await db.Conversations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==conversationId);if(conversation is null)return;var participants=conversation.Participants.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);if(!participants.Contains(username,StringComparer.OrdinalIgnoreCase))return;await Clients.Group($"conversation:{conversationId}").SendAsync("typing.changed",new{conversationId,username,isTyping});}
    public async Task CallSignal(string targetUsername,string callId,string signalType,string payload){var sender=Context.User?.Identity?.Name;if(string.IsNullOrWhiteSpace(sender)||string.IsNullOrWhiteSpace(targetUsername)||string.IsNullOrWhiteSpace(callId)||string.IsNullOrWhiteSpace(signalType)||string.Equals(sender,targetUsername,StringComparison.OrdinalIgnoreCase))return;if(targetUsername.Length>128||callId.Length>128||signalType.Length>64||payload.Length>256*1024)return;var targetExists=await db.Users.AsNoTracking().AnyAsync(x=>x.Username==targetUsername);if(!targetExists)return;await Clients.Group($"user:{targetUsername}").SendAsync("call.signal",new{callId,fromUsername=sender,signalType,payload});}
}
