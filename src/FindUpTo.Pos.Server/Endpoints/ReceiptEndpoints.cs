using System.Security.Claims;
using System.Text;
using FindUpTo.Pos.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Endpoints;

public static class ReceiptEndpoints
{
    public static void MapReceiptEndpoints(this WebApplication app)
    {
        app.MapGet("/api/orders/{id:int}/receipt", async (int id, ClaimsPrincipal user, CoreDbContext db) =>
        {
            var order = await db.Orders.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id);
            if (order is null) return Results.NotFound();

            var business = await db.BusinessSettings.AsNoTracking().SingleAsync();
            var payments = await db.Payments.AsNoTracking().Where(x => x.PosOrderId == id && x.Status == "Paid").OrderBy(x => x.CreatedAtUtc).ToListAsync();
            var table = order.TableId.HasValue ? await db.Tables.AsNoTracking().Where(x => x.Id == order.TableId.Value).Select(x => x.Name).SingleOrDefaultAsync() : null;

            var html = BuildHtml(business.BusinessName, business.Phone, business.Address, business.CurrencySymbol, order, payments, table);
            await AuditEndpoints.WriteAsync(db, user, "Printed", "Receipt", id.ToString(), $"Order={id}");
            return Results.Content(html, "text/html; charset=utf-8");
        }).RequireAuthorization();
    }

    private static string BuildHtml(string businessName, string phone, string address, string currency, Models.PosOrder order, List<Models.Payment> payments, string? table)
    {
        static string E(string value) => System.Net.WebUtility.HtmlEncode(value);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>Receipt #").Append(order.Id).Append("</title>");
        sb.Append("<style>body{font-family:Arial,sans-serif;width:300px;margin:0 auto;padding:12px;font-size:13px}h2{text-align:center;margin:4px 0}p{text-align:center;margin:3px 0}.line{border-top:1px dashed #000;margin:8px 0}.row{display:flex;justify-content:space-between;gap:8px}.item{margin:6px 0}.muted{font-size:11px}.total{font-size:16px;font-weight:bold}@media print{body{width:72mm;padding:2mm}}</style></head><body>");
        sb.Append("<h2>").Append(E(businessName)).Append("</h2><p>").Append(E(phone)).Append("</p><p>").Append(E(address)).Append("</p>");
        sb.Append("<div class='line'></div><div class='row'><span>Order #").Append(order.Id).Append("</span><span>").Append(order.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append("</span></div>");
        sb.Append("<div class='row'><span>").Append(E(order.OrderType)).Append("</span><span>").Append(E(order.Status)).Append("</span></div>");
        if (!string.IsNullOrWhiteSpace(table)) sb.Append("<div class='row'><span>Table</span><span>").Append(E(table)).Append("</span></div>");
        sb.Append("<div class='line'></div>");
        foreach (var item in order.Items)
        {
            sb.Append("<div class='item'><div>").Append(E(item.ProductName)).Append("</div><div class='row'><span>").Append(item.Quantity).Append(" x ").Append(currency).Append(item.UnitPrice.ToString("0.00")).Append("</span><span>").Append(currency).Append(item.LineTotal.ToString("0.00")).Append("</span></div>");
            if (!string.IsNullOrWhiteSpace(item.Notes)) sb.Append("<div class='muted'>").Append(E(item.Notes)).Append("</div>");
            sb.Append("</div>");
        }
        sb.Append("<div class='line'></div><div class='row'><span>Subtotal</span><span>").Append(currency).Append(order.Subtotal.ToString("0.00")).Append("</span></div>");
        sb.Append("<div class='row'><span>Tax</span><span>").Append(currency).Append(order.Tax.ToString("0.00")).Append("</span></div>");
        sb.Append("<div class='row total'><span>Total</span><span>").Append(currency).Append(order.Total.ToString("0.00")).Append("</span></div>");
        foreach (var payment in payments) sb.Append("<div class='row'><span>").Append(E(payment.Method)).Append(" Paid</span><span>").Append(currency).Append(payment.AmountPaid.ToString("0.00")).Append("</span></div>");
        if (!string.IsNullOrWhiteSpace(order.Notes)) sb.Append("<p class='muted'>").Append(E(order.Notes)).Append("</p>");
        sb.Append("<div class='line'></div><p>Thank you!</p><script>window.addEventListener('load',()=>window.print());</script></body></html>");
        return sb.ToString();
    }
}
