using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class BackupEndpoints
{
    public static void MapBackupEndpoints(this WebApplication app)
    {
        app.MapPost("/api/admin/backup", async (HttpContext context, CoreDbContext db, IWebHostEnvironment env) =>
        {
            var databasePath = db.Database.GetDbConnection().DataSource;
            if (string.IsNullOrWhiteSpace(databasePath) || databasePath == ":memory:")
                return Results.BadRequest("A file-backed SQLite database is required.");

            var fullPath = Path.GetFullPath(databasePath);
            if (!File.Exists(fullPath)) return Results.NotFound("Database file not found.");

            var backupDirectory = Path.Combine(env.ContentRootPath, "backups");
            Directory.CreateDirectory(backupDirectory);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            var backupPath = Path.Combine(backupDirectory, $"pos-{stamp}.db");
            var escapedPath = backupPath.Replace("'", "''");

            await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{escapedPath}'");
            var info = new FileInfo(backupPath);
            await AuditEndpoints.WriteAsync(db, context.User, "Created", "Backup", stamp, info.Name);
            return Results.Ok(new { fileName = info.Name, sizeBytes = info.Length, createdAtUtc = info.CreationTimeUtc });
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/admin/backups", (IWebHostEnvironment env) =>
        {
            var directory = Path.Combine(env.ContentRootPath, "backups");
            if (!Directory.Exists(directory)) return Results.Ok(Array.Empty<object>());
            var files = Directory.EnumerateFiles(directory, "pos-*.db")
                .Select(path => new FileInfo(path))
                .OrderByDescending(x => x.CreationTimeUtc)
                .Take(100)
                .Select(x => new { fileName = x.Name, sizeBytes = x.Length, createdAtUtc = x.CreationTimeUtc });
            return Results.Ok(files);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));
    }
}
