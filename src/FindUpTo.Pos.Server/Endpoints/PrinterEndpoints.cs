using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
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
            if (string.IsNullOrWhiteSpace(input.DocumentType))
                return Results.BadRequest(new { message = "DocumentType is required." });

            var printers = await DiscoverNetworkAsync(ct);
            var wantReceipt = input.DocumentType.Equals("Receipt", StringComparison.OrdinalIgnoreCase);
            var selected = printers.Where(x => x.Connected && (wantReceipt ? x.Kind == "Thermal80mm" : x.Kind == "A4")).FirstOrDefault();
            return selected is null ? Results.NotFound(new { message = "No suitable connected printer was discovered." }) : Results.Ok(selected);
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));

        app.MapPost("/api/printers/print-receipt/{id:int}", async (int id, CoreDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var order = await db.Orders.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (order is null) return Results.NotFound();

            var settings = await db.BusinessSettings.AsNoTracking().SingleOrDefaultAsync(ct);
            var printers = await DiscoverNetworkAsync(ct);
            var printer = printers.FirstOrDefault(x => x.Connected && x.Kind == "Thermal80mm");
            if (printer is null) return Results.NotFound(new { message = "No connected 80mm network thermal printer found." });

            try
            {
                await PrintReceiptAsync(printer.Address, order, settings, ct);
                await AuditEndpoints.WriteAsync(db, user, "Printed", "Receipt", id.ToString(), $"NetworkPrinter={printer.Address}");
                return Results.Ok(new { printed = true, printer });
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
            {
                return Results.Problem("The selected network printer could not be reached.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization(p => p.RequireRole("Owner", "Manager", "Admin", "Counter"));
    }

    private static async Task PrintReceiptAsync(string address, PosOrder order, BusinessSetting? settings, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(address), 9100, ct);
        await using var stream = client.GetStream();

        var businessName = string.IsNullOrWhiteSpace(settings?.BusinessName) ? "POS" : settings.BusinessName.Trim();
        var currency = string.IsNullOrWhiteSpace(settings?.CurrencySymbol) ? "" : settings.CurrencySymbol.Trim() + " ";
        var text = new StringBuilder();
        text.Append("\x1B\x40");
        text.Append("\x1B\x61\x01");
        text.Append(businessName).Append("\n");
        if (!string.IsNullOrWhiteSpace(settings?.Phone)) text.Append(settings.Phone.Trim()).Append("\n");
        if (!string.IsNullOrWhiteSpace(settings?.Address)) text.Append(settings.Address.Trim()).Append("\n");
        text.Append("ORDER #").Append(order.Id).Append("\n");
        text.Append(order.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append("\n");
        text.Append("\x1B\x61\x00");
        text.Append("--------------------------------\n");
        foreach (var item in order.Items)
        {
            text.Append(item.ProductName).Append("\n");
            text.Append(item.Quantity).Append(" x ").Append(currency).Append(item.UnitPrice.ToString("0.00")).Append("    ").Append(currency).Append(item.LineTotal.ToString("0.00")).Append("\n");
        }
        text.Append("--------------------------------\n");
        text.Append("Subtotal: ").Append(currency).Append(order.Subtotal.ToString("0.00")).Append("\n");
        text.Append("Tax:      ").Append(currency).Append(order.Tax.ToString("0.00")).Append("\n");
        text.Append("TOTAL:    ").Append(currency).Append(order.Total.ToString("0.00")).Append("\n\n");
        text.Append("Thank you!\n\n\n");
        text.Append("\x1D\x56\x00");
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<List<PrinterInfo>> DiscoverNetworkAsync(CancellationToken ct)
    {
        var result = new List<PrinterInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configured in (Environment.GetEnvironmentVariable("POS_PRINTER_IPS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPAddress.TryParse(configured, out var address) || address.AddressFamily != AddressFamily.InterNetwork) continue;
            await ProbeAsync(address, result, seen, ct);
        }

        foreach (var ip in LocalSubnetCandidates())
        {
            ct.ThrowIfCancellationRequested();
            await ProbeAsync(ip, result, seen, ct);
        }

        return result;
    }

    private static async Task ProbeAsync(IPAddress ip, List<PrinterInfo> result, HashSet<string> seen, CancellationToken ct)
    {
        if (!seen.Add(ip.ToString())) return;
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(ip, 9100, ct).WaitAsync(TimeSpan.FromMilliseconds(250), ct);
            result.Add(new PrinterInfo($"Network raw printer {ip}", "Network TCP/9100", ip.ToString(), true, "Thermal80mm"));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (SocketException) { }
        catch (IOException) { }
    }

    private static IEnumerable<IPAddress> LocalSubnetCandidates()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up))
        foreach (var address in nic.GetIPProperties().UnicastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork && x.IPv4Mask is not null))
        {
            var bytes = address.Address.GetAddressBytes();
            var mask = address.IPv4Mask.GetAddressBytes();
            if (mask.Length != 4) continue;

            var network = new byte[4];
            var broadcast = new byte[4];
            for (var i = 0; i < 4; i++)
            {
                network[i] = (byte)(bytes[i] & mask[i]);
                broadcast[i] = (byte)(network[i] | ~mask[i]);
            }

            var networkValue = ToUInt32(network);
            var broadcastValue = ToUInt32(broadcast);
            var hostCount = broadcastValue > networkValue ? broadcastValue - networkValue - 1 : 0;
            if (hostCount == 0 || hostCount > 254) continue;

            for (var value = networkValue + 1; value < broadcastValue; value++)
            {
                var ip = FromUInt32(value);
                if (!ip.Equals(address.Address)) yield return ip;
            }
        }
    }

    private static uint ToUInt32(byte[] bytes) => ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    private static IPAddress FromUInt32(uint value) => new(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });

    private sealed record PrinterSelectRequest(string DocumentType);
    private sealed record PrinterInfo(string Name, string Connection, string Address, bool Connected, string Kind);
}
