using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using FashionDataAnalysisPlatform.Data;
using FashionDataAnalysisPlatform.Models;

namespace FashionDataAnalysisPlatform.Controllers
{
    public class InventoriesController : Controller
    {
        private readonly AppDbContext _context;

        public InventoriesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: Inventories
        public async Task<IActionResult> Index(List<int>? storeIds, string? dateRange = "all")
        {
            var today = DateTime.Today;
            DateTime? salesStart = dateRange switch {
                "7d"  => today.AddDays(-7),
                "30d" => today.AddDays(-30),
                "90d" => today.AddDays(-90),
                "12m" => today.AddMonths(-12),
                "all" => (DateTime?)null,
                _     => today.AddDays(-30)
            };
            // For velocity KPIs (weeks of cover, turnover) "all" uses 365-day annualised rate
            var refDays  = salesStart.HasValue ? Math.Max((today - salesStart.Value).TotalDays, 1) : 365.0;
            var refWeeks = refDays / 7.0;

            ViewBag.DateRange = dateRange;
            ViewBag.StoreIds  = storeIds ?? new List<int>();

            // ── 1. Raw inventory (no navigation props) ──────────────
            var invQuery = _context.Inventories.AsNoTracking();
            if (storeIds != null && storeIds.Count > 0)
                invQuery = invQuery.Where(i => i.StoreId != null && storeIds.Contains(i.StoreId.Value));

            var rawInv = await invQuery
                .Select(i => new {
                    i.InventoryId,
                    i.ProductId,
                    i.CurrentStock,
                    i.MinimumStockThreshold,
                    i.LastUpdated,
                    i.LastRestockDate,
                    i.StoreId
                })
                .ToListAsync();

            // Empty-state fallback
            if (!rawInv.Any())
            {
                ViewBag.NoData          = true;
                ViewBag.TotalSKUs       = 0;
                ViewBag.UnitsOnHand     = 0;
                ViewBag.InventoryValue  = 0m;
                ViewBag.LowStockCount   = 0;
                ViewBag.OutOfStockCount = 0;
                ViewBag.WeeksOfCover    = 0.0;
                ViewBag.SellThroughRate   = 0.0;
                ViewBag.StockToSalesRatio = 0.0;
                ViewBag.InvTurnover       = 0.0;
                ViewBag.OverstockCount    = 0;
                ViewBag.LowStockAlerts  = "[]";
                ViewBag.DeadStockItems  = "[]";
                ViewBag.AgingLabels     = "[\"<30d\",\"30–60d\",\"61–90d\",\"91–180d\",\">180d\"]";
                ViewBag.AgingCounts     = "[0,0,0,0,0]";
                ViewBag.PriorityCritical = 0;
                ViewBag.PriorityHigh    = 0;
                ViewBag.PriorityMedium  = 0;
                ViewBag.PriorityLow     = 0;
                ViewBag.CatLabels       = "[]";
                ViewBag.CatValues       = "[]";
                ViewBag.SmartInsights   = "[]";
                return View();
            }

            // ── 2. Batch product lookup ──────────────────────────────
            var invProductIds = rawInv.Select(i => i.ProductId).Distinct().ToList();
            var productRaw = await _context.Products.AsNoTracking()
                .Where(p => invProductIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.ProductName, p.Category, p.UnitPrice, p.BaseCost })
                .ToListAsync();
            var productMap = productRaw.ToDictionary(p => p.ProductId);

