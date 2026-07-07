using FashionDataAnalysisPlatform.Data;
using FashionDataAnalysisPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace FashionDataAnalysisPlatform.Services
{
    // Generates live platform notifications from existing DB data — no separate table.
    // Each notification has a deterministic ID so the client can track read/unread state.
    public class NotificationService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<NotificationService> _logger;

        // Seasons whose end dates are past as of the current thesis date (2026-06-16).
        // AW24 ended 2025-03-01, SS25 ended 2025-09-01, AW25 ended 2026-03-01.
        private static readonly string[] ExpiredSeasons = ["AW24", "SS25", "AW25"];

        private static readonly Dictionary<string, int> SeverityOrder =
            new() { ["Critical"] = 0, ["Warning"] = 1, ["Info"] = 2, ["Success"] = 3 };

        public NotificationService(AppDbContext context, ILogger<NotificationService> logger)
        {
            _context = context;
            _logger  = logger;
        }

        public async Task<List<AppNotification>> GetNotificationsAsync()
        {
            var notifications = new List<AppNotification>();

            await AddInventoryNotifications(notifications);
            await AddSmartInsightsNotifications(notifications);
            await AddForecastingNotifications(notifications);
            await AddSustainabilityNotifications(notifications);

            // Sort Critical → Warning → Info so the panel always leads with the most urgent item.
            notifications.Sort((a, b) =>
                SeverityOrder.GetValueOrDefault(a.Severity, 9)
                    .CompareTo(SeverityOrder.GetValueOrDefault(b.Severity, 9)));

            _logger.LogInformation("[Notifications] Total generated: {Count}", notifications.Count);

            // Fallback: panel is never empty during a live demo.
            if (notifications.Count == 0)
            {
                notifications.Add(new AppNotification
                {
                    Id        = "system-health-ok",
                    Title     = "System Health Check Passed",
                    Message   = "All inventory, sales, and forecast indicators are within normal parameters.",
                    Severity  = "Info",
                    Module    = "Dashboard",
                    ModuleUrl = "/Dashboard",
                    TimeLabel = "Now"
                });
            }

            return notifications;
        }

        // SOURCE 1 & 2: Inventory
        private async Task AddInventoryNotifications(List<AppNotification> list)
        {
            // 1a. Out-of-stock products — shown unconditionally (no recent-sales filter).
            // The Python generator stops ordering expired-season products (AW24/SS25/AW25)
            // after their season end date, so OOS expired-season items have zero 30-day sales.
            // Removing the recency filter ensures they still surface as alerts.
            var oosProductIds = await _context.Inventories
                .AsNoTracking()
                .Where(i => i.CurrentStock == 0)
                .Select(i => i.ProductId)
                .Distinct()
                .ToListAsync();

            _logger.LogInformation("[Notifications] Source 1a — OOS products: {Count}", oosProductIds.Count);

            if (oosProductIds.Count > 0)
            {
                // Top 3 by ALL-TIME revenue so expired-season products still appear.
                var revenueByOos = await _context.Sales
                    .AsNoTracking()
                    .Where(s => oosProductIds.Contains(s.ProductId))
                    .GroupBy(s => s.ProductId)
                    .Select(g => new
                    {
                        ProductId = g.Key,
                        Revenue   = g.Sum(x => x.Revenue),
                        LastSale  = g.Max(x => x.SaleDate)
                    })
                    .OrderByDescending(x => x.Revenue)
                    .Take(3)
                    .ToListAsync();

                if (revenueByOos.Count > 0)
                {
                    var topPids = revenueByOos.Select(x => x.ProductId).ToList();
                    var nameMap = await _context.Products
                        .AsNoTracking()
                        .Where(p => topPids.Contains(p.ProductId))
                        .ToDictionaryAsync(p => p.ProductId, p => p.ProductName);

                    var cutoff90 = DateTime.Today.AddDays(-90);
                    foreach (var item in revenueByOos)
                    {
                        if (!nameMap.TryGetValue(item.ProductId, out var name)) continue;
                        bool recentSales = item.LastSale >= cutoff90;
                        list.Add(new AppNotification
                        {
                            Id        = $"oos-{item.ProductId}",
                            Title     = $"{name} — Out of Stock",
                            Message   = recentSales
                                ? $"€{item.Revenue:N0} all-time revenue. Immediate replenishment required."
                                : $"€{item.Revenue:N0} all-time revenue. Seasonal clearance complete — review write-off.",
                            Severity  = recentSales ? "Critical" : "Warning",
                            Module    = "Inventory",
                            ModuleUrl = "/Inventories",
                            TimeLabel = "Now"
                        });
                    }
                }

                // Always show the aggregate count so the Inventory module has a visible alert.
                list.Add(new AppNotification
                {
                    Id        = "oos-aggregate",
                    Title     = $"{oosProductIds.Count} SKUs Currently Out of Stock",
                    Message   = "Zero-inventory products detected across all stores. Review replenishment and clearance status.",
                    Severity  = "Warning",
                    Module    = "Inventory",
                    ModuleUrl = "/Inventories",
                    TimeLabel = "Now"
                });
            }

            // 1b. Low-stock aggregate (above zero but at or below minimum threshold)
            var lowStockCount = await _context.Inventories
                .AsNoTracking()
                .Where(i => i.CurrentStock > 0 && i.CurrentStock <= i.MinimumStockThreshold)
                .CountAsync();

            _logger.LogInformation("[Notifications] Source 1b — Low-stock products: {Count}", lowStockCount);

            if (lowStockCount > 0)
            {
                list.Add(new AppNotification
                {
                    Id        = "low-stock-aggregate",
                    Title     = $"{lowStockCount} Products Approaching Minimum Threshold",
                    Message   = "These SKUs risk stockout before the next replenishment cycle.",
                    Severity  = lowStockCount >= 8 ? "Warning" : "Info",
                    Module    = "Inventory",
                    ModuleUrl = "/Inventories",
                    TimeLabel = "Today"
                });
            }
        }

        // SOURCE 3: Smart Insights — revenue trend anomaly
        private async Task AddSmartInsightsNotifications(List<AppNotification> list)
        {
            // Compare revenue per category: last 30 days vs the prior 30 days.
            // 14-day windows were too narrow and rarely diverged across the same month.
            // Threshold lowered from 20% to 15% to match expected seasonal variance.
            var today  = DateTime.Today;
            var curr30 = today.AddDays(-30);
            var prev30 = today.AddDays(-60);

            var recentSales = await _context.Sales
                .AsNoTracking()
                .Where(s => s.SaleDate >= prev30 && s.ProductId != 0)
                .Select(s => new { s.ProductId, s.SaleDate, s.Revenue })
                .ToListAsync();

            if (!recentSales.Any())
            {
                _logger.LogInformation("[Notifications] Source 3 — No sales in last 60 days");
                return;
            }

            var pids = recentSales.Select(s => s.ProductId).Distinct().ToList();
            var catByPid = await _context.Products
                .AsNoTracking()
                .Where(p => pids.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId, p => p.Category);

            var catRevCurr = recentSales
                .Where(s => s.SaleDate >= curr30 && catByPid.ContainsKey(s.ProductId))
                .GroupBy(s => catByPid[s.ProductId])
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Revenue));

            var catRevPrev = recentSales
                .Where(s => s.SaleDate < curr30 && catByPid.ContainsKey(s.ProductId))
                .GroupBy(s => catByPid[s.ProductId])
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Revenue));

            string? worstCat  = null;
            double  worstDecl = 0.0;

            foreach (var cat in catRevPrev.Keys)
            {
                var prev = catRevPrev[cat];
                if (prev < 500m) continue;  // skip low-volume categories
                var curr   = catRevCurr.GetValueOrDefault(cat, 0m);
                double pct = (double)((prev - curr) / prev * 100m);
                if (pct > 15.0 && pct > worstDecl)
                {
                    worstDecl = pct;
                    worstCat  = cat;
                }
            }

            _logger.LogInformation("[Notifications] Source 3 — Category decline worst: {Cat} {Pct:N0}%",
                worstCat ?? "none", worstDecl);

            if (worstCat != null)
            {
                var prevRev = catRevPrev[worstCat];
                var currRev = catRevCurr.GetValueOrDefault(worstCat, 0m);
                list.Add(new AppNotification
                {
                    Id        = $"si-decline-{worstCat.ToLowerInvariant().Replace(" ", "-")}",
                    Title     = $"{worstCat} Revenue Down {worstDecl:N0}%",
                    Message   = $"€{currRev:N0} (last 30d) vs €{prevRev:N0} (prior 30d). Review demand signals and stock positioning.",
                    Severity  = worstDecl >= 35.0 ? "Critical" : "Warning",
                    Module    = "Smart Insights",
                    ModuleUrl = "/SmartInsights",
                    TimeLabel = "This week"
                });
            }
        }

        // SOURCE 4 & 5: Forecasting 
        private async Task AddForecastingNotifications(List<AppNotification> list)
        {
            // 4a. Forecast confidence warning
            var bestAccuracy = await _context.ForecastAccuracies
                .AsNoTracking()
                .Where(a => a.Target == "Revenue")
                .OrderByDescending(a => a.AccuracyPercent)
                .Select(a => (decimal?)a.AccuracyPercent)
                .FirstOrDefaultAsync();

            _logger.LogInformation("[Notifications] Source 4a — Forecast accuracy: {Acc}",
                bestAccuracy.HasValue ? $"{bestAccuracy:N1}%" : "no runs");

            if (bestAccuracy.HasValue && bestAccuracy.Value < 60m)
            {
                list.Add(new AppNotification
                {
                    Id        = "forecast-low-confidence",
                    Title     = "Low Forecast Confidence",
                    Message   = $"Best model accuracy is {bestAccuracy:N1}%. More historical data may improve reliability.",
                    Severity  = bestAccuracy < 45m ? "Critical" : "Warning",
                    Module    = "Forecasting",
                    ModuleUrl = "/Forecasting",
                    TimeLabel = "This week"
                });
            }

            // 4b. Forecast vs current stock gap
            var latestRunAt = await _context.ForecastResults
                .AsNoTracking()
                .MaxAsync(f => (DateTime?)f.GeneratedAt);

            if (!latestRunAt.HasValue)
            {
                _logger.LogInformation("[Notifications] Source 4b — No forecast runs found");
                return;
            }

            var forecastedByCategory = await _context.ForecastResults
                .AsNoTracking()
                .Where(f => f.GeneratedAt == latestRunAt.Value)
                .GroupBy(f => f.Category)
                .Select(g => new { Category = g.Key, Units = g.Sum(x => x.UnitsForecast) })
                .ToListAsync();

            if (!forecastedByCategory.Any()) return;

            var stockByCategory = await (
                from inv in _context.Inventories.AsNoTracking()
                join p   in _context.Products.AsNoTracking() on inv.ProductId equals p.ProductId
                group inv.CurrentStock by p.Category into g
                select new { Category = g.Key, Stock = g.Sum() }
            ).ToListAsync();

            var stockDict = stockByCategory.ToDictionary(x => x.Category, x => x.Stock);

            var gapCategories = forecastedByCategory
                .Where(f =>
                {
                    var stock = stockDict.GetValueOrDefault(f.Category, 0);
                    return f.Units > stock && (f.Units - stock) > 20;
                })
                .OrderByDescending(f => f.Units - stockDict.GetValueOrDefault(f.Category, 0))
                .ToList();

            _logger.LogInformation("[Notifications] Source 4b — Forecast stock gap categories: {Count}", gapCategories.Count);

            if (gapCategories.Count >= 2)
            {
                var topGap   = gapCategories[0];
                var gapUnits = topGap.Units - stockDict.GetValueOrDefault(topGap.Category, 0);
                list.Add(new AppNotification
                {
                    Id        = "forecast-stock-gap",
                    Title     = $"Forecasted Demand Exceeds Stock in {gapCategories.Count} Categories",
                    Message   = $"Largest gap: {topGap.Category} needs {gapUnits:N0} more units. Review purchase orders.",
                    Severity  = gapCategories.Count >= 4 ? "Critical" : "Warning",
                    Module    = "Forecasting",
                    ModuleUrl = "/Forecasting",
                    TimeLabel = "This week"
                });
            }
        }

        // SOURCE 6 & 7: Sustainability
        private async Task AddSustainabilityNotifications(List<AppNotification> list)
        {
            var today           = DateTime.Today;
            var deadStockCutoff = today.AddDays(-90);  // Matches InventoriesController window

            // 6a. Dead stock: in-stock products with no sales in 90 days.
            // Was 45 days — increased to match InventoriesController and to catch expired-season
            // products whose generator stopped ordering after their season end date.
            var recentSoldPids = await _context.Sales
                .AsNoTracking()
                .Where(s => s.SaleDate >= deadStockCutoff)
                .Select(s => s.ProductId)
                .Distinct()
                .ToListAsync();

            var deadStockCount = await _context.Inventories
                .AsNoTracking()
                .Where(i => i.CurrentStock > 0 && !recentSoldPids.Contains(i.ProductId))
                .Select(i => i.ProductId)
                .Distinct()
                .CountAsync();

            _logger.LogInformation("[Notifications] Source 6a — Dead stock (90d no-sale): {Count}", deadStockCount);

            if (deadStockCount > 0)
            {
                list.Add(new AppNotification
                {
                    Id        = "sustainability-dead-stock",
                    Title     = $"{deadStockCount} Dead Stock SKUs",
                    Message   = $"Products holding inventory with zero sales in the past 90 days. Consider liquidation or markdown.",
                    Severity  = deadStockCount >= 10 ? "Warning" : "Info",
                    Module    = "Sustainability",
                    ModuleUrl = "/Sustainability",
                    TimeLabel = "Today"
                });
            }

            // 6b. Expired seasonal stock still in inventory
            var expiredSeasonalCount = await (
                from inv in _context.Inventories.AsNoTracking()
                where inv.CurrentStock > 0
                join p in _context.Products.AsNoTracking() on inv.ProductId equals p.ProductId
                where p.IsSeasonal && p.Season != null && ExpiredSeasons.Contains(p.Season)
                select inv.InventoryId
            ).CountAsync();

            _logger.LogInformation("[Notifications] Source 6b — Expired-season stock: {Count}", expiredSeasonalCount);

            if (expiredSeasonalCount > 0)
            {
                list.Add(new AppNotification
                {
                    Id        = "sustainability-expired-season",
                    Title     = $"{expiredSeasonalCount} Expired-Season Products Still in Stock",
                    Message   = "Seasonal items past their sell date (AW24, SS25, AW25). Initiate clearance, markdown, or donation.",
                    Severity  = expiredSeasonalCount >= 5 ? "Critical" : "Warning",
                    Module    = "Sustainability",
                    ModuleUrl = "/Sustainability",
                    TimeLabel = "Today"
                });
            }
        }
    }
}
