using FashionDataAnalysisPlatform.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FashionDataAnalysisPlatform.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(List<int>? storeIds, string? dateRange = "all")
        {
            storeIds  ??= new List<int>();
            dateRange ??= "all";

            var stores = await _context.Stores
                .AsNoTracking()
                .OrderBy(s => s.StoreName)
                .ToListAsync();

            ViewBag.Stores           = stores;
            ViewBag.SelectedStoreIds = storeIds;
            ViewBag.DateRange        = dateRange;

            // ── Date cut-off ───────────────────────────────────────────────
            DateTime? cutoff = dateRange switch
            {
                "7d"  => DateTime.Today.AddDays(-7),
                "30d" => DateTime.Today.AddDays(-30),
                "90d" => DateTime.Today.AddDays(-90),
                "12m" => DateTime.Today.AddMonths(-12),
                _     => null
            };

            // ── Base sales query ───────────────────────────────────────────
            var salesQuery = _context.Sales
                .AsNoTracking()
                .Include(s => s.Store)
                .Include(s => s.Product)
                .AsQueryable();

            if (storeIds.Any())
                salesQuery = salesQuery.Where(s =>
                    s.StoreId.HasValue && storeIds.Contains(s.StoreId.Value));

            if (cutoff.HasValue)
                salesQuery = salesQuery.Where(s => s.SaleDate >= cutoff.Value);

            // ── KPIs ───────────────────────────────────────────────────────
            var totalRevenue  = await salesQuery.SumAsync(s => (decimal?)s.Revenue) ?? 0;
            var totalUnits    = await salesQuery.SumAsync(s => (int?)s.Quantity)    ?? 0;
            var grossProfit   = await salesQuery.SumAsync(s => (decimal?)s.Profit)  ?? 0;

            var totalOrders = await salesQuery
                .Where(s => s.ExternalOrderId != null)
                .Select(s => s.ExternalOrderId)
                .Distinct()
                .CountAsync();

            var averageOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0;
            var profitMargin      = totalRevenue > 0 ? grossProfit / totalRevenue * 100 : 0;

            var topCategory = await salesQuery
                .Where(s => s.Product != null)
                .GroupBy(s => s.Product!.Category)
                .Select(g => new { Category = g.Key, Revenue = g.Sum(x => x.Revenue) })
                .OrderByDescending(x => x.Revenue)
                .FirstOrDefaultAsync();

            var lowStockQuery = _context.Inventories.AsNoTracking().AsQueryable();
            if (storeIds.Any())
                lowStockQuery = lowStockQuery.Where(i =>
                    i.StoreId.HasValue && storeIds.Contains(i.StoreId.Value));

            var lowStockCount = await lowStockQuery
                .CountAsync(i => i.CurrentStock <= i.MinimumStockThreshold);

            ViewBag.TotalRevenue      = totalRevenue;
            ViewBag.TotalOrders       = totalOrders;
            ViewBag.TotalUnits        = totalUnits;
            ViewBag.GrossProfit       = grossProfit;
            ViewBag.AverageOrderValue = averageOrderValue;
            ViewBag.ProfitMargin      = profitMargin;
            ViewBag.TopCategory       = topCategory?.Category ?? "N/A";
            ViewBag.LowStockCount     = lowStockCount;

            // ── Prior-period KPIs (trend indicators) ───────────────────────
            decimal priorRevenue = 0, priorProfit = 0;
            int     priorOrders  = 0, priorUnits  = 0;

            if (cutoff.HasValue)
            {
                var span       = (DateTime.Today - cutoff.Value).TotalDays;
                var priorEnd   = cutoff.Value;
                var priorStart = cutoff.Value.AddDays(-span);

                var priorQ = _context.Sales.AsNoTracking().AsQueryable();
                if (storeIds.Any())
                    priorQ = priorQ.Where(s => s.StoreId.HasValue && storeIds.Contains(s.StoreId.Value));
                priorQ = priorQ.Where(s => s.SaleDate >= priorStart && s.SaleDate < priorEnd);

                priorRevenue = await priorQ.SumAsync(s => (decimal?)s.Revenue) ?? 0;
                priorUnits   = await priorQ.SumAsync(s => (int?)s.Quantity)    ?? 0;
                priorProfit  = await priorQ.SumAsync(s => (decimal?)s.Profit)  ?? 0;
                priorOrders  = await priorQ
                    .Where(s => s.ExternalOrderId != null)
                    .Select(s => s.ExternalOrderId)
                    .Distinct()
                    .CountAsync();
            }

            ViewBag.PriorRevenue = priorRevenue;
            ViewBag.PriorOrders  = priorOrders;
            ViewBag.PriorUnits   = priorUnits;
            ViewBag.PriorProfit  = priorProfit;
            ViewBag.PriorAov     = priorOrders > 0 ? priorRevenue / priorOrders : 0m;
            ViewBag.PriorMargin  = priorRevenue > 0 ? priorProfit / priorRevenue * 100 : 0m;

            // ── Revenue trend — granularity adapts to selected date range ──
            string[]  trendLabels;
            decimal[] trendRevenue;
            decimal[] trendProfit;
            string    trendGranularity;

            if (dateRange == "7d" || dateRange == "30d")
            {
                // Daily grouping
                var dailyRaw = await salesQuery
                    .GroupBy(s => s.SaleDate.Date)
                    .Select(g => new
                    {
                        Date    = g.Key,
                        Revenue = g.Sum(x => x.Revenue),
                        Profit  = g.Sum(x => (decimal?)x.Profit) ?? 0
                    })
                    .OrderBy(x => x.Date)
                    .ToListAsync();

                // 7 days: short day name only ("Mon"); 30 days: "dd MMM" ("15 Jan")
                string dailyFmt = dateRange == "7d" ? "ddd" : "dd MMM";
                trendLabels      = dailyRaw.Select(x => x.Date.ToString(dailyFmt)).ToArray();
                trendRevenue     = dailyRaw.Select(x => Math.Round(x.Revenue, 2)).ToArray();
                trendProfit      = dailyRaw.Select(x => Math.Round(x.Profit,  2)).ToArray();
                trendGranularity = "daily";
            }
            else if (dateRange == "90d")
            {
                // Weekly grouping — project minimal columns then aggregate in C#
                var raw90 = await salesQuery
                    .Select(s => new { s.SaleDate, s.Revenue, s.Profit })
                    .ToListAsync();

                var weeklyRaw = raw90
                    .GroupBy(s =>
                    {
                        var d    = s.SaleDate.Date;
                        int diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                        return d.AddDays(-diff); // Monday that starts this week
                    })
                    .Select(g => new
                    {
                        Date    = g.Key,
                        Revenue = g.Sum(x => x.Revenue),
                        Profit  = g.Sum(x => x.Profit ?? 0)
                    })
                    .OrderBy(x => x.Date)
                    .ToList();

                trendLabels      = weeklyRaw.Select(x => x.Date.ToString("dd MMM")).ToArray();
                trendRevenue     = weeklyRaw.Select(x => Math.Round(x.Revenue, 2)).ToArray();
                trendProfit      = weeklyRaw.Select(x => Math.Round(x.Profit,  2)).ToArray();
                trendGranularity = "weekly";
            }
            else
            {
                // Monthly grouping for 12m and all-time
                var monthlyRaw = await salesQuery
                    .GroupBy(s => new { s.SaleDate.Year, s.SaleDate.Month })
                    .Select(g => new
                    {
                        g.Key.Year,
                        g.Key.Month,
                        Revenue = g.Sum(x => x.Revenue),
                        Profit  = g.Sum(x => (decimal?)x.Profit) ?? 0
                    })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month)
                    .ToListAsync();

                // 12m: "Jan 2025"; all-time: "Jan 25" (shorter for dense axes)
                string monthlyFmt = dateRange == "12m" ? "MMM yyyy" : "MMM yy";
                trendLabels      = monthlyRaw.Select(x => new DateTime(x.Year, x.Month, 1).ToString(monthlyFmt)).ToArray();
                trendRevenue     = monthlyRaw.Select(x => Math.Round(x.Revenue, 2)).ToArray();
                trendProfit      = monthlyRaw.Select(x => Math.Round(x.Profit,  2)).ToArray();
                trendGranularity = "monthly";
            }

            ViewBag.TrendLabels       = JsonSerializer.Serialize(trendLabels);
            ViewBag.TrendRevenue      = JsonSerializer.Serialize(trendRevenue);
            ViewBag.TrendProfit       = JsonSerializer.Serialize(trendProfit);
            ViewBag.TrendGranularity  = trendGranularity;

            // ── Revenue + profit by category ───────────────────────────────
            var catRaw = await salesQuery
                .Where(s => s.Product != null)
                .GroupBy(s => s.Product!.Category)
                .Select(g => new
                {
                    Category = g.Key,
                    Revenue  = g.Sum(x => x.Revenue),
                    Profit   = g.Sum(x => (decimal?)x.Profit) ?? 0
                })
                .OrderByDescending(x => x.Revenue)
                .Take(8)
                .ToListAsync();

            ViewBag.CategoryLabels  = JsonSerializer.Serialize(
                catRaw.Select(x => x.Category ?? "Unknown").ToArray());
            ViewBag.CategoryRevenue = JsonSerializer.Serialize(
                catRaw.Select(x => Math.Round(x.Revenue, 2)).ToArray());
            ViewBag.CategoryProfit  = JsonSerializer.Serialize(
                catRaw.Select(x => Math.Round(x.Profit,  2)).ToArray());

            // ── Sales channel distribution ─────────────────────────────────
            var chanRaw = await salesQuery
                .Where(s => !string.IsNullOrEmpty(s.SalesChannel))
                .GroupBy(s => s.SalesChannel!)
                .Select(g => new
                {
                    Channel = g.Key,
                    Revenue = g.Sum(x => x.Revenue)
                })
                .OrderByDescending(x => x.Revenue)
                .ToListAsync();

            ViewBag.ChannelLabels  = JsonSerializer.Serialize(
                chanRaw.Select(x => x.Channel).ToArray());
            ViewBag.ChannelRevenue = JsonSerializer.Serialize(
                chanRaw.Select(x => Math.Round(x.Revenue, 2)).ToArray());

            // ── Top 5 products by revenue ──────────────────────────────────
            var topProductsRaw = await salesQuery
                .Where(s => s.Product != null)
                .GroupBy(s => new { s.Product!.ProductName, s.Product!.Category })
                .Select(g => new
                {
                    Name     = g.Key.ProductName,
                    Category = g.Key.Category,
                    Revenue  = g.Sum(x => x.Revenue),
                    Units    = g.Sum(x => (int?)x.Quantity) ?? 0,
                    Profit   = g.Sum(x => (decimal?)x.Profit) ?? 0
                })
                .OrderByDescending(x => x.Revenue)
                .Take(5)
                .ToListAsync();

            ViewBag.TopProducts = JsonSerializer.Serialize(
                topProductsRaw.Select(p => new
                {
                    name     = p.Name     ?? "Unknown",
                    category = p.Category ?? "Unknown",
                    revenue  = Math.Round(p.Revenue, 2),
                    units    = p.Units,
                    margin   = p.Revenue > 0 ? Math.Round(p.Profit / p.Revenue * 100, 1) : 0m
                })
            );

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> LiveMetrics(List<int>? storeIds, string? dateRange = "all")
        {
            storeIds  ??= new List<int>();
            dateRange ??= "all";

            DateTime? cutoff = dateRange switch
            {
                "7d"  => DateTime.Today.AddDays(-7),
                "30d" => DateTime.Today.AddDays(-30),
                "90d" => DateTime.Today.AddDays(-90),
                "12m" => DateTime.Today.AddMonths(-12),
                _     => null
            };

            var salesQuery = _context.Sales
                .AsNoTracking()
                .Include(s => s.Store)
                .Include(s => s.Product)
                .AsQueryable();

            if (storeIds.Any())
                salesQuery = salesQuery.Where(s =>
                    s.StoreId.HasValue && storeIds.Contains(s.StoreId.Value));

            if (cutoff.HasValue)
                salesQuery = salesQuery.Where(s => s.SaleDate >= cutoff.Value);

            // KPIs
            var totalRevenue  = await salesQuery.SumAsync(s => (decimal?)s.Revenue) ?? 0;
            var totalUnits    = await salesQuery.SumAsync(s => (int?)s.Quantity)    ?? 0;
            var grossProfit   = await salesQuery.SumAsync(s => (decimal?)s.Profit)  ?? 0;

            var totalOrders = await salesQuery
                .Where(s => s.ExternalOrderId != null)
                .Select(s => s.ExternalOrderId)
                .Distinct()
                .CountAsync();

            var averageOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0;
            var profitMargin      = totalRevenue > 0 ? grossProfit / totalRevenue * 100 : 0;

            var topCategoryRaw = await salesQuery
                .Where(s => s.Product != null)
                .GroupBy(s => s.Product!.Category)
                .Select(g => new { Category = g.Key, Revenue = g.Sum(x => x.Revenue) })
                .OrderByDescending(x => x.Revenue)
                .FirstOrDefaultAsync();

            var lowStockQuery = _context.Inventories.AsNoTracking().AsQueryable();
            if (storeIds.Any())
                lowStockQuery = lowStockQuery.Where(i =>
                    i.StoreId.HasValue && storeIds.Contains(i.StoreId.Value));
            var lowStockCount = await lowStockQuery
                .CountAsync(i => i.CurrentStock <= i.MinimumStockThreshold);

            string topCategory = topCategoryRaw?.Category ?? "N/A";

            // Revenue trend
            string[]  trendLabels;
            decimal[] trendRevenue;
            decimal[] trendProfit;
            string    trendGranularity;

            if (dateRange == "7d" || dateRange == "30d")
            {
                var dailyRaw = await salesQuery
                    .GroupBy(s => s.SaleDate.Date)
                    .Select(g => new { Date = g.Key, Revenue = g.Sum(x => x.Revenue), Profit = g.Sum(x => (decimal?)x.Profit) ?? 0 })
                    .OrderBy(x => x.Date).ToListAsync();
                string dailyFmt = dateRange == "7d" ? "ddd" : "dd MMM";
                trendLabels      = dailyRaw.Select(x => x.Date.ToString(dailyFmt)).ToArray();
                trendRevenue     = dailyRaw.Select(x => Math.Round(x.Revenue, 2)).ToArray();
                trendProfit      = dailyRaw.Select(x => Math.Round(x.Profit,  2)).ToArray();
                trendGranularity = "daily";
            }
            else if (dateRange == "90d")
            {
                var raw90 = await salesQuery.Select(s => new { s.SaleDate, s.Revenue, s.Profit }).ToListAsync();
                var weeklyRaw = raw90
                    .GroupBy(s => { var d = s.SaleDate.Date; int diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7; return d.AddDays(-diff); })
                    .Select(g => new { Date = g.Key, Revenue = g.Sum(x => x.Revenue), Profit = g.Sum(x => x.Profit ?? 0) })
                    .OrderBy(x => x.Date).ToList();
                trendLabels      = weeklyRaw.Select(x => x.Date.ToString("dd MMM")).ToArray();
                trendRevenue     = weeklyRaw.Select(x => Math.Round(x.Revenue, 2)).ToArray();
                trendProfit      = weeklyRaw.Select(x => Math.Round(x.Profit,  2)).ToArray();
                trendGranularity = "weekly";
            }
            else
            {
                var monthlyRaw = await salesQuery
                    .GroupBy(s => new { s.SaleDate.Year, s.SaleDate.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(x => x.Revenue), Profit = g.Sum(x => (decimal?)x.Profit) ?? 0 })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month).ToListAsync();
                string monthlyFmt = dateRange == "12m" ? "MMM yyyy" : "MMM yy";
                trendLabels      = monthlyRaw.Select(x => new DateTime(x.Year, x.Month, 1).ToString(monthlyFmt)).ToArray();
                trendRevenue     = monthlyRaw.Select(x => Math.Round(x.Revenue, 2)).ToArray();
                trendProfit      = monthlyRaw.Select(x => Math.Round(x.Profit,  2)).ToArray();
                trendGranularity = "monthly";
            }

            // Category (used by profitability chart)
            var catRaw = await salesQuery
                .Where(s => s.Product != null)
                .GroupBy(s => s.Product!.Category)
                .Select(g => new { Category = g.Key, Revenue = g.Sum(x => x.Revenue), Profit = g.Sum(x => (decimal?)x.Profit) ?? 0 })
                .OrderByDescending(x => x.Revenue).Take(8).ToListAsync();

            // Channel
            var chanRaw = await salesQuery
                .Where(s => !string.IsNullOrEmpty(s.SalesChannel))
                .GroupBy(s => s.SalesChannel!)
                .Select(g => new { Channel = g.Key, Revenue = g.Sum(x => x.Revenue) })
                .OrderByDescending(x => x.Revenue).ToListAsync();

            // Top products
            var topProductsRaw = await salesQuery
                .Where(s => s.Product != null)
                .GroupBy(s => new { s.Product!.ProductName, s.Product!.Category })
                .Select(g => new { Name = g.Key.ProductName, Category = g.Key.Category, Revenue = g.Sum(x => x.Revenue), Units = g.Sum(x => (int?)x.Quantity) ?? 0, Profit = g.Sum(x => (decimal?)x.Profit) ?? 0 })
                .OrderByDescending(x => x.Revenue).Take(5).ToListAsync();

            // Alerts
            var alerts = new List<object>();
            if (lowStockCount == 0)
                alerts.Add(new { type = "success", icon = "fa-circle-check",        title = "Inventory Healthy",     message = "No products are below their minimum stock threshold." });
            else if (lowStockCount <= 5)
                alerts.Add(new { type = "warning", icon = "fa-triangle-exclamation", title = "Stock Monitor",         message = $"{lowStockCount} product(s) are near their minimum stock threshold. Review inventory levels." });
            else
                alerts.Add(new { type = "danger",  icon = "fa-circle-exclamation",  title = "Inventory Risk",         message = $"{lowStockCount} products are below minimum stock. Restocking recommended." });

            if (profitMargin >= 25)
                alerts.Add(new { type = "success", icon = "fa-arrow-trend-up",       title = "Strong Profitability",  message = $"Profit margin of {profitMargin:N1}% is above the 25% target." });
            else if (profitMargin > 0)
                alerts.Add(new { type = "info",    icon = "fa-chart-line",            title = "Margin Insight",        message = $"Current profit margin is {profitMargin:N1}%. Consider reviewing pricing or cost structure." });

            if (topCategory != "N/A")
                alerts.Add(new { type = "info",    icon = "fa-tag",                   title = "Category Leader",       message = $"{topCategory} is currently the highest-revenue product category." });

            alerts.Add(new { type = "success",     icon = "fa-rotate",                title = "Data Sync Active",      message = "Dashboard data is synchronised incrementally via the retail API using delta processing." });

            return Json(new
            {
                totalRevenue      = Math.Round(totalRevenue,      2),
                totalOrders,
                totalUnits,
                grossProfit       = Math.Round(grossProfit,       2),
                averageOrderValue = Math.Round(averageOrderValue, 2),
                profitMargin      = Math.Round(profitMargin,      2),
                topCategory,
                lowStockCount,
                trendLabels,
                trendRevenue,
                trendProfit,
                trendGranularity,
                categoryLabels  = catRaw.Select(x => x.Category ?? "Unknown").ToArray(),
                categoryRevenue = catRaw.Select(x => Math.Round(x.Revenue, 2)).ToArray(),
                categoryProfit  = catRaw.Select(x => Math.Round(x.Profit,  2)).ToArray(),
                channelLabels   = chanRaw.Select(x => x.Channel).ToArray(),
                channelRevenue  = chanRaw.Select(x => Math.Round(x.Revenue, 2)).ToArray(),
                topProducts     = topProductsRaw.Select(p => new
                {
                    name     = p.Name     ?? "Unknown",
                    category = p.Category ?? "Unknown",
                    revenue  = Math.Round(p.Revenue, 2),
                    units    = p.Units,
                    margin   = p.Revenue > 0 ? Math.Round(p.Profit / p.Revenue * 100, 1) : 0m
                }).ToArray(),
                alerts
            });
        }
    }
}
