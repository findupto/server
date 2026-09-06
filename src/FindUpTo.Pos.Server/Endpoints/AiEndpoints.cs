using System.Security.Claims;
using FindUpTo.Pos.Server.Services;

namespace FindUpTo.Pos.Server.Endpoints;

public static class AiEndpoints
{
    private static readonly string[] AllowedRoles = ["Owner", "Manager", "Admin", "Counter"];

    public static void MapAiEndpoints(this WebApplication app)
    {
        app.MapPost("/api/ai/operate", async (AiOperateRequest input, ClaimsPrincipal user, BusinessAiService ai, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(input.Message)) return Results.BadRequest(new { message = "message is required." });
            if (input.Message.Length > 4000) return Results.BadRequest(new { message = "message is limited to 4000 characters." });
            try
            {
                return Results.Ok(await ai.RunAsync(user, input.Message, cancellationToken));
            }
            catch (UnauthorizedAccessException ex) { return Results.Forbid(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 503); }
        }).RequireAuthorization(p => p.RequireRole(AllowedRoles));

        app.MapGet("/api/ai/status", () => Results.Ok(new
        {
            enabled = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")),
            model = Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim() is { Length: > 0 } model ? model : "gpt-5",
            capabilities = new[] { "sales", "payments", "inventory", "purchasing", "receiving", "product-updates", "reports", "receipt-printing" }
        })).RequireAuthorization(p => p.RequireRole(AllowedRoles));
    }

    public sealed record AiOperateRequest(string Message);
}
