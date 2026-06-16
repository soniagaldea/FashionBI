using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FashionDataAnalysisPlatform.Data;

namespace FashionDataAnalysisPlatform.Controllers
{
    [Authorize]
    public class StoreComparisonController : Controller
    {
        private readonly AppDbContext _context;
        public StoreComparisonController(AppDbContext context) => _context = context;

        public async Task<IActionResult> Index(string? dateRange = "all")
        {
            dateRange ??= "all";
            var today = DateTime.Today;
            DateTime? cutoff = dateRange switch
            {
                "7d"  => today.AddDays(-7),
                "30d" => today.AddDays(-30),
                "90d" => today.AddDays(-90),
                "12m" => today.AddMonths(-12),
                "all" => (DateTime?)null,
                _     => today.AddDays(-30)
            };
            double refDays  = cutoff.HasValue ? Math.Max((today - cutoff.Value).TotalDays, 1) : 365.0;
            double refWeeks = refDays / 7.0;

            ViewBag.DateRange = dateRange;

            // ── 1. Stores ────────────────────────────────────────────────────
            var stores = await _context.Stores.AsNoTracking()
                .OrderBy(s => s.StoreName)
                .Select(s => new { s.StoreId, s.StoreName, s.City, s.StoreType })
                .ToListAsync();

            if (!stores.Any())
            {
                ViewBag.StoreData     = "[]";
                ViewBag.StoreList     = "[]";
                ViewBag.SmartInsights = "[]";
                ViewBag.NoStores      = true;
                return View();
            }

            var allStoreIds = stores.Select(s => s.StoreId).ToList();

            // ── 2. Sales in period ────────────────────────────────────────────
            var salesQ = _context.Sales.AsNoTracking()
                .Where(s => s.StoreId != null && allStoreIds.Contains(s.StoreId.Value) && s.SaleDate <= today);
            if (cutoff.HasValue)
                salesQ = salesQ.Where(s => s.SaleDate >= cutoff.Value);

            var rawSales = await salesQ
                .Select(s => new {
                    StoreId  = s.StoreId!.Value,
                    s.ProductId,
                    s.Revenue,
                    Profit   = s.Profit ?? 0m,
                    s.Quantity,
                    Channel  = s.SalesChannel,
                    OrderId  = s.ExternalOrderId
                })
                .ToListAsync();

            // ── 2b. Prior-period sales ────────────────────────────────────────
            var priorSalesList = new List<(int StoreId, decimal Revenue, decimal Profit, int? OrderId)>();
            if (cutoff.HasValue)
            {
                var priorSpan  = (today - cutoff.Value).TotalDays;
                var priorEnd   = cutoff.Value;
                var priorStart = cutoff.Value.AddDays(-priorSpan);
                var ps = await _context.Sales.AsNoTracking()
                    .Where(s => s.StoreId != null && allStoreIds.Contains(s.StoreId.Value)
                             && s.SaleDate >= priorStart && s.SaleDate < priorEnd)
                    .Select(s => new {
                        StoreId = s.StoreId!.Value,
                        s.Revenue,
                        Profit  = s.Profit ?? 0m,
                        OrderId = s.ExternalOrderId
                    })
                    .ToListAsync();
                priorSalesList = ps.Select(x => (x.StoreId, x.Revenue, x.Profit, x.OrderId)).ToList();
            }

            // ── 3. Inventory snapshot ─────────────────────────────────────────
            var rawInv = await _context.Inventories.AsNoTracking()
                .Where(i => i.StoreId != null && allStoreIds.Contains(i.StoreId.Value))
                .Select(i => new {
                    StoreId  = i.StoreId!.Value,
                    i.ProductId,
                    i.CurrentStock,
                    i.MinimumStockThreshold,
                    i.LastUpdated,
                    i.LastRestockDate
                })
                .ToListAsync();

            // ── 4. Product prices ─────────────────────────────────────────────
            var invProductIds = rawInv.Select(i => i.ProductId).Distinct().ToList();
            var priceList = await _context.Products.AsNoTracking()
                .Where(p => invProductIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.UnitPrice })
                .ToListAsync();
            var priceMap = priceList.ToDictionary(p => p.ProductId, p => p.UnitPrice);