            // ── 3. Store names ───────────────────────────────────────
            var invStoreIds = rawInv.Select(i => i.StoreId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct().ToList();
            Dictionary<int, string> storeMap;
            if (invStoreIds.Count > 0)
            {
                var storeRaw = await _context.Stores.AsNoTracking()
                    .Where(s => invStoreIds.Contains(s.StoreId))
                    .Select(s => new { s.StoreId, s.StoreName })
                    .ToListAsync();
                storeMap = storeRaw.ToDictionary(s => s.StoreId, s => s.StoreName);
            }
            else { storeMap = new Dictionary<int, string>(); }

            // ── 4. Sales in reference period ─────────────────────────
            var salesRefQ = _context.Sales.AsNoTracking().AsQueryable();
            if (salesStart.HasValue)
                salesRefQ = salesRefQ.Where(s => s.SaleDate >= salesStart.Value && s.SaleDate <= today);
            if (storeIds != null && storeIds.Count > 0)
                salesRefQ = salesRefQ.Where(s => s.StoreId != null && storeIds.Contains(s.StoreId.Value));

            var rawSalesRef = await salesRefQ
                .Select(s => new { s.ProductId, s.Quantity })
                .ToListAsync();

            // ── 5. Last sale dates per product (dead stock detection) ─
            var lastSalesQ = _context.Sales.AsNoTracking()
                .Where(s => invProductIds.Contains(s.ProductId));
            if (storeIds != null && storeIds.Count > 0)
                lastSalesQ = lastSalesQ.Where(s => s.StoreId != null && storeIds.Contains(s.StoreId.Value));

            var lastSalesRaw = await lastSalesQ
                .GroupBy(s => s.ProductId)
                .Select(g => new { ProductId = g.Key, LastSale = g.Max(s => s.SaleDate) })
                .ToListAsync();
            var lastSaleDates = lastSalesRaw.ToDictionary(x => x.ProductId, x => x.LastSale);

            // ── KPIs ─────────────────────────────────────────────────
            var totalSKUs       = rawInv.Count;
            var unitsOnHand     = rawInv.Sum(i => i.CurrentStock);
            var inventoryValue  = rawInv.Sum(i => {
                var price = productMap.TryGetValue(i.ProductId, out var p) ? p.UnitPrice : 0m;
                return price * i.CurrentStock;
            });
            var lowStockCount   = rawInv.Count(i => i.CurrentStock > 0 && i.CurrentStock <= i.MinimumStockThreshold);
            var outOfStockCount = rawInv.Count(i => i.CurrentStock == 0);

            var salesByProduct  = rawSalesRef.GroupBy(s => s.ProductId)
                                    .ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));
            var totalSoldRef    = rawSalesRef.Sum(s => s.Quantity);
            var avgWeeklySales  = totalSoldRef / refWeeks;
            var weeksOfCover    = avgWeeklySales > 0 ? unitsOnHand / avgWeeklySales : 0;

            // ── Stock Health ─────────────────────────────────────────
            var sellThroughRate   = (unitsOnHand + totalSoldRef) > 0
                ? (double)totalSoldRef / (unitsOnHand + totalSoldRef) * 100 : 0;
            var stockToSalesRatio = totalSoldRef > 0
                ? (double)unitsOnHand / totalSoldRef : 0;
            var invTurnover       = unitsOnHand > 0
                ? totalSoldRef / (refDays / 365.0) / unitsOnHand : 0;
            var overstockCount    = rawInv.Count(i => {
                var sold = salesByProduct.TryGetValue(i.ProductId, out var s) ? s : 0;
                var avgWeekly = refWeeks > 0 ? sold / refWeeks : 0;
                return avgWeekly > 0 && i.CurrentStock > avgWeekly * 8;
            });

            // ── Low Stock Alerts ─────────────────────────────────────
            var lowStockAlerts = rawInv
                .Where(i => i.CurrentStock <= i.MinimumStockThreshold)
                .OrderBy(i => i.CurrentStock)
                .Take(10)
                .Select(i => {
                    productMap.TryGetValue(i.ProductId, out var p);
                    storeMap.TryGetValue(i.StoreId ?? 0, out var storeName);
                    return new {
                        ProductName  = p?.ProductName ?? "Unknown",
                        Category     = p?.Category    ?? "—",
                        CurrentStock = i.CurrentStock,
                        MinThreshold = i.MinimumStockThreshold,
                        Gap          = i.MinimumStockThreshold - i.CurrentStock,
                        StoreName    = storeName ?? "All Stores",
                        IsOutOfStock = i.CurrentStock == 0
                    };
                }).ToList();

            // ── Dead Stock ───────────────────────────────────────────
            var deadStockItems = rawInv
                .Where(i => i.CurrentStock > 0)
                .Select(i => {
                    var lastSale = lastSaleDates.TryGetValue(i.ProductId, out var d) ? (DateTime?)d : null;
                    var refDate  = lastSale ?? (i.LastRestockDate ?? i.LastUpdated);
                    var idleDays = (today - refDate).Days;
                    productMap.TryGetValue(i.ProductId, out var p);
                    return new {
                        ProductName  = p?.ProductName ?? "Unknown",
                        Category     = p?.Category    ?? "—",
                        CurrentStock = i.CurrentStock,
                        IdleDays     = idleDays,
                        LastSaleDate = lastSale.HasValue ? lastSale.Value.ToString("dd MMM yyyy") : "Never sold",
                        Value        = (p?.UnitPrice ?? 0m) * i.CurrentStock
                    };
                })
                .Where(x => x.IdleDays >= 90)
                .OrderByDescending(x => x.IdleDays)
                .Take(10)
                .ToList();

