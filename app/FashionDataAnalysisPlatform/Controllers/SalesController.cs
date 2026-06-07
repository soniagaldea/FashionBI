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
    public class SalesController : Controller
    {
        private readonly AppDbContext _context;

        public SalesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: Sales
        public async Task<IActionResult> Index(List<int>? storeIds, string? dateRange = "all")
        {
            storeIds  ??= new List<int>();
            dateRange ??= "all";

            ViewBag.SelectedStoreIds = storeIds;
            ViewBag.DateRange        = dateRange;

            // ── Date cut-off ─────────────────────────────────────────────────
            DateTime? cutoff = dateRange switch
            {
                "7d"  => DateTime.Today.AddDays(-7),
                "30d" => DateTime.Today.AddDays(-30),
                "90d" => DateTime.Today.AddDays(-90),
                "12m" => DateTime.Today.AddMonths(-12),
                _     => null
            };

            // ── Filtered query — no navigation props, no JOINs ───────────────
            var salesQuery = _context.Sales.AsNoTracking().AsQueryable();

            if (storeIds.Any())
                salesQuery = salesQuery.Where(s => s.StoreId.HasValue && storeIds.Contains(s.StoreId.Value));

            if (cutoff.HasValue)
                salesQuery = salesQuery.Where(s => s.SaleDate >= cutoff.Value);

            // ── Single in-memory fetch — all analytics computed from this ─────
            var rawSales = await salesQuery
                .Select(s => new
                {
                    s.SaleDate,
                    s.ProductId,
                    s.Revenue,
                    Profit   = s.Profit         ?? 0m,
                    s.Quantity,
                    Channel  = s.SalesChannel,
                    Discount = s.DiscountPercent,
                    OrderId  = s.ExternalOrderId,
                    s.Color,
                    s.Size
                })
                .ToListAsync();

            // ── Batch product lookup (EF Core emits WHERE 1=0 for empty list) ─
            var productIds = rawSales.Select(s => s.ProductId).Distinct().ToList();
            var productMap = await _context.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.ProductName, p.Category })
                .ToDictionaryAsync(p => p.ProductId);

            // ── KPIs ──────────────────────────────────────────────────────────
            decimal totalRevenue = rawSales.Sum(s => s.Revenue);
            int     totalUnits   = rawSales.Sum(s => s.Quantity);
            decimal grossProfit  = rawSales.Sum(s => s.Profit);
            int     totalOrders  = rawSales
                .Where(s => s.OrderId.HasValue)
                .Select(s => s.OrderId!.Value)
                .Distinct().Count();

            decimal averageOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0m;
            decimal profitMargin      = totalRevenue > 0 ? grossProfit  / totalRevenue * 100 : 0m;

            ViewBag.TotalRevenue      = totalRevenue;
            ViewBag.TotalOrders       = totalOrders;
            ViewBag.TotalUnits        = totalUnits;
            ViewBag.GrossProfit       = grossProfit;
            ViewBag.AverageOrderValue = averageOrderValue;
            ViewBag.ProfitMargin      = profitMargin;

            // ── Revenue Trend + AOV Trend (one grouping pass, two series) ────
            string[]  trendLabels;
            decimal[] trendRevenue;
            decimal[] aovValues;
            string    trendGranularity;

            if (dateRange == "7d" || dateRange == "30d")
            {
                string fmt     = dateRange == "7d" ? "ddd" : "dd MMM";
                var    grouped = rawSales
                    .GroupBy(s => s.SaleDate.Date)
                    .OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        decimal rev = g.Sum(x => x.Revenue);
                        int     oc  = g.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).Distinct().Count();
                        return new { Label = g.Key.ToString(fmt), Revenue = Math.Round(rev, 2), Aov = oc > 0 ? Math.Round(rev / oc, 2) : 0m };
                    }).ToList();
                trendLabels      = grouped.Select(x => x.Label).ToArray();
                trendRevenue     = grouped.Select(x => x.Revenue).ToArray();
                aovValues        = grouped.Select(x => x.Aov).ToArray();
                trendGranularity = "daily";
            }
            else if (dateRange == "90d")
            {
                var grouped = rawSales
                    .GroupBy(s =>
                    {
                        var d    = s.SaleDate.Date;
                        int diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                        return d.AddDays(-diff);
                    })
                    .OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        decimal rev = g.Sum(x => x.Revenue);
                        int     oc  = g.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).Distinct().Count();
                        return new { Label = g.Key.ToString("dd MMM"), Revenue = Math.Round(rev, 2), Aov = oc > 0 ? Math.Round(rev / oc, 2) : 0m };
                    }).ToList();
                trendLabels      = grouped.Select(x => x.Label).ToArray();
                trendRevenue     = grouped.Select(x => x.Revenue).ToArray();
                aovValues        = grouped.Select(x => x.Aov).ToArray();
                trendGranularity = "weekly";
            }
            else
            {
                string fmt     = dateRange == "12m" ? "MMM yyyy" : "MMM yy";
                var    grouped = rawSales
                    .GroupBy(s => new { s.SaleDate.Year, s.SaleDate.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g =>
                    {
                        decimal rev = g.Sum(x => x.Revenue);
                        int     oc  = g.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).Distinct().Count();
                        return new { Label = new DateTime(g.Key.Year, g.Key.Month, 1).ToString(fmt), Revenue = Math.Round(rev, 2), Aov = oc > 0 ? Math.Round(rev / oc, 2) : 0m };
                    }).ToList();
                trendLabels      = grouped.Select(x => x.Label).ToArray();
                trendRevenue     = grouped.Select(x => x.Revenue).ToArray();
                aovValues        = grouped.Select(x => x.Aov).ToArray();
                trendGranularity = "monthly";
            }

            ViewBag.TrendLabels      = JsonSerializer.Serialize(trendLabels);
            ViewBag.TrendRevenue     = JsonSerializer.Serialize(trendRevenue);
            ViewBag.AovValues        = JsonSerializer.Serialize(aovValues);
            ViewBag.TrendGranularity = trendGranularity;

            // ── Discount Efficiency by bands (revenue + profit margin per band) ─
            var discBandLabels  = new[] { "0%", "1–10%", "11–20%", "21–30%", "30%+" };
            var discBandCounts  = new int[5];
            var discBandRevenue = new decimal[5];
            var discBandProfit  = new decimal[5];

            foreach (var s in rawSales)
            {
                decimal d   = s.Discount ?? 0m;
                int     idx = d == 0m ? 0 : d <= 10m ? 1 : d <= 20m ? 2 : d <= 30m ? 3 : 4;
                discBandCounts[idx]++;
                discBandRevenue[idx] += s.Revenue;
                discBandProfit[idx]  += s.Profit;
            }

            var discBandMargin = discBandRevenue
                .Select((rev, i) => rev > 0 ? Math.Round(discBandProfit[i] / rev * 100, 1) : 0m)
                .ToArray();

            ViewBag.DiscountBandLabels  = JsonSerializer.Serialize(discBandLabels);
            ViewBag.DiscountBandCounts  = JsonSerializer.Serialize(discBandCounts);
            ViewBag.DiscountBandRevenue = JsonSerializer.Serialize(discBandRevenue.Select(v => Math.Round(v, 2)).ToArray());
            ViewBag.DiscountBandMargin  = JsonSerializer.Serialize(discBandMargin);

            // ── Top 5 products by revenue ─────────────────────────────────────
            var topProductsRaw = rawSales
                .GroupBy(s => s.ProductId)
                .Select(g =>
                {
                    productMap.TryGetValue(g.Key, out var prod);
                    decimal rev    = g.Sum(x => x.Revenue);
                    decimal profit = g.Sum(x => x.Profit);
                    return new
                    {
                        name     = prod?.ProductName ?? "Unknown",
                        category = prod?.Category    ?? "Unknown",
                        revenue  = Math.Round(rev, 2),
                        units    = g.Sum(x => x.Quantity),
                        margin   = rev > 0 ? Math.Round(profit / rev * 100, 1) : 0m
                    };
                })
                .OrderByDescending(x => x.revenue)
                .Take(5)
                .ToList();

            ViewBag.TopProducts = JsonSerializer.Serialize(topProductsRaw);

            // ── Color Performance (top 6 by revenue) ─────────────────────────
            var colorPerf = rawSales
                .Where(s => !string.IsNullOrWhiteSpace(s.Color))
                .GroupBy(s => s.Color!)
                .Select(g => new
                {
                    color   = g.Key,
                    units   = g.Sum(x => x.Quantity),
                    revenue = Math.Round(g.Sum(x => x.Revenue), 2)
                })
                .OrderByDescending(x => x.revenue)
                .Take(6)
                .ToList();

            ViewBag.ColorLabels  = JsonSerializer.Serialize(colorPerf.Select(x => x.color).ToArray());
            ViewBag.ColorUnits   = JsonSerializer.Serialize(colorPerf.Select(x => x.units).ToArray());
            ViewBag.ColorRevenue = JsonSerializer.Serialize(colorPerf.Select(x => x.revenue).ToArray());

            // ── Size Performance (standard sizes) ────────────────────────────
            var sizeOrder  = new[] { "XS", "S", "M", "L", "XL", "XXL" };
            var sizeLookup = rawSales
                .Where(s => !string.IsNullOrWhiteSpace(s.Size))
                .GroupBy(s => s.Size!.ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            var sizeUnits = sizeOrder
                .Select(s => sizeLookup.TryGetValue(s, out var v) ? v : 0)
                .ToArray();

            ViewBag.SizeLabels = JsonSerializer.Serialize(sizeOrder);
            ViewBag.SizeUnits  = JsonSerializer.Serialize(sizeUnits);

            // ── Channel Split Summary ─────────────────────────────────────────
            var channelSplit = rawSales
                .Where(s => !string.IsNullOrWhiteSpace(s.Channel))
                .GroupBy(s => s.Channel!)
                .Select(g =>
                {
                    decimal rev    = g.Sum(x => x.Revenue);
                    int     orders = g.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).Distinct().Count();
                    return new
                    {
                        channel = g.Key,
                        revenue = Math.Round(rev, 2),
                        share   = totalRevenue > 0 ? Math.Round(rev / totalRevenue * 100, 1) : 0m,
                        aov     = orders > 0 ? Math.Round(rev / orders, 2) : 0m
                    };
                })
                .OrderByDescending(x => x.revenue)
                .ToList();

            ViewBag.ChannelSplit = JsonSerializer.Serialize(channelSplit);

            // ── Category revenue (for insights) ──────────────────────────────
            var catRevenue = rawSales
                .GroupBy(s =>
                {
                    productMap.TryGetValue(s.ProductId, out var prod);
                    return prod?.Category ?? "Unknown";
                })
                .Select(g => new { Category = g.Key, Revenue = Math.Round(g.Sum(x => x.Revenue), 2) })
                .OrderByDescending(x => x.Revenue)
                .ToList();

            // ── Smart Insights ────────────────────────────────────────────────
            var insights = new List<object>();

            // 1. Revenue trend direction: compare first half vs second half of the period
            if (trendRevenue.Length >= 2)
            {
                int     half       = Math.Max(1, trendRevenue.Length / 2);
                decimal firstHalf  = trendRevenue.Take(half).Sum();
                decimal secondHalf = trendRevenue.Skip(trendRevenue.Length - half).Sum();
                decimal changePct  = firstHalf > 0 ? Math.Round((secondHalf - firstHalf) / firstHalf * 100, 1) : 0m;
                if (changePct > 5)
                    insights.Add(new { tag = "success", icon = "fa-arrow-trend-up",   title = $"Revenue growing (+{changePct}%)",    body = $"Second-half revenue outpaced first half by {changePct}%. Momentum is positive." });
                else if (changePct < -5)
                    insights.Add(new { tag = "warning", icon = "fa-arrow-trend-down", title = $"Revenue declining ({changePct}%)",    body = $"Second-half revenue fell {Math.Abs(changePct)}% vs first half. Review channel and product performance." });
                else
                    insights.Add(new { tag = "info",    icon = "fa-chart-line",       title = "Revenue stable across the period",     body = "Revenue is broadly flat. Consider targeted promotions or a new channel to accelerate growth." });
            }
            else if (totalRevenue > 0)
                insights.Add(new { tag = "info", icon = "fa-chart-line", title = $"€{totalRevenue:N0} total revenue", body = $"From {totalOrders:N0} orders in the selected period." });

            // 2. Best category
            var topCat = catRevenue.FirstOrDefault(x => x.Category != "Unknown");
            if (topCat != null)
            {
                decimal catPct = totalRevenue > 0 ? Math.Round((decimal)topCat.Revenue / totalRevenue * 100, 1) : 0m;
                insights.Add(new { tag = "info", icon = "fa-tag", title = $"{topCat.Category} drives {catPct}% of revenue", body = $"Top-performing category in the selected period. Prioritise stock depth and new arrivals here." });
            }

            // 3. Product concentration or best product
            if (topProductsRaw.Any())
            {
                decimal top5Rev = topProductsRaw.Sum(x => x.revenue);
                decimal concPct = totalRevenue > 0 ? Math.Round(top5Rev / totalRevenue * 100, 1) : 0m;
                var     topProd = topProductsRaw.First();
                decimal topPct  = totalRevenue > 0 ? Math.Round(topProd.revenue / totalRevenue * 100, 1) : 0m;
                if (concPct > 60)
                    insights.Add(new { tag = "warning", icon = "fa-triangle-exclamation", title = $"Top-5 SKUs = {concPct}% of revenue", body = $"High SKU concentration increases stock-out risk. Expanding the active product range can reduce dependency." });
                else
                    insights.Add(new { tag = "success", icon = "fa-shirt", title = $"{topProd.name} leads by revenue", body = $"{topProd.units:N0} units · {topPct}% revenue share · {topProd.margin.ToString("N1")}% margin." });
            }

            // 4. Discount efficiency: full-price margin vs combined 21%+ margin
            decimal baselineMargin   = discBandRevenue[0] > 0 ? Math.Round(discBandProfit[0] / discBandRevenue[0] * 100, 1) : profitMargin;
            decimal heavyRev         = discBandRevenue[3] + discBandRevenue[4];
            decimal heavyPrf         = discBandProfit[3]  + discBandProfit[4];
            decimal heavyMargin      = heavyRev > 0 ? Math.Round(heavyPrf / heavyRev * 100, 1) : baselineMargin;
            decimal marginGap        = baselineMargin - heavyMargin;

            if (heavyRev == 0)
                insights.Add(new { tag = "success", icon = "fa-percent", title = "No heavy discounting",             body = $"No orders with 21%+ discounts in this period. Full-price margin: {baselineMargin.ToString("N1")}%." });
            else if (marginGap > 12)
                insights.Add(new { tag = "warning", icon = "fa-percent", title = $"Discounts cost {marginGap.ToString("N1")}pp of margin", body = $"Orders at 21%+ off average {heavyMargin.ToString("N1")}% margin vs {baselineMargin.ToString("N1")}% at full price. Review discount ceilings." });
            else
                insights.Add(new { tag = "success", icon = "fa-percent", title = "Discounts are well-managed",      body = $"Margin gap between full-price ({baselineMargin.ToString("N1")}%) and heavily-discounted ({heavyMargin.ToString("N1")}%) is within acceptable range." });

            // 5. AOV trend direction: compare first third vs last third
            if (aovValues.Length >= 3)
            {
                int     third     = Math.Max(1, aovValues.Length / 3);
                decimal firstAvg  = aovValues.Take(third).Average();
                decimal lastAvg   = aovValues.Skip(aovValues.Length - third).Average();
                decimal aovChange = firstAvg > 0 ? Math.Round((lastAvg - firstAvg) / firstAvg * 100, 1) : 0m;
                if (aovChange > 5)
                    insights.Add(new { tag = "success", icon = "fa-receipt", title = $"AOV trending up (+{aovChange}%)",  body = $"Customers are spending more per order. Upsells and bundles are delivering results." });
                else if (aovChange < -5)
                    insights.Add(new { tag = "warning", icon = "fa-receipt", title = $"AOV declining ({aovChange}%)",     body = $"Average basket value is shrinking. Review minimum-spend incentives and cross-sell prompts." });
                else
                    insights.Add(new { tag = "info",    icon = "fa-receipt", title = $"AOV steady at €{averageOrderValue:N2}", body = $"Basket value is stable. A free-shipping threshold or bundle offer could lift AOV." });
            }
            else if (averageOrderValue > 0)
                insights.Add(new { tag = "info", icon = "fa-receipt", title = $"AOV: €{averageOrderValue:N2}", body = $"Based on {totalOrders:N0} orders. Bundles and upsells can grow this metric." });

            // 6. Strongest channel
            var topChannel = channelSplit.FirstOrDefault();
            if (topChannel != null)
                insights.Add(new { tag = "primary", icon = "fa-store", title = $"{topChannel.channel} is your strongest channel", body = $"{topChannel.share}% of revenue · AOV €{topChannel.aov:N2}. Prioritise investment here." });

            ViewBag.SmartInsights = JsonSerializer.Serialize(insights.Take(6));

            return View();
        }

        // GET: Sales/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var sale = await _context.Sales
                .Include(s => s.Product)
                .FirstOrDefaultAsync(m => m.SaleId == id);
            if (sale == null)
            {
                return NotFound();
            }

            return View(sale);
        }

        // GET: Sales/Create
        public IActionResult Create()
        {
            ViewData["ProductId"] = new SelectList(_context.Products, "ProductId", "ProductName");
            return View();
        }

        // POST: Sales/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("SaleId,ProductId,SaleDate,Quantity")] Sale sale)
        {
            var product = await _context.Products.FindAsync(sale.ProductId);

            if (product == null)
            {
                ModelState.AddModelError("ProductId", "Selected product is invalid.");
            }

            if (ModelState.IsValid)
            {
                sale.UnitPrice = product!.UnitPrice;
                sale.Revenue = sale.UnitPrice * sale.Quantity;

                _context.Add(sale);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            ViewData["ProductId"] = new SelectList(_context.Products, "ProductId", "ProductName", sale.ProductId);
            return View(sale);
        }

        // GET: Sales/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var sale = await _context.Sales.FindAsync(id);
            if (sale == null)
            {
                return NotFound();
            }
            ViewData["ProductId"] = new SelectList(_context.Products, "ProductId", "ProductName", sale.ProductId);
            return View(sale);
        }

        // POST: Sales/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("SaleId,ProductId,SaleDate,Quantity")] Sale sale)
        {
            if (id != sale.SaleId)
            {
                return NotFound();
            }

            var existingSale = await _context.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.SaleId == id);
            var product = await _context.Products.FindAsync(sale.ProductId);

            if (existingSale == null)
            {
                return NotFound();
            }

            if (product == null)
            {
                ModelState.AddModelError("ProductId", "Selected product is invalid.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    sale.UnitPrice = product!.UnitPrice;
                    sale.Revenue = sale.UnitPrice * sale.Quantity;

                    _context.Update(sale);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SaleExists(sale.SaleId))
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

            ViewData["ProductId"] = new SelectList(_context.Products, "ProductId", "ProductName", sale.ProductId);
            return View(sale);
        }

        // GET: Sales/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var sale = await _context.Sales
                .Include(s => s.Product)
                .FirstOrDefaultAsync(m => m.SaleId == id);
            if (sale == null)
            {
                return NotFound();
            }

            return View(sale);
        }

        // POST: Sales/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var sale = await _context.Sales.FindAsync(id);
            if (sale != null)
            {
                _context.Sales.Remove(sale);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool SaleExists(int id)
        {
            return _context.Sales.Any(e => e.SaleId == id);
        }
    }
}
