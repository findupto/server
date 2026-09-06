using System.Printing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:18991");
var agentKey = Environment.GetEnvironmentVariable("FINDUPTO_PRINTER_AGENT_KEY");
if (string.IsNullOrWhiteSpace(agentKey) || agentKey.Length < 32)
    throw new InvalidOperationException("FINDUPTO_PRINTER_AGENT_KEY must be configured and at least 32 characters long.");

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "FindUpTo.PrinterAgent" }));

app.MapGet("/printers", () => Results.Ok(PrinterService.Discover()))
   .RequireAgentKey(agentKey);

app.MapPost("/print/raw", async (RawPrintRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.PrinterName) || string.IsNullOrWhiteSpace(request.DataBase64))
        return Results.BadRequest(new { error = "printerName and dataBase64 are required" });

    byte[] data;
    try { data = Convert.FromBase64String(request.DataBase64); }
    catch (FormatException) { return Results.BadRequest(new { error = "dataBase64 is invalid" }); }

    try
    {
        RawPrinter.Print(request.PrinterName, data);
        return Results.Ok(new { printed = true, printer = request.PrinterName });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Printer error: {ex.Message}", statusCode: 503);
    }
}).RequireAgentKey(agentKey);

app.Run();

public sealed record RawPrintRequest(string PrinterName, string DataBase64);
public sealed record PrinterInfo(string Name, string Connection, bool Connected, string Kind, string? Port, string? Driver);

static class AgentAuthExtensions
{
    public static RouteHandlerBuilder RequireAgentKey(this RouteHandlerBuilder endpoint, string expectedKey)
    {
        endpoint.AddEndpointFilter(async (context, next) =>
        {
            if (!context.HttpContext.Request.Headers.TryGetValue("X-FindUpTo-Agent-Key", out var provided))
                return Results.Unauthorized();
            var supplied = Encoding.UTF8.GetBytes(provided.ToString());
            var expected = Encoding.UTF8.GetBytes(expectedKey);
            return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected)
                ? await next(context)
                : Results.Unauthorized();
        });
        return endpoint;
    }
}

static class PrinterService
{
    public static IReadOnlyList<PrinterInfo> Discover()
    {
        using var server = new LocalPrintServer();
        return server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections })
            .Select(q =>
            {
                q.Refresh();
                var name = q.Name;
                var port = Safe(() => q.QueuePort?.Name);
                var driver = Safe(() => q.QueueDriver?.Name);
                var connected = Safe(() => !q.IsOffline) ?? true;
                var kind = Classify(name, port, driver);
                var connection = port ?? "Windows spooler";
                q.Dispose();
                return new PrinterInfo(name, connection, connected, kind, port, driver);
            })
            .OrderByDescending(x => x.Connected)
            .ThenBy(x => x.Kind)
            .ThenBy(x => x.Name)
            .ToList();
    }

    static string Classify(string name, string? port, string? driver)
    {
        var s = $"{name} {port} {driver}".ToLowerInvariant();
        if (s.Contains("thermal") || s.Contains("receipt") || s.Contains("pos") || s.Contains("80mm") || s.Contains("58mm")) return "Thermal80mm";
        return "A4";
    }

    static T? Safe<T>(Func<T> action)
    {
        try { return action(); } catch { return default; }
    }
}

static class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    sealed class DOCINFO { public string pDocName = "FindUpTo POS"; public string? pOutputFile; public string pDataType = "RAW"; }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool OpenPrinter(string name, out nint handle, nint defaults);
    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    static extern bool ClosePrinter(nint handle);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int StartDocPrinter(nint handle, int level, [In] DOCINFO doc);
    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    static extern bool EndDocPrinter(nint handle);
    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    static extern bool StartPagePrinter(nint handle);
    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    static extern bool EndPagePrinter(nint handle);
    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    static extern bool WritePrinter(nint handle, byte[] buffer, int count, out int written);

    public static void Print(string printerName, byte[] data)
    {
        if (!OpenPrinter(printerName, out var handle, 0)) ThrowWin32("OpenPrinter");
        try
        {
            var doc = new DOCINFO();
            if (StartDocPrinter(handle, 1, doc) == 0) ThrowWin32("StartDocPrinter");
            try
            {
                if (!StartPagePrinter(handle)) ThrowWin32("StartPagePrinter");
                try
                {
                    if (!WritePrinter(handle, data, data.Length, out var written) || written != data.Length) ThrowWin32("WritePrinter");
                }
                finally { EndPagePrinter(handle); }
            }
            finally { EndDocPrinter(handle); }
        }
        finally { ClosePrinter(handle); }
    }

    static void ThrowWin32(string operation) => throw new InvalidOperationException($"{operation} failed with Win32 error {Marshal.GetLastWin32Error()}.");
}
