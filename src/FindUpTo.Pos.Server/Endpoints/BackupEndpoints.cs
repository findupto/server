using System.Text;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Services;

namespace FindUpTo.Pos.Server.Endpoints;

public static class BackupEndpoints
{
    public static void MapBackupEndpoints(this WebApplication app)
    {
        app.MapPost("/api/admin/backup", async (HttpContext context, CoreDbContext db, IWebHostEnvironment env, CancellationToken ct) =>
        {
            try
            {
                await BackupService.Gate.WaitAsync(ct);
                try { var file = await BackupService.CreateBackupAsync(db, env.ContentRootPath, ct); BackupService.PruneBackups(env.ContentRootPath, 30); await AuditEndpoints.WriteAsync(db, context.User, "Created", "Backup", file.Name, "Manual backup"); return Results.Ok(new { fileName = file.Name, sizeBytes = file.Length, createdAtUtc = file.CreationTimeUtc }); }
                finally { BackupService.Gate.Release(); }
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
            catch (FileNotFoundException ex) { return Results.NotFound(ex.Message); }
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapGet("/api/admin/backups", (IWebHostEnvironment env) =>
        {
            var directory = Path.Combine(env.ContentRootPath, "backups");
            if (!Directory.Exists(directory)) return Results.Ok(Array.Empty<object>());
            var files = Directory.EnumerateFiles(directory, "pos-*.db").Select(path => new FileInfo(path)).OrderByDescending(x => x.CreationTimeUtc).Take(100).Select(x => new { fileName = x.Name, sizeBytes = x.Length, createdAtUtc = x.CreationTimeUtc });
            return Results.Ok(files);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin"));

        app.MapPost("/api/admin/restore", async (HttpRequest request, CoreDbContext db, IWebHostEnvironment env, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest("Multipart form data is required.");
            var form = await request.ReadFormAsync(ct); var upload = form.Files.GetFile("file");
            if (upload is null || upload.Length == 0) return Results.BadRequest("A backup file is required.");
            if (!upload.FileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Only .db backup files are supported.");
            if (upload.Length > 512L * 1024 * 1024) return Results.BadRequest("Backup file is too large.");
            var temp = Path.Combine(Path.GetTempPath(), $"pos-restore-{Guid.NewGuid():N}.db");
            try
            {
                await using (var output = File.Create(temp)) await upload.CopyToAsync(output, ct);
                await using (var header = File.OpenRead(temp)) { var bytes = new byte[16]; if (await header.ReadAsync(bytes.AsMemory(0, 16), ct) != 16 || Encoding.ASCII.GetString(bytes) != "SQLite format 3\0") return Results.BadRequest("The uploaded file is not a valid SQLite database."); }
                await BackupService.Gate.WaitAsync(ct);
                try
                {
                    var safety = await BackupService.CreateBackupAsync(db, env.ContentRootPath, ct);
                    db.Database.GetDbConnection().Close();
                    await BackupService.RestoreAsync(temp, db, ct);
                    return Results.Ok(new { restoredFrom = Path.GetFileName(upload.FileName), safetyBackup = safety.Name, message = "Database restored. Restart the server before continuing to process transactions." });
                }
                finally { BackupService.Gate.Release(); }
            }
            finally { try { File.Delete(temp); } catch { } }
        }).RequireAuthorization(p => p.RequireRole("Owner"));
    }
}