            // ── 5. Last sale dates per (store, product) for dead stock ─────────
            var lastSalesRaw = await _context.Sales.AsNoTracking()
                .Where(s => s.StoreId != null
                         && allStoreIds.Contains(s.StoreId.Value)
                         && invProductIds.Contains(s.ProductId))
                .GroupBy(s => new { s.StoreId, s.ProductId })
                .Select(g => new {
                    StoreId   = g.Key.StoreId!.Value,
                    g.Key.ProductId,
                    LastSale  = g.Max(x => x.SaleDate)
                })
                .ToListAsync();

            var lastSaleMap = lastSalesRaw
                .ToDictionary(x => (x.StoreId, x.ProductId), x => x.LastSale);

            // ── 6. Per-store analytics (all in-memory) ────────────────────────
            var storeDataList = stores.Select(store =>
            {
                var sid    = store.StoreId;
                var sList  = rawSales.Where(s => s.StoreId == sid).ToList();
                var iList  = rawInv.Where(i => i.StoreId == sid).ToList();

                decimal revenue = sList.Sum(s => s.Revenue);
                decimal profit  = sList.Sum(s => s.Profit);
                int     units   = sList.Sum(s => s.Quantity);
                int     orders  = sList
                    .Where(s => s.OrderId.HasValue)
                    .Select(s => s.OrderId!.Value)
                    .Distinct().Count();
                decimal aov    = orders > 0 ? Math.Round(revenue / orders, 2) : 0m;
                decimal margin = revenue > 0 ? Math.Round(profit / revenue * 100, 1) : 0m;

                // Channel revenue split
                decimal onlineRev   = sList.Where(s => s.Channel == "Online").Sum(s => s.Revenue);
                decimal mobileRev   = sList.Where(s => s.Channel == "Mobile").Sum(s => s.Revenue);
                decimal physicalRev = sList.Where(s => s.Channel == "Physical").Sum(s => s.Revenue);
                decimal chTotal     = onlineRev + mobileRev + physicalRev;

                decimal onlinePct   = chTotal > 0 ? Math.Round(onlineRev   / chTotal * 100, 1) : 0m;
                decimal mobilePct   = chTotal > 0 ? Math.Round(mobileRev   / chTotal * 100, 1) : 0m;
                decimal physicalPct = chTotal > 0 ? Math.Round(physicalRev / chTotal * 100, 1) : 0m;

                // Inventory KPIs
                decimal invValue    = iList.Sum(i => {
                    var price = priceMap.TryGetValue(i.ProductId, out var p) ? p : 0m;
                    return price * i.CurrentStock;
                });
                int unitsOnHand   = iList.Sum(i => i.CurrentStock);
                double invTurnover = unitsOnHand > 0 && units > 0
                    ? Math.Round(units / (refDays / 365.0) / unitsOnHand, 2) : 0.0;

                int lowStock  = iList.Count(i => i.CurrentStock > 0 && i.CurrentStock <= i.MinimumStockThreshold);
                int oos       = iList.Count(i => i.CurrentStock == 0);
                int deadStock = iList.Count(i => {
                    if (i.CurrentStock == 0) return false;
                    var key      = (sid, i.ProductId);
                    DateTime ref_ = lastSaleMap.TryGetValue(key, out var ls) ? ls
                                  : i.LastRestockDate ?? i.LastUpdated;
                    return (today - ref_).Days >= 90;
                });

                var pList   = priorSalesList.Where(s => s.StoreId == sid).ToList();
                decimal pRev    = pList.Sum(s => s.Revenue);
                decimal pProfit = pList.Sum(s => s.Profit);
                int     pOrders = pList.Where(s => s.OrderId.HasValue).Select(s => s.OrderId!.Value).Distinct().Count();
                decimal pMargin = pRev > 0 ? Math.Round(pProfit / pRev * 100, 1) : 0m;
                decimal pAov    = pOrders > 0 ? Math.Round(pRev / pOrders, 2) : 0m;

                return new {
                    storeId      = sid,
                    storeName    = store.StoreName,
                    city         = store.City   ?? "",
                    storeType    = store.StoreType ?? "",
                    revenue      = Math.Round(revenue, 2),
                    profit       = Math.Round(profit, 2),
                    orders,
                    units,
                    aov,
                    margin,
                    onlinePct,
                    mobilePct,
                    physicalPct,
                    invValue     = Math.Round(invValue, 2),
                    invTurnover,
                    lowStock,
                    oos,
                    deadStock,
                    prevRevenue  = Math.Round(pRev, 2),
                    prevMargin   = pMargin,
                    prevAov      = pAov
                };
            }).ToList();