            // ── Inventory Aging ──────────────────────────────────────
            var aging = new int[5]; // <30d, 30–60d, 61–90d, 91–180d, >180d
            foreach (var inv in rawInv)
            {
                var age = (today - (inv.LastRestockDate ?? inv.LastUpdated)).Days;
                if      (age < 30)  aging[0]++;
                else if (age < 60)  aging[1]++;
                else if (age < 90)  aging[2]++;
                else if (age < 180) aging[3]++;
                else                aging[4]++;
            }

            // ── Reorder Priority ─────────────────────────────────────
            var priCritical = rawInv.Count(i => i.CurrentStock == 0);
            var priHigh     = rawInv.Count(i => i.CurrentStock > 0 && i.CurrentStock < i.MinimumStockThreshold);
            var priMedium   = rawInv.Count(i => i.CurrentStock >= i.MinimumStockThreshold
                                             && i.CurrentStock <= i.MinimumStockThreshold * 2);
            var priLow      = rawInv.Count(i => i.CurrentStock > i.MinimumStockThreshold * 2);

            // ── Category Inventory Value ─────────────────────────────
            var catGroups = rawInv
                .GroupBy(i => productMap.TryGetValue(i.ProductId, out var p) ? (p.Category ?? "Other") : "Other")
                .Select(g => new {
                    Category = g.Key,
                    Value    = g.Sum(i => {
                        var price = productMap.TryGetValue(i.ProductId, out var p) ? p.UnitPrice : 0m;
                        return (double)(price * i.CurrentStock);
                    })
                })
                .OrderByDescending(x => x.Value)
                .Take(8)
                .ToList();

            // ── Smart Insights ────────────────────────────────────────
            var insights = new List<object>();

            if (outOfStockCount > 0)
            {
                var pct = totalSKUs > 0 ? (double)outOfStockCount / totalSKUs * 100 : 0;
                insights.Add(new { tag = "danger", icon = "fa-circle-xmark", title = "Out-of-stock alert",
                    body = $"{outOfStockCount} SKU{(outOfStockCount == 1 ? "" : "s")} ({pct:F0}% of inventory) have zero units — immediate replenishment needed." });
            }

            var deadValue = deadStockItems.Sum(d => d.Value);
            if (deadValue > 0)
                insights.Add(new { tag = "warning", icon = "fa-box-archive", title = "Capital tied in dead stock",
                    body = $"€{deadValue:N0} in inventory has had no sales for 90+ days. Consider markdowns or redistribution to free working capital." });

            if (weeksOfCover < 2 && weeksOfCover > 0)
                insights.Add(new { tag = "danger", icon = "fa-circle-exclamation", title = "Low weeks of cover",
                    body = $"Stock covers only {weeksOfCover:F1}w at the current sell rate — reorder urgently to avoid stockouts." });
            else if (weeksOfCover > 24)
                insights.Add(new { tag = "warning", icon = "fa-triangle-exclamation", title = "Excess stock position",
                    body = $"Inventory cover is very high — stock is accumulating faster than it is selling, increasing holding costs and obsolescence risk." });
            else if (weeksOfCover >= 2)
                insights.Add(new { tag = "success", icon = "fa-circle-check", title = "Healthy stock coverage",
                    body = $"Weeks of cover at {weeksOfCover:F1}w — inventory is well-matched to current sales velocity." });

            if (catGroups.Any())
            {
                var top    = catGroups.First();
                var totalV = catGroups.Sum(c => c.Value);
                var topPct = totalV > 0 ? top.Value / totalV * 100 : 0;
                if (topPct > 40)
                    insights.Add(new { tag = "info", icon = "fa-circle-info", title = $"{top.Category} dominates inventory",
                        body = $"{top.Category} holds {topPct:F0}% of total inventory value — consider broadening category mix to reduce concentration risk." });
                else
                    insights.Add(new { tag = "info", icon = "fa-circle-info", title = "Category mix is balanced",
                        body = $"No single category exceeds 40% of inventory value. {top.Category} leads at {topPct:F0}%." });
            }

            if (sellThroughRate < 20)
                insights.Add(new { tag = "warning", icon = "fa-chart-line", title = "Low sell-through rate",
                    body = $"Only {sellThroughRate:F0}% sell-through in the period — stock is moving slowly relative to available supply. Review pricing and promotions." });
            else if (sellThroughRate > 65)
                insights.Add(new { tag = "success", icon = "fa-circle-check", title = "Strong sell-through rate",
                    body = $"{sellThroughRate:F0}% sell-through — products are selling well. Monitor closely to avoid stockouts on top SKUs." });

