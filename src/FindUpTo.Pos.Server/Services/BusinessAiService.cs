using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Claims;
using FindUpTo.Pos.Server.Data;
using FindUpTo.Pos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace FindUpTo.Pos.Server.Services;

public sealed class BusinessAiService(IHttpClientFactory httpClientFactory, CoreDbContext db, PromotionPricingService pricing, InventoryService inventory, AiProviderService providers)
{
    private const string DefaultModel = "gpt-5";
    private static readonly HashSet<string> SalesRoles = ["Owner", "Manager", "Admin", "Counter"];
    private static readonly HashSet<string> ManagementRoles = ["Owner", "Manager", "Admin"];

    public async Task<object> RunAsync(ClaimsPrincipal user, string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("AI command is required.");
        var provider = await providers.ResolveAsync(cancellationToken);
        if (provider.Provider == "none") throw new InvalidOperationException("No AI provider is available. FindUpTo automatically checks Ollama, LM Studio and llama.cpp locally; the Owner can also configure a purchased model in AI Configuration.");
        var username = user.Identity?.Name ?? "unknown";
        var role = user.FindFirstValue(ClaimTypes.Role) ?? "";
        var tools = BuildTools();
        var executed = new List<object>();
        if (provider.Local) return await RunLocalAsync(provider, user, message.Trim(), tools, executed, username, role, cancellationToken);
        var apiKey = await providers.GetApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("The selected paid AI provider requires an API key. Configure it from AI Configuration or use automatic local AI mode.");
        var input = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = message.Trim() } };
        var response = await CreateResponseAsync(provider, apiKey, input, tools, username, role, cancellationToken);
        for (var round = 0; round < 8; round++)
        {
            var functionCalls = response["output"]?.AsArray().Where(x => x?["type"]?.GetValue<string>() == "function_call").ToList() ?? [];
            if (functionCalls.Count == 0) return new { success = true, provider = provider.Provider, model = provider.Model, message = ExtractOutputText(response) ?? "Done.", role, username, actions = executed };
            var nextInput = new JsonArray();
            foreach (var outputItem in response["output"]?.AsArray() ?? []) nextInput.Add(outputItem?.DeepClone());
            foreach (var call in functionCalls)
            {
                var name = call?["name"]?.GetValue<string>() ?? ""; var callId = call?["call_id"]?.GetValue<string>() ?? ""; var rawArgs = call?["arguments"]?.GetValue<string>() ?? "{}"; object result;
                try { var args = JsonNode.Parse(rawArgs)?.AsObject() ?? []; result = await ExecuteToolAsync(name, args, user, username, role, cancellationToken); } catch (Exception ex) { result = new { success = false, error = ex.Message }; }
                executed.Add(new { tool = name, result }); nextInput.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = callId, ["output"] = JsonSerializer.Serialize(result) });
            }
            response = await CreateResponseAsync(provider, apiKey, nextInput, tools, username, role, cancellationToken);
        }
        throw new InvalidOperationException("AI reached its maximum action rounds without completing the request.");
    }

    private async Task<object> RunLocalAsync(AiProviderInfo provider, ClaimsPrincipal user, string message, JsonArray tools, List<object> executed, string username, string role, CancellationToken cancellationToken)
    {
        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = BuildInstructions(username, role) }, new JsonObject { ["role"] = "user", ["content"] = message } };
        var chatTools = new JsonArray();
        foreach (var tool in tools) chatTools.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = tool?["name"]?.DeepClone(), ["description"] = tool?["description"]?.DeepClone(), ["parameters"] = tool?["parameters"]?.DeepClone() } });
        var client = httpClientFactory.CreateClient("business-ai");
        for (var round = 0; round < 8; round++)
        {
            var body = new JsonObject { ["model"] = provider.Model, ["messages"] = messages, ["tools"] = chatTools, ["tool_choice"] = "auto", ["temperature"] = 0.1 };
            using var request = AiProviderService.CreateRequest(provider, "/v1/chat/completions"); request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, cancellationToken); var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Local AI provider error ({(int)response.StatusCode}): {content}");
            var json = JsonNode.Parse(content)?.AsObject() ?? throw new InvalidOperationException("Local AI provider returned an invalid response.");
            var messageNode = json["choices"]?.AsArray().FirstOrDefault()?["message"]?.AsObject() ?? throw new InvalidOperationException("Local AI provider returned no message.");
            var toolCalls = messageNode["tool_calls"]?.AsArray() ?? [];
            var assistant = new JsonObject { ["role"] = "assistant", ["content"] = messageNode["content"]?.DeepClone() ?? "" };
            if (toolCalls.Count > 0) assistant["tool_calls"] = toolCalls.DeepClone(); messages.Add(assistant);
            if (toolCalls.Count == 0) return new { success = true, provider = provider.Provider, model = provider.Model, message = messageNode["content"]?.GetValue<string>() ?? "Done.", role, username, actions = executed };
            foreach (var call in toolCalls)
            {
                var name = call?["function"]?["name"]?.GetValue<string>() ?? ""; var callId = call?["id"]?.GetValue<string>() ?? ""; var rawArgs = call?["function"]?["arguments"]?.GetValue<string>() ?? "{}"; object result;
                try { var args = JsonNode.Parse(rawArgs)?.AsObject() ?? []; result = await ExecuteToolAsync(name, args, user, username, role, cancellationToken); } catch (Exception ex) { result = new { success = false, error = ex.Message }; }
                executed.Add(new { tool = name, result }); messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = callId, ["content"] = JsonSerializer.Serialize(result) });
            }
        }
        throw new InvalidOperationException("Local AI reached its maximum action rounds without completing the request.");
    }

    private async Task<JsonObject> CreateResponseAsync(AiProviderInfo provider, string apiKey, JsonArray input, JsonArray tools, string username, string role, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["model"] = provider.Model, ["store"] = false, ["instructions"] = BuildInstructions(username, role), ["input"] = input, ["tools"] = tools, ["tool_choice"] = "auto", ["max_output_tokens"] = 1200 };
        var client = httpClientFactory.CreateClient("business-ai"); using var request = AiProviderService.CreateRequest(provider, "/v1/responses"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey); request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken); var content = await response.Content.ReadAsStringAsync(cancellationToken); if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"AI provider error ({(int)response.StatusCode}): {content}"); return JsonNode.Parse(content)?.AsObject() ?? throw new InvalidOperationException("AI provider returned an invalid response.");
    }

    private static string BuildInstructions(string username, string role) => $"You are FindUpTo POS Business Operator, an action-oriented restaurant/retail POS assistant. Current authenticated user: {username}; role: {role}. You have direct access to live POS business tools. Use tools instead of guessing database values. For financial or inventory mutations, act only when the user clearly requested the mutation; never invent products, quantities, prices, supplier names, payment amounts, or customer identity. Respect tool authorization errors. Never bypass role restrictions. For a sale, verify products and stock through tools and use the sale tool; do not merely describe how to sell. For printing, create or retrieve the requested receipt/report and return the printable URL. Keep responses concise and operational. Use business data returned by tools. If required information is missing, ask one focused question instead of making assumptions.";

    private async Task<object> ExecuteToolAsync(string name, JsonObject args, ClaimsPrincipal user, string username, string role, CancellationToken cancellationToken) => name switch
    {
        "search_products" => await SearchProductsAsync(args), "inventory_summary" => await InventorySummaryAsync(args), "low_stock" => await LowStockAsync(), "sales_report" => await SalesReportAsync(args), "create_sale" => await CreateSaleAsync(args, user, username, role, cancellationToken), "add_stock" => await AdjustStockAsync(args, user, username, role, false), "set_stock" => await AdjustStockAsync(args, user, username, role, true), "update_product" => await UpdateProductAsync(args, user, username, role), "create_purchase" => await CreatePurchaseAsync(args, user, username, role), "receive_purchase" => await ReceivePurchaseAsync(args, user, username, role), "print_sale" => PrintSale(args, user), _ => throw new InvalidOperationException($"Unknown AI tool: {name}")
    };

    private async Task<object> SearchProductsAsync(JsonObject args)
    {
        var query = args["query"]?.GetValue<string>()?.Trim() ?? ""; var products = await db.Products.AsNoTracking().Where(x => x.Available && (query == "" || x.Name.Contains(query) || x.Barcode.Contains(query))).OrderBy(x => x.Name).Take(30).ToListAsync(); var ids = products.Select(x => x.Id).ToList(); var stock = await db.ProductInventories.AsNoTracking().Where(x => ids.Contains(x.ProductId)).ToDictionaryAsync(x => x.ProductId); return products.Select(p => new { p.Id, p.Name, p.Barcode, p.Price, stock = stock.GetValueOrDefault(p.Id)?.QuantityOnHand ?? 0m, averageCost = stock.GetValueOrDefault(p.Id)?.AverageCost ?? 0m, trackInventory = stock.GetValueOrDefault(p.Id)?.TrackInventory ?? false }).ToList();
    }
    private async Task<object> InventorySummaryAsync(JsonObject args)
    {
        var query = args["product_name"]?.GetValue<string>()?.Trim() ?? ""; return await db.Products.AsNoTracking().Join(db.ProductInventories.AsNoTracking(), p => p.Id, i => i.ProductId, (p, i) => new { p.Id, p.Name, p.Price, i.QuantityOnHand, i.ReorderLevel, i.AverageCost, i.TrackInventory }).Where(x => query == "" || x.Name.Contains(query)).OrderBy(x => x.Name).Take(100).ToListAsync();
    }
    private async Task<object> LowStockAsync() => await db.Products.AsNoTracking().Join(db.ProductInventories.AsNoTracking().Where(i => i.TrackInventory && i.QuantityOnHand <= i.ReorderLevel), p => p.Id, i => i.ProductId, (p, i) => new { p.Id, p.Name, p.Barcode, p.Price, i.QuantityOnHand, i.ReorderLevel, i.AverageCost }).OrderBy(x => x.QuantityOnHand).ToListAsync();
    private async Task<object> SalesReportAsync(JsonObject args)
    {
        var days = Math.Clamp(args["days"]?.GetValue<int>() ?? 1, 1, 365); var since = DateTime.UtcNow.AddDays(-days); var orders = await db.Orders.AsNoTracking().Where(x => x.CreatedAtUtc >= since && x.Status != "Cancelled").Include(x => x.Items).ToListAsync(); var payments = await db.Payments.AsNoTracking().Where(x => x.CreatedAtUtc >= since && x.Status == "Paid").ToListAsync(); return new { days, orderCount = orders.Count, grossSales = Math.Round(orders.Sum(x => x.Total), 2), subtotal = Math.Round(orders.Sum(x => x.Subtotal), 2), tax = Math.Round(orders.Sum(x => x.Tax), 2), discount = Math.Round(orders.Sum(x => x.Discount), 2), paid = Math.Round(payments.Sum(x => x.AmountPaid - x.ChangeAmount), 2), paymentMix = payments.GroupBy(x => x.Method).Select(g => new { method = g.Key, amount = Math.Round(g.Sum(x => x.AmountPaid - x.ChangeAmount), 2) }).OrderByDescending(x => x.amount), topProducts = orders.SelectMany(x => x.Items).GroupBy(x => x.ProductName).Select(g => new { product = g.Key, quantity = g.Sum(x => x.Quantity), sales = Math.Round(g.Sum(x => x.LineTotal), 2) }).OrderByDescending(x => x.sales).Take(10) };
    }

    private async Task<object> CreateSaleAsync(JsonObject args, ClaimsPrincipal user, string username, string role, CancellationToken cancellationToken)
    {
        EnsureRole(role, SalesRoles, "sales"); var items = args["items"]?.AsArray() ?? throw new InvalidOperationException("Sale items are required."); if (items.Count == 0 || items.Count > 100) throw new InvalidOperationException("Sale must contain 1-100 items."); var orderType = args["order_type"]?.GetValue<string>()?.Trim() ?? "Counter"; if (!new[] { "Counter", "Dine In", "Pickup", "Delivery" }.Contains(orderType, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid order type.");
        var ids = new List<(int id, int qty, string notes)>(); foreach (var item in items) { var productName = item?["product_name"]?.GetValue<string>()?.Trim() ?? ""; var product = await db.Products.AsNoTracking().Where(x => x.Available && x.Name == productName).SingleOrDefaultAsync(cancellationToken) ?? await db.Products.AsNoTracking().Where(x => x.Available && x.Name.Contains(productName)).OrderBy(x => x.Name).FirstOrDefaultAsync(cancellationToken); if (product is null) throw new InvalidOperationException($"Product not found: {productName}"); var qty = item?["quantity"]?.GetValue<int>() ?? 0; if (qty <= 0 || qty > 10000) throw new InvalidOperationException($"Invalid quantity for {product.Name}."); ids.Add((product.Id, qty, item?["notes"]?.GetValue<string>()?.Trim() ?? "")); }
        var customerId = args["customer_id"]?.GetValue<int?>(); var tableId = args["table_id"]?.GetValue<int?>(); var products = await db.Products.AsNoTracking().Where(x => ids.Select(i => i.id).Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken); var costs = await db.ProductInventories.AsNoTracking().Where(x => products.Keys.Contains(x.ProductId)).ToDictionaryAsync(x => x.ProductId, x => x.AverageCost, cancellationToken); var order = new PosOrder { CustomerId = customerId, TableId = tableId, CreatedByUsername = username, OrderType = new[] { "Counter", "Dine In", "Pickup", "Delivery" }.First(x => x.Equals(orderType, StringComparison.OrdinalIgnoreCase)), Notes = args["notes"]?.GetValue<string>()?.Trim() ?? "" };
        foreach (var (id, qty, notes) in ids) { var p = products[id]; order.Items.Add(new OrderItem { ProductId = p.Id, ProductName = p.Name, UnitPrice = Math.Round(p.Price, 2), UnitCost = costs.GetValueOrDefault(p.Id), Quantity = qty, Notes = notes, LineTotal = Math.Round(p.Price * qty, 2) }); } await pricing.ApplyAsync(order, products);
        var method = args["payment_method"]?.GetValue<string>()?.Trim(); var normalized = string.IsNullOrWhiteSpace(method) ? "" : new[] { "Cash", "Card", "Online" }.FirstOrDefault(x => x.Equals(method, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Payment method must be Cash, Card, or Online."); var tendered = Math.Round(args["amount_tendered"]?.GetValue<decimal>() ?? order.Total, 2); if (!string.IsNullOrWhiteSpace(normalized) && tendered <= 0) throw new InvalidOperationException("Amount tendered must be greater than zero."); if (normalized is "Card" or "Online" && tendered != order.Total) throw new InvalidOperationException("Card/Online tender must equal the order total."); if (normalized is "Card" or "Online" && string.IsNullOrWhiteSpace(args["payment_reference"]?.GetValue<string>())) throw new InvalidOperationException("Payment reference is required for Card and Online.");
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null; db.Orders.Add(order); await db.SaveChangesAsync(cancellationToken); var stock = await inventory.DeductForSaleAsync(order); if (!stock.Success) { if (tx is not null) await tx.RollbackAsync(cancellationToken); throw new InvalidOperationException($"Insufficient stock for {stock.ProductName}: required {stock.Required}, available {stock.Available}."); } if (!string.IsNullOrWhiteSpace(normalized)) { var change = normalized == "Cash" ? Math.Max(0m, tendered - order.Total) : 0m; db.Payments.Add(new Payment { PosOrderId = order.Id, AmountTendered = tendered, AmountPaid = order.Total, ChangeAmount = change, Method = normalized, Reference = args["payment_reference"]?.GetValue<string>()?.Trim() ?? "", CollectedByUsername = username }); } await db.SaveChangesAsync(cancellationToken); if (tx is not null) await tx.CommitAsync(cancellationToken); await AuditEndpoints.WriteAsync(db, user, "CreatedByAI", "Order", order.Id.ToString(), $"AI sale by {username}");
        return new { orderId = order.Id, status = order.Status, subtotal = order.Subtotal, tax = order.Tax, discount = order.Discount, total = order.Total, payment = string.IsNullOrWhiteSpace(normalized) ? "Unpaid" : normalized, receiptUrl = $"/api/orders/{order.Id}/receipt" };
    }

    private async Task<object> AdjustStockAsync(JsonObject args, ClaimsPrincipal user, string username, string role, bool absolute)
    {
        EnsureRole(role, ManagementRoles, "inventory changes"); var name = args["product_name"]?.GetValue<string>()?.Trim() ?? ""; var product = await db.Products.SingleOrDefaultAsync(x => x.Name == name) ?? await db.Products.Where(x => x.Name.Contains(name)).OrderBy(x => x.Name).FirstOrDefaultAsync(); if (product is null) throw new InvalidOperationException($"Product not found: {name}"); var inv = await db.ProductInventories.SingleOrDefaultAsync(x => x.ProductId == product.Id) ?? new ProductInventory { ProductId = product.Id, TrackInventory = true }; var old = inv.QuantityOnHand; var next = absolute ? args["quantity_on_hand"]?.GetValue<decimal>() ?? throw new InvalidOperationException("quantity_on_hand is required") : old + (args["quantity_change"]?.GetValue<decimal>() ?? 0m); if (next < 0) throw new InvalidOperationException("Stock cannot become negative."); if (inv.Id == 0) db.ProductInventories.Add(inv); inv.QuantityOnHand = next; inv.ReorderLevel = args["reorder_level"]?.GetValue<decimal>() ?? inv.ReorderLevel; inv.TrackInventory = true; inv.UpdatedAtUtc = DateTime.UtcNow; if (args["unit_cost"] is not null) inv.AverageCost = args["unit_cost"]!.GetValue<decimal>(); db.StockMovements.Add(new StockMovement { ProductId = product.Id, QuantityChange = next - old, BalanceAfter = next, UnitCost = inv.AverageCost, Type = absolute ? "AI Set" : "AI Adjustment", Reason = args["reason"]?.GetValue<string>()?.Trim() ?? "AI operation", Username = username }); await db.SaveChangesAsync(); await AuditEndpoints.WriteAsync(db, user, absolute ? "AISetStock" : "AIAdjustStock", "Product", product.Id.ToString(), $"Old={old};New={next}"); return new { productId = product.Id, product = product.Name, previous = old, quantityOnHand = next, reorderLevel = inv.ReorderLevel, averageCost = inv.AverageCost };
    }
    private async Task<object> UpdateProductAsync(JsonObject args, ClaimsPrincipal user, string username, string role)
    {
        EnsureRole(role, ManagementRoles, "product management"); var name = args["product_name"]?.GetValue<string>()?.Trim() ?? ""; var product = await db.Products.SingleOrDefaultAsync(x => x.Name == name) ?? await db.Products.Where(x => x.Name.Contains(name)).OrderBy(x => x.Name).FirstOrDefaultAsync(); if (product is null) throw new InvalidOperationException($"Product not found: {name}"); if (args["price"] is not null) { var price = args["price"]!.GetValue<decimal>(); if (price < 0) throw new InvalidOperationException("Price cannot be negative."); product.Price = Math.Round(price, 2); } if (args["available"] is not null) product.Available = args["available"]!.GetValue<bool>(); if (args["barcode"] is not null) product.Barcode = args["barcode"]!.GetValue<string>()?.Trim() ?? ""; product.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(); await AuditEndpoints.WriteAsync(db, user, "AIUpdated", "Product", product.Id.ToString(), $"Updated by {username}"); return new { product.Id, product.Name, product.Price, product.Available, product.Barcode };
    }
    private async Task<object> CreatePurchaseAsync(JsonObject args, ClaimsPrincipal user, string username, string role)
    {
        EnsureRole(role, ManagementRoles, "purchasing"); var supplierName = args["supplier_name"]?.GetValue<string>()?.Trim() ?? ""; if (string.IsNullOrWhiteSpace(supplierName)) throw new InvalidOperationException("supplier_name is required."); var supplier = await db.Suppliers.SingleOrDefaultAsync(x => x.Name == supplierName && x.Active) ?? await db.Suppliers.Where(x => x.Active && x.Name.Contains(supplierName)).OrderBy(x => x.Name).FirstOrDefaultAsync(); if (supplier is null) throw new InvalidOperationException($"Supplier not found: {supplierName}"); var order = new PurchaseOrder { SupplierId = supplier.Id, CreatedByUsername = username, Notes = args["notes"]?.GetValue<string>()?.Trim() ?? "", Status = "Draft" }; foreach (var item in args["items"]?.AsArray() ?? throw new InvalidOperationException("Purchase items are required.")) { var name = item?["product_name"]?.GetValue<string>()?.Trim() ?? ""; var product = await db.Products.SingleOrDefaultAsync(x => x.Name == name) ?? await db.Products.Where(x => x.Name.Contains(name)).OrderBy(x => x.Name).FirstOrDefaultAsync(); if (product is null) throw new InvalidOperationException($"Product not found: {name}"); var qty = item?["quantity"]?.GetValue<decimal>() ?? 0m; if (qty <= 0) throw new InvalidOperationException("Purchase quantity must be greater than zero."); order.Items.Add(new PurchaseOrderItem { ProductId = product.Id, ProductName = product.Name, QuantityOrdered = qty, UnitCost = item?["unit_cost"]?.GetValue<decimal>() ?? 0m }); } db.PurchaseOrders.Add(order); await db.SaveChangesAsync(); await AuditEndpoints.WriteAsync(db, user, "AICreated", "PurchaseOrder", order.Id.ToString(), $"Supplier={supplier.Name}"); return new { purchaseOrderId = order.Id, supplier = supplier.Name, status = order.Status, items = order.Items.Select(x => new { x.ProductName, x.QuantityOrdered, x.UnitCost }) };
    }
    private async Task<object> ReceivePurchaseAsync(JsonObject args, ClaimsPrincipal user, string username, string role)
    {
        EnsureRole(role, ManagementRoles, "purchase receiving"); var id = args["purchase_order_id"]?.GetValue<int>() ?? 0; var order = await db.PurchaseOrders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id); if (order is null) throw new InvalidOperationException("Purchase order not found."); foreach (var item in args["items"]?.AsArray() ?? []) { var productId = item?["product_id"]?.GetValue<int>() ?? 0; var qty = item?["quantity"]?.GetValue<decimal>() ?? 0m; var line = order.Items.SingleOrDefault(x => x.ProductId == productId) ?? throw new InvalidOperationException($"Product {productId} is not on purchase order {id}."); var remaining = line.QuantityOrdered - line.QuantityReceived; if (qty <= 0 || qty > remaining) throw new InvalidOperationException($"Receive quantity must be between 0 and {remaining} for {line.ProductName}."); var inv = await db.ProductInventories.SingleOrDefaultAsync(x => x.ProductId == productId) ?? new ProductInventory { ProductId = productId, TrackInventory = true }; if (inv.Id == 0) db.ProductInventories.Add(inv); var oldQty = inv.QuantityOnHand; var newQty = oldQty + qty; var cost = line.UnitCost; inv.AverageCost = newQty > 0 ? Math.Round(((oldQty * inv.AverageCost) + (qty * cost)) / newQty, 4) : cost; inv.QuantityOnHand = newQty; inv.TrackInventory = true; inv.UpdatedAtUtc = DateTime.UtcNow; line.QuantityReceived += qty; db.StockMovements.Add(new StockMovement { ProductId = productId, QuantityChange = qty, BalanceAfter = newQty, UnitCost = cost, Type = "Purchase", Reason = $"AI receive PO #{id}", Username = username }); } order.Status = order.Items.All(x => x.QuantityReceived >= x.QuantityOrdered) ? "Received" : "PartiallyReceived"; order.UpdatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(); await AuditEndpoints.WriteAsync(db, user, "AIReceived", "PurchaseOrder", order.Id.ToString(), $"Status={order.Status}"); return new { purchaseOrderId = order.Id, status = order.Status, items = order.Items.Select(x => new { x.ProductName, x.QuantityOrdered, x.QuantityReceived, x.UnitCost }) };
    }
    private object PrintSale(JsonObject args, ClaimsPrincipal user)
    {
        EnsureRole(user.FindFirstValue(ClaimTypes.Role) ?? "", SalesRoles, "receipt printing"); var orderId = args["order_id"]?.GetValue<int>() ?? 0; if (orderId <= 0) throw new InvalidOperationException("order_id is required."); return new { orderId, printableUrl = $"/api/orders/{orderId}/receipt", action = "Open this URL in the POS browser to print the receipt." };
    }
    private static void EnsureRole(string role, IEnumerable<string> allowed, string operation) { if (!allowed.Contains(role, StringComparer.OrdinalIgnoreCase)) throw new UnauthorizedAccessException($"Role '{role}' is not allowed to perform {operation}."); }
    private static string? ExtractOutputText(JsonObject response)
    {
        if (response["output_text"] is JsonValue direct && direct.TryGetValue<string>(out var value) && !string.IsNullOrWhiteSpace(value)) return value; foreach (var item in response["output"]?.AsArray() ?? []) foreach (var content in item?["content"]?.AsArray() ?? []) if (content?["type"]?.GetValue<string>() == "output_text") return content?["text"]?.GetValue<string>(); return null;
    }
    private static JsonArray BuildTools() => new()
    {
        Fn("search_products", "Find live available products by name or barcode.", new { type = "object", properties = new { query = new { type = "string" } }, required = new[] { "query" }, additionalProperties = false }),
        Fn("inventory_summary", "Read current inventory, cost, reorder level and tracking state.", new { type = "object", properties = new { product_name = new { type = "string" } }, required = Array.Empty<string>(), additionalProperties = false }),
        Fn("low_stock", "List products at or below their reorder level.", new { type = "object", properties = new { }, required = Array.Empty<string>(), additionalProperties = false }),
        Fn("sales_report", "Read sales and payment performance for the requested number of days.", new { type = "object", properties = new { days = new { type = "integer", minimum = 1, maximum = 365 } }, required = new[] { "days" }, additionalProperties = false }),
        Fn("create_sale", "Create a real POS sale, deduct tracked inventory and optionally collect payment. Use only for explicit sale requests.", new { type = "object", properties = new { items = new { type = "array", items = new { type = "object", properties = new { product_name = new { type = "string" }, quantity = new { type = "integer", minimum = 1 }, notes = new { type = "string" } }, required = new[] { "product_name", "quantity" }, additionalProperties = false } }, order_type = new { type = "string", @enum = new[] { "Counter", "Dine In", "Pickup", "Delivery" } }, customer_id = new { type = new[] { "integer", "null" } }, table_id = new { type = new[] { "integer", "null" } }, payment_method = new { type = new[] { "string", "null" }, @enum = new[] { "Cash", "Card", "Online", null } }, amount_tendered = new { type = new[] { "number", "null" } }, payment_reference = new { type = "string" }, notes = new { type = "string" } }, required = new[] { "items", "order_type" }, additionalProperties = false }),
        Fn("add_stock", "Increase or decrease tracked stock by a delta. Management roles only.", new { type = "object", properties = new { product_name = new { type = "string" }, quantity_change = new { type = "number" }, unit_cost = new { type = new[] { "number", "null" } }, reason = new { type = "string" } }, required = new[] { "product_name", "quantity_change" }, additionalProperties = false }),
        Fn("set_stock", "Set an exact stock quantity and optionally reorder level. Management roles only.", new { type = "object", properties = new { product_name = new { type = "string" }, quantity_on_hand = new { type = "number", minimum = 0 }, reorder_level = new { type = "number", minimum = 0 }, unit_cost = new { type = new[] { "number", "null" } }, reason = new { type = "string" } }, required = new[] { "product_name", "quantity_on_hand" }, additionalProperties = false }),
        Fn("update_product", "Update a product price, availability or barcode. Management roles only.", new { type = "object", properties = new { product_name = new { type = "string" }, price = new { type = new[] { "number", "null" } }, available = new { type = new[] { "boolean", "null" } }, barcode = new { type = new[] { "string", "null" } } }, required = new[] { "product_name" }, additionalProperties = false }),
        Fn("create_purchase", "Create a real purchase order for an existing supplier. Management roles only.", new { type = "object", properties = new { supplier_name = new { type = "string" }, items = new { type = "array", items = new { type = "object", properties = new { product_name = new { type = "string" }, quantity = new { type = "number", minimum = 0.0001 }, unit_cost = new { type = "number", minimum = 0 } }, required = new[] { "product_name", "quantity" }, additionalProperties = false } }, notes = new { type = "string" } }, required = new[] { "supplier_name", "items" }, additionalProperties = false }),
        Fn("receive_purchase", "Receive quantities from an existing purchase order and add them to inventory.", new { type = "object", properties = new { purchase_order_id = new { type = "integer" }, items = new { type = "array", items = new { type = "object", properties = new { product_id = new { type = "integer" }, quantity = new { type = "number", minimum = 0.0001 } }, required = new[] { "product_id", "quantity" }, additionalProperties = false } } }, required = new[] { "purchase_order_id", "items" }, additionalProperties = false }),
        Fn("print_sale", "Return the printable receipt URL for an existing sale.", new { type = "object", properties = new { order_id = new { type = "integer" } }, required = new[] { "order_id" }, additionalProperties = false })
    };
    private static JsonObject Fn(string name, string description, object parameters) => new() { ["type"] = "function", ["name"] = name, ["description"] = description, ["parameters"] = JsonSerializer.SerializeToNode(parameters), ["strict"] = false };
}
