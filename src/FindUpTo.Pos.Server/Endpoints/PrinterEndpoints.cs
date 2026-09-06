using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FindUpTo.Pos.Server.Endpoints;

public static class PrinterEndpoints
{
    public static void MapPrinterEndpoints(this WebApplication app)
    {
        app.MapGet("/api/printers/discover", async (CancellationToken ct) =>
        {
            var printers = new List<PrinterInfo>();
            var network = await DiscoverNetworkAsync(ct);
            printers.AddRange(network);
            return Results.Ok(printers.OrderByDescending(x => x.Connected).ThenByDescending(x => x.Kind == "Thermal80mm").ThenBy(x => x.Name));
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapPost("/api/printers/select", async (PrinterSelectRequest input, CancellationToken ct) =>
        {
            var printers = await DiscoverNetworkAsync(ct);
            var selected = printers.Where(x => x.Connected).Where(x => input.DocumentType.Equals("Receipt", StringComparison.OrdinalIgnoreCase) ? x.Kind == "Thermal80mm" : x.Kind == "A4").OrderByDescending(x => x.Kind == (input.DocumentType.Equals("Receipt", StringComparison.OrdinalIgnoreCase) ? "Thermal80mm" : "A4")).FirstOrDefault();
            return selected is null ? Results.NotFound(new { message = "No suitable connected printer was discovered." }) : Results.Ok(selected);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));
    }

    private static async Task<List<PrinterInfo>> DiscoverNetworkAsync(CancellationToken ct)
    {
        var result = new List<PrinterInfo>();
        foreach (var ip in LocalSubnetCandidates())
        {
            ct.ThrowIfCancellationRequested();
            using var client = new TcpClient();
            try
            {
                var task = client.ConnectAsync(ip, 9100, ct);
                await task.WaitAsync(TimeSpan.FromMilliseconds(120), ct);
                result.Add(new PrinterInfo($"Network printer {ip}", "Network", ip.ToString(), true, "Thermal80mm"));
            }
            catch { }
        }
        return result;
    }

    private static IEnumerable<IPAddress> LocalSubnetCandidates()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up))
        {
            foreach (var address in nic.GetIPProperties().UnicastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                var bytes = address.Address.GetAddressBytes();
                var mask = address.IPv4Mask.GetAddressBytes();
                if (mask.Length != 4) continue;
                for (var host = 1; host < 255; host++)
                {
                    var candidate = new byte[4];
                    for (var i = 0; i < 4; i++) candidate[i] = (byte)((bytes[i] & mask[i]) | (host & ~mask[i]));
                    var ip = new IPAddress(candidate);
                    if (!ip.Equals(address.Address)) yield return ip;
                }
            }
        }
    }

    private sealed record PrinterSelectRequest(string DocumentType);
    private sealed record PrinterInfo(string Name, string Connection, string Address, bool Connected, string Kind);
}