            // ── 7. Smart cross-store insights ─────────────────────────────────
            var insights = new List<object>();
            var active   = storeDataList.Where(s => s.revenue > 0).ToList();

            if (active.Count >= 2)
            {
                var topRev = active.OrderByDescending(s => s.revenue).First();
                var botRev = active.OrderBy(s => s.revenue).First();
                decimal revGap = botRev.revenue > 0
                    ? Math.Round((topRev.revenue - botRev.revenue) / botRev.revenue * 100, 0) : 0m;
                insights.Add(new {
                    tag   = "success", icon = "fa-trophy",
                    title = $"{topRev.storeName} leads on revenue",
                    body  = $"€{topRev.revenue:N0} vs €{botRev.revenue:N0} for {botRev.storeName} — a {revGap}% gap. Investigate what drives {topRev.storeName}'s outperformance."
                });

                var topMargin = active.OrderByDescending(s => s.margin).First();
                var botMargin = active.OrderBy(s => s.margin).First();
                insights.Add(new {
                    tag   = "success", icon = "fa-percent",
                    title = $"{topMargin.storeName} is most profitable",
                    body  = $"{topMargin.margin:N1}% margin vs {botMargin.margin:N1}% at {botMargin.storeName}. Review cost structure and discount practices at lower-margin stores."
                });

                var topAov = active.Where(s => s.orders > 0).OrderByDescending(s => s.aov).FirstOrDefault();
                if (topAov != null)
                    insights.Add(new {
                        tag   = "info", icon = "fa-receipt",
                        title = $"{topAov.storeName} has the highest basket value",
                        body  = $"AOV of €{topAov.aov:N2} per order. Replicating its upsell and bundling approach could lift other stores."
                    });

                var topTurn = active.Where(s => s.invTurnover > 0).OrderByDescending(s => s.invTurnover).FirstOrDefault();
                if (topTurn != null)
                    insights.Add(new {
                        tag   = "success", icon = "fa-rotate",
                        title = $"{topTurn.storeName} has the best inventory turnover",
                        body  = $"{topTurn.invTurnover:N2}× — stock is moving efficiently. Use as the benchmark for replenishment cadence across all stores."
                    });

                var highRisk = storeDataList.OrderByDescending(s => s.lowStock + s.oos + s.deadStock).First();
                int riskTotal = highRisk.lowStock + highRisk.oos + highRisk.deadStock;
                if (riskTotal > 0)
                    insights.Add(new {
                        tag   = "warning", icon = "fa-triangle-exclamation",
                        title = $"{highRisk.storeName} has the highest inventory risk",
                        body  = $"{highRisk.oos} out-of-stock, {highRisk.lowStock} low-stock, {highRisk.deadStock} dead-stock SKUs. Immediate inventory review recommended."
                    });

                // Channel dominance difference insight
                var onlineDom   = active.Count(s => s.onlinePct   >= s.mobilePct && s.onlinePct   >= s.physicalPct);
                var physicalDom = active.Count(s => s.physicalPct >= s.onlinePct && s.physicalPct >= s.mobilePct);
                if (onlineDom > 0 && physicalDom > 0)
                    insights.Add(new {
                        tag   = "info", icon = "fa-store",
                        title = "Stores differ in channel strength",
                        body  = $"{onlineDom} store{(onlineDom == 1 ? "" : "s")} are online-led, {physicalDom} are physical-led. Cross-channel strategies may unlock additional revenue."
                    });
            }
            else
            {
                insights.Add(new {
                    tag   = "info", icon = "fa-circle-info",
                    title = "No sales data for the selected period",
                    body  = "Try a wider date range or verify that stores have synced data."
                });
            }

            var opts = new JsonSerializerOptions();
            ViewBag.StoreData     = JsonSerializer.Serialize(storeDataList, opts);
            ViewBag.StoreList     = JsonSerializer.Serialize(
                stores.Select(s => new { s.StoreId, s.StoreName }).ToList(), opts);
            ViewBag.SmartInsights = JsonSerializer.Serialize(insights.Take(6), opts);

            return View();
        }
    }
}