            var oldStockCount = aging[3] + aging[4];
            if (oldStockCount > 0 && insights.Count < 6)
            {
                var oldPct = totalSKUs > 0 ? (double)oldStockCount / totalSKUs * 100 : 0;
                insights.Add(new { tag = "warning", icon = "fa-clock-rotate-left", title = "Aging stock risk",
                    body = $"{oldStockCount} SKU{(oldStockCount == 1 ? "" : "s")} ({oldPct:F0}%) have been in stock for 90+ days. Promotional activity recommended." });
            }

            while (insights.Count < 3)
                insights.Add(new { tag = "info", icon = "fa-circle-check", title = "Inventory health nominal",
                    body = "No critical inventory issues detected in the selected period." });

            // ── ViewBag bindings ──────────────────────────────────────
            var opts = new JsonSerializerOptions();

            ViewBag.TotalSKUs       = totalSKUs;
            ViewBag.UnitsOnHand     = unitsOnHand;
            ViewBag.InventoryValue  = inventoryValue;
            ViewBag.LowStockCount   = lowStockCount;
            ViewBag.OutOfStockCount = outOfStockCount;
            ViewBag.WeeksOfCover    = Math.Round(weeksOfCover, 1);

            ViewBag.SellThroughRate   = Math.Round(sellThroughRate,   1);
            ViewBag.StockToSalesRatio = Math.Round(stockToSalesRatio, 2);
            ViewBag.InvTurnover       = Math.Round(invTurnover,       2);
            ViewBag.OverstockCount    = overstockCount;

            ViewBag.LowStockAlerts = JsonSerializer.Serialize(lowStockAlerts, opts);
            ViewBag.DeadStockItems = JsonSerializer.Serialize(deadStockItems, opts);

            ViewBag.AgingLabels = JsonSerializer.Serialize(new[] { "<30d", "30–60d", "61–90d", "91–180d", ">180d" }, opts);
            ViewBag.AgingCounts = JsonSerializer.Serialize(aging, opts);

            ViewBag.PriorityCritical = priCritical;
            ViewBag.PriorityHigh     = priHigh;
            ViewBag.PriorityMedium   = priMedium;
            ViewBag.PriorityLow      = priLow;

            ViewBag.CatLabels = JsonSerializer.Serialize(catGroups.Select(c => c.Category).ToArray(), opts);
            ViewBag.CatValues = JsonSerializer.Serialize(catGroups.Select(c => c.Value).ToArray(), opts);

            ViewBag.SmartInsights = JsonSerializer.Serialize(insights, opts);

            return View();
        }

        // GET: Inventories/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var inventory = await _context.Inventories
                .Include(i => i.Product)
                .FirstOrDefaultAsync(m => m.InventoryId == id);
            if (inventory == null)
            {
                return NotFound();
            }

            return View(inventory);
        }

        // GET: Inventories/Create
        public IActionResult Create()
        {
            ViewData["ProductId"] = new SelectList(_context.Products, "ProductId", "ProductName");
            return View();
        }

        // POST: Inventories/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("InventoryId,ProductId,CurrentStock,LastUpdated")] Inventory inventory)
        {
            if (ModelState.IsValid)
            {
                _context.Add(inventory);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["ProductId"] = new SelectList(_context.Products, "ProductId", "ProductName", inventory.ProductId);
            return View(inventory);
        }

        // GET: Inventories/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var inventory = await _context.Inventories.FindAsync(id);
            if (inventory == null)
            {
                return NotFound();
            }
            ViewData["ProductId"] = new SelectList(_context.Products, "ProductId", "ProductName", inventory.ProductId);
            return View(inventory);
        }

        // POST: Inventories/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("InventoryId,ProductId,CurrentStock,LastUpdated")] Inventory inventory)
        {
            if (id != inventory.InventoryId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(inventory);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!InventoryExists(inventory.InventoryId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["ProductId"] = new SelectList(_context.Products, "ProductId", "ProductName", inventory.ProductId);
            return View(inventory);
        }

        // GET: Inventories/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var inventory = await _context.Inventories
                .Include(i => i.Product)
                .FirstOrDefaultAsync(m => m.InventoryId == id);
            if (inventory == null)
            {
                return NotFound();
            }

            return View(inventory);
        }

        // POST: Inventories/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var inventory = await _context.Inventories.FindAsync(id);
            if (inventory != null)
            {
                _context.Inventories.Remove(inventory);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool InventoryExists(int id)
        {
            return _context.Inventories.Any(e => e.InventoryId == id);
        }
    }
}
