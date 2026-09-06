using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class PrinterEndpoints
{
    public static void MapPrinterEndpoints(this WebApplication app)
    {
        app.MapGet("/api/printers/discover", async (CancellationToken ct) =>
        {
            var printers = await DiscoverNetworkAsync(ct);
            return Results.Ok(printers.OrderByDescending(x => x.Connected).ThenByDescending(x => x.Kind == "Thermal80mm").ThenBy(x => x.Name));
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapPost("/api/printers/select", async (PrinterSelectRequest input, CancellationToken ct) =>
        {
            var printers = await DiscoverNetworkAsync(ct);
            var wantReceipt = input.DocumentType.Equals("Receipt", StringComparison.OrdinalIgnoreCase);
            var selected = printers.Where(x => x.Connected && (wantReceipt ? x.Kind == "Thermal80mm" : x.Kind == "A4")).FirstOrDefault();
            return selected is null ? Results.NotFound(new { message = "No suitable connected printer was discovered." }) : Results.Ok(selected);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapPost("/api/printers/print-receipt/{id:int}", async (int id, CoreDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var order = await db.Orders.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (order is null) return Results.NotFound();
            var printers = await DiscoverNetworkAsync(ct);
            var printer = printers.FirstOrDefault(x => x.Connected && x.Kind == "Thermal80mm");
            if (printer is null) return Results.NotFound(new { message = "No connected 80mm network thermal printer found." });

            try
            {
                await PrintReceiptAsync(printer.Address, order, ct);
                await AuditEndpoints.WriteAsync(db, user, "Printed", "Receipt", id.ToString(), $"NetworkPrinter={printer.Address}");
                return Results.Ok(new { printed = true, printer });
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
            {
                return Results.Problem("The selected network printer could not be reached.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));
    }

    private static async Task PrintReceiptAsync(string address, Models.PosOrder order, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(address), 9100, ct);
        await using var stream = client.GetStream();
        var text = new StringBuilder();
        text.Append("\x1B\x40");
        text.Append("\x1B\x61\x01");
        text.Append("FINDUPTO POS\n");
        text.Append("ORDER #").Append(order.Id).Append("\n");
        text.Append(order.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append("\n");
        text.Append("\x1B\x61\x00");
        text.Append("--------------------------------\n");
        foreach (var item in order.Items)
        {
            text.Append(item.ProductName).Append("\n");
            text.Append(item.Quantity).Append(" x ").Append(item.UnitPrice.ToString("0.00")).Append("    ").Append(item.LineTotal.ToString("0.00")).Append("\n");
        }
        text.Append("--------------------------------\n");
        text.Append("Subtotal: ").Append(order.Subtotal.ToString("0.00")).Append("\n");
        text.Append("Tax:      ").Append(order.Tax.ToString("0.00")).Append("\n");
        text.Append("TOTAL:    ").Append(order.Total.ToString("0.00")).Append("\n\n");
        text.Append("Thank you!\n\n\n");
        text.Append("\x1D\x56\x00");
        var bytes = Encoding.ASCII.GetBytes(text.ToString());
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
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
                await client.ConnectAsync(ip, 9100, ct).WaitAsync(TimeSpan.FromMilliseconds(120), ct);
                result.Add(new PrinterInfo($"Network printer {ip}", "Network", ip.ToString(), true, "Thermal80mm"));
            }
            catch { }
        }
        return result;
    }

    private static IEnumerable<IPAddress> LocalSubnetCandidates()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up))
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

    private sealed record PrinterSelectRequest(string DocumentType);
    private sealed record PrinterInfo(string Name, string Connection, string Address, bool Connected, string Kind);
}
