using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class MessagingEndpoints
{
    public static void MapMessagingEndpoints(this WebApplication app)
    {
        app.MapGet("/api/messages/conversations", async (ClaimsPrincipal user, CoreDbContext db) =>
        {
            var username = user.Identity?.Name ?? "";
            return Results.Ok(await db.Conversations.AsNoTracking().Where(x => x.Participants.Contains(username)).OrderByDescending(x => x.UpdatedAtUtc).Take(100).ToListAsync());
        }).RequireAuthorization();

        app.MapGet("/api/messages/{conversationId:int}", async (int conversationId, ClaimsPrincipal user, CoreDbContext db) =>
        {
            var username = user.Identity?.Name ?? "";
            if (!await db.Conversations.AnyAsync(x => x.Id == conversationId && x.Participants.Contains(username))) return Results.Forbid();
            return Results.Ok(await db.Messages.AsNoTracking().Where(x => x.ConversationId == conversationId).OrderBy(x => x.CreatedAtUtc).Take(500).ToListAsync());
        }).RequireAuthorization();

        app.MapPost("/api/messages/conversations", async (CreateConversationRequest input, ClaimsPrincipal user, CoreDbContext db) =>
        {
            var username = user.Identity?.Name ?? "";
            var participants = input.Participants?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            if (!participants.Contains(username, StringComparer.OrdinalIgnoreCase)) participants.Add(username);
            if (participants.Count < 2) return Results.BadRequest("At least two participants are required.");
            var conversation = new Conversation { Title = input.Title?.Trim() ?? "", Participants = string.Join(",", participants) };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();
            return Results.Created($"/api/messages/conversations/{conversation.Id}", conversation);
        }).RequireAuthorization();

        app.MapPost("/api/messages/{conversationId:int}", async (int conversationId, SendMessageRequest input, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var username = user.Identity?.Name ?? "";
            var conversation = await db.Conversations.FindAsync(conversationId);
            if (conversation is null || !conversation.Participants.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(username, StringComparer.OrdinalIgnoreCase)) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(input.Text)) return Results.BadRequest("Message text is required.");
            var message = new Message { ConversationId = conversationId, SenderUsername = username, Text = input.Text.Trim() };
            conversation.UpdatedAtUtc = DateTime.UtcNow;
            db.Messages.Add(message);
            await db.SaveChangesAsync();
            await hub.Clients.Group($"conversation:{conversationId}").SendAsync("message.created", message);
            return Results.Created($"/api/messages/{conversationId}", message);
        }).RequireAuthorization();

        app.MapPost("/api/messages/{conversationId:int}/read", async (int conversationId, ClaimsPrincipal user, CoreDbContext db, IHubContext<PosHub> hub) =>
        {
            var username = user.Identity?.Name ?? "";
            var conversation = await db.Conversations.FindAsync(conversationId);
            if (conversation is null || !conversation.Participants.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(username, StringComparer.OrdinalIgnoreCase)) return Results.Forbid();
            await hub.Clients.Group($"conversation:{conversationId}").SendAsync("messages.read", new { conversationId, username, readAtUtc = DateTime.UtcNow });
            return Results.Ok(new { conversationId, username });
        }).RequireAuthorization();
    }
}

public sealed class Conversation
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Participants { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Message
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public string SenderUsername { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record CreateConversationRequest(string? Title, List<string> Participants);
public sealed record SendMessageRequest(string Text);
