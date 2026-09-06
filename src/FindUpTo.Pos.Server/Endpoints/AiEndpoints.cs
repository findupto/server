            if (provider == "openai" && string.IsNullOrWhiteSpace(input.ApiKey)) return Results.BadRequest("API key is required for a purchased OpenAI model.");
            if (provider is "ollama" or "lmstudio" or "llamacpp" && string.IsNullOrWhiteSpace(input.Model)) return Results.BadRequest("A local model name is required.");
            await providers.ConfigureAsync(provider, input.Model ?? "", input.ApiKey, input.BaseUrl, cancellationToken);
            return Results.Ok(new { success = true, message = "AI configuration saved. API keys are never returned by this endpoint." });
        }).RequireAuthorization(p => p.RequireRole("Owner"));
    }

    private static async Task<object> GetInventoryForecastAsync(CoreDbContext db, int horizon, CancellationToken ct)
    {
        var now = DateTime.UtcNow; var fromDate = now.AddDays(-Math.Max(30, horizon));
        var products = await db.Products.AsNoTracking().Where(x => x.Available).ToListAsync(ct);
        var inventory = await db.ProductInventories.AsNoTracking().ToDictionaryAsync(x => x.ProductId, ct);
        var sales = await (from item in db.OrderItems.AsNoTracking()
                           join order in db.Orders.AsNoTracking() on item.PosOrderId equals order.Id
                           where order.CreatedAtUtc >= fromDate && order.Status != "Cancelled"
                           group item by item.ProductId into g
                           select new { productId = g.Key, units = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.productId, x => x.units, ct);
        var items = products.Select(p =>
        {
            inventory.TryGetValue(p.Id, out var s); var units = sales.GetValueOrDefault(p.Id); var daily = units / (decimal)Math.Max(30, horizon); var forecast = Math.Ceiling(daily * horizon);
            var onHand = s?.QuantityOnHand ?? 0m; var reorder = s?.ReorderLevel ?? 0m;
            return new { productId = p.Id, productName = p.Name, quantityOnHand = onHand, reorderLevel = reorder, unitsSold = units, dailySalesRate = Math.Round(daily, 3), forecastUnits = forecast,
                daysRemaining = daily > 0 ? Math.Round(onHand / daily, 1) : (decimal?)null, recommendedOrderQuantity = Math.Max(0m, Math.Ceiling(Math.Max(reorder, forecast) - onHand)) };
        }).OrderByDescending(x => x.recommendedOrderQuantity).ToList();
        return new { generatedAtUtc = now, horizonDays = horizon, items };
    }

    private static async Task<object> GetReordersAsync(CoreDbContext db, CancellationToken ct) => new
    {
        generatedAtUtc = DateTime.UtcNow,
        recommendations = await db.ProductInventories.AsNoTracking().Where(x => x.TrackInventory && x.QuantityOnHand <= x.ReorderLevel)