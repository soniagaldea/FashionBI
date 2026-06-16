using FashionDataAnalysisPlatform.Data;
using FashionDataAnalysisPlatform.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FashionDataAnalysisPlatform.Services
{
    public class SmartInsightsService
    {
        private readonly AppDbContext _context;
        public SmartInsightsService(AppDbContext context) => _context = context;

        // ── Flat projection types used in-memory only ──────────────────────
        private sealed record RSale(int ProductId, int StoreId, DateTime SaleDate,
            decimal Revenue, decimal Profit, int Quantity, string? Channel, int? OrderId);
        private sealed record RProd(int ProductId, string Name, string Code,
            string Category, decimal UnitPrice);
        private sealed record RInv(int ProductId, int StoreId, int Stock, int MinThreshold);

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC ENTRY POINT
        // ════════════════════════════════════════════════════════════════════
        public async Task<SmartInsightsViewModel> BuildViewModelAsync(List<int> storeIds)
        {
            var vm = new SmartInsightsViewModel { LastRefreshedAt = DateTime.Now };

            // Fixed 24-month window — long enough for SPC baselines and ABC/XYZ classification
            var cutoff = DateTime.Today.AddMonths(-24);

            // ── Sales query — optional store filter ───────────────────────
            var salesQuery = _context.Sales.AsNoTracking()
                .Where(s => s.SaleDate >= cutoff && s.StoreId != null);

            if (storeIds.Any())
                salesQuery = salesQuery.Where(s => storeIds.Contains(s.StoreId!.Value));

            var sales = await salesQuery
                .Select(s => new RSale(
                    s.ProductId, s.StoreId!.Value, s.SaleDate,
                    s.Revenue, s.Profit ?? 0m, s.Quantity,
                    s.SalesChannel, s.ExternalOrderId))
                .ToListAsync();

            if (!sales.Any()) return vm;
            vm.HasSalesData = true;

            var products = (await _context.Products.AsNoTracking()
                .Select(p => new RProd(p.ProductId, p.ProductName, p.ProductCode,
                    p.Category, p.UnitPrice))
                .ToListAsync())
                .ToDictionary(p => p.ProductId);

            // Inventories — filtered to selected stores
            var invQuery = _context.Inventories.AsNoTracking()
                .Where(i => i.StoreId != null);
            if (storeIds.Any())
                invQuery = invQuery.Where(i => storeIds.Contains(i.StoreId!.Value));
            var inventories = await invQuery
                .Select(i => new RInv(i.ProductId, i.StoreId!.Value,
                    i.CurrentStock, i.MinimumStockThreshold))
                .ToListAsync();

            // Store map — filtered to selected stores
            var storeQuery = _context.Stores.AsNoTracking().AsQueryable();
            if (storeIds.Any())
                storeQuery = storeQuery.Where(s => storeIds.Contains(s.StoreId));
            var storeMap = await storeQuery
                .ToDictionaryAsync(s => s.StoreId, s => s.StoreName);

            // Latest forecast run
            var latestForecastAt = await _context.ForecastResults
                .MaxAsync(f => (DateTime?)f.GeneratedAt);

            List<(string StoreName, string Category, decimal RevForecast, int UnitsForecast)> forecasts = new();
            decimal forecastAccuracy = 0m;

            if (latestForecastAt.HasValue)
            {
                vm.HasForecastData = true;
                forecasts = await _context.ForecastResults.AsNoTracking()
                    .Where(f => f.GeneratedAt == latestForecastAt.Value)
                    .Select(f => ValueTuple.Create(f.StoreName, f.Category,
                        f.RevenueForecast, f.UnitsForecast))
                    .ToListAsync();

                var revAcc = await _context.ForecastAccuracies.AsNoTracking()
                    .Where(a => a.Target == "Revenue")
                    .OrderBy(a => a.MAPE)
                    .FirstOrDefaultAsync();
                forecastAccuracy = revAcc?.AccuracyPercent ?? 50m;
            }

            // ── Build all four sections ───────────────────────────────────
            BuildHealthScore(vm, sales, inventories, forecastAccuracy);
            BuildStrategicActions(vm, sales, inventories, products, forecasts,
                forecastAccuracy, storeMap);

            // Aggregate financial impact from the action board
            vm.TotalRevenueAtRisk     = vm.UrgentActions.Sum(a => a.EstimatedImpact);
            vm.TotalRevenueUpside     = vm.OpportunityActions.Sum(a => a.EstimatedImpact);
            vm.TotalOverstockRecovery = vm.OptimizeActions
                .Where(a => a.Icon == "fa-boxes-stacked")
                .Sum(a => a.EstimatedImpact);

            BuildKpiExceptions(vm, sales, storeMap, products);
            BuildAbcXyz(vm, sales, products, inventories);

            return vm;
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION 1 — BUSINESS HEALTH SCORE
        // ════════════════════════════════════════════════════════════════════
        private static void BuildHealthScore(
            SmartInsightsViewModel vm,
            List<RSale> sales,
            List<RInv> inventories,
            decimal forecastAccuracy)
        {
            var mostRecent        = sales.Max(s => s.SaleDate);
            var currentMonthStart = new DateTime(mostRecent.Year, mostRecent.Month, 1);
            var prev12Start       = currentMonthStart.AddMonths(-12);
            var prev3Start        = currentMonthStart.AddMonths(-3);

            // ── Revenue score (0–25) ────────────────────────────────────
            var historicMonthlyRevenues = sales
                .Where(s => s.SaleDate >= prev12Start && s.SaleDate < currentMonthStart)
                .GroupBy(s => new { s.SaleDate.Year, s.SaleDate.Month })
                .Select(g => g.Sum(x => x.Revenue))
                .ToList();

            decimal currentRevenue = sales
                .Where(s => s.SaleDate >= currentMonthStart)
                .Sum(s => s.Revenue);

            decimal avgMonthly = historicMonthlyRevenues.Any()
                ? historicMonthlyRevenues.Average() : 1m;

            double revRatio = avgMonthly > 0 ? (double)(currentRevenue / avgMonthly) : 1.0;
            int    revScore = (int)Math.Min(25, revRatio * 20.0);

            string revLabel  = revRatio >= 1.1 ? "Above Trend" : revRatio >= 0.85 ? "On Track" : "Below Trend";
            string revDetail = $"Current month: €{currentRevenue:N0} vs €{avgMonthly:N0} 12-month avg";

            // ── Profitability score (0–25) ──────────────────────────────
            var last3m   = sales.Where(s => s.SaleDate >= prev3Start).ToList();
            decimal rev3 = last3m.Sum(s => s.Revenue);
            decimal prf3 = last3m.Sum(s => s.Profit);
            decimal margin = rev3 > 0 ? prf3 / rev3 * 100m : 0m;

            int    profScore  = (int)Math.Min(25, (double)margin / 25.0 * 25.0);
            string profLabel  = margin >= 25m ? "Strong" : margin >= 15m ? "Adequate" : "Weak";
            string profDetail = $"3-month profit margin: {margin:N1}% (benchmark: 25%)";

            // ── Inventory score (0–25) ──────────────────────────────────
            int total    = inventories.Count;
            int healthy  = inventories.Count(i => i.Stock > i.MinThreshold);
            int oos      = inventories.Count(i => i.Stock == 0);
            int invScore = total > 0 ? (int)Math.Round((double)healthy / total * 25.0) : 12;

            string invLabel  = invScore >= 20 ? "Healthy" : invScore >= 12 ? "Monitor" : "At Risk";
            string invDetail = $"{healthy}/{total} SKUs above threshold · {oos} out of stock";

            // ── Forecast score (0–25) ───────────────────────────────────
            int    fcastScore  = forecastAccuracy > 0
                ? (int)Math.Min(25, (double)forecastAccuracy / 100.0 * 25.0) : 12;
            string fcastLabel  = forecastAccuracy >= 70 ? "High Confidence"
                               : forecastAccuracy >= 50 ? "Medium Confidence"
                               : forecastAccuracy > 0   ? "Low Confidence"
                               : "No Data";
            string fcastDetail = forecastAccuracy > 0
                ? $"Best model accuracy: {forecastAccuracy:N1}%"
                : "No forecasts generated yet — run the Forecasting module";

            // ── Composite ──────────────────────────────────────────────
            int score = revScore + profScore + invScore + fcastScore;

            vm.RevenueScore       = revScore;
            vm.ProfitabilityScore = profScore;
            vm.InventoryScore     = invScore;
            vm.ForecastScore      = fcastScore;

            vm.RevenueScoreLabel       = revLabel;
            vm.ProfitabilityScoreLabel = profLabel;
            vm.InventoryScoreLabel     = invLabel;
            vm.ForecastScoreLabel      = fcastLabel;

            vm.RevenueScoreDetail       = revDetail;
            vm.ProfitabilityScoreDetail = profDetail;
            vm.InventoryScoreDetail     = invDetail;
            vm.ForecastScoreDetail      = fcastDetail;

            vm.HealthScore      = score;
            vm.HealthStatus     = score >= 70 ? "Healthy" : score >= 45 ? "Caution" : "At Risk";
            vm.HealthColorClass = score >= 70 ? "emerald" : score >= 45 ? "amber" : "red";

            var weak = new List<string>();
            if (revScore   < 14) weak.Add("revenue below trend");
            if (profScore  < 12) weak.Add("margin pressure");
            if (invScore   < 12) weak.Add("inventory health issues");
            if (fcastScore < 10) weak.Add("low forecast confidence");

            vm.HealthExplanation = weak.Any()
                ? $"Score is weighed down by: {string.Join(", ", weak)}. " +
                  "Review the Strategic Action Board below for specific next steps."
                : "All four pillars are performing at or above target. " +
                  "Continue monitoring for early signals of change.";
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION 2 — STRATEGIC ACTION BOARD
        // ════════════════════════════════════════════════════════════════════
        private static void BuildStrategicActions(
            SmartInsightsViewModel vm,
            List<RSale> sales,
            List<RInv> inventories,
            Dictionary<int, RProd> products,
            List<(string StoreName, string Category, decimal RevForecast, int UnitsForecast)> forecasts,
            decimal forecastAccuracy,
            Dictionary<int, string> storeMap)
        {
            var today     = sales.Max(s => s.SaleDate);
            var cur3Start = new DateTime(today.Year, today.Month, 1).AddMonths(-3);
            var prv3Start = cur3Start.AddMonths(-3);

            // ── Pre-compute category metrics (single pass each) ─────────
            var catCur = sales
                .Where(s => s.SaleDate >= cur3Start && products.ContainsKey(s.ProductId))
                .GroupBy(s => products[s.ProductId].Category)
                .ToDictionary(g => g.Key, g => new {
                    Revenue = g.Sum(x => x.Revenue),
                    Profit  = g.Sum(x => x.Profit)
                });

            var catPrev = sales
                .Where(s => s.SaleDate >= prv3Start && s.SaleDate < cur3Start
                         && products.ContainsKey(s.ProductId))
                .GroupBy(s => products[s.ProductId].Category)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Revenue));

            decimal networkRevCur  = catCur.Values.Sum(x => x.Revenue);
            decimal networkProfCur = catCur.Values.Sum(x => x.Profit);
            decimal networkMargin  = networkRevCur > 0 ? networkProfCur / networkRevCur * 100m : 0m;

            // ── Inventory per (storeId, category) ──────────────────────
            var avgPriceByCat = products.Values
                .GroupBy(p => p.Category)
                .ToDictionary(g => g.Key, g => g.Average(p => p.UnitPrice));

            var invByStCat = inventories
                .Where(i => products.ContainsKey(i.ProductId))
                .GroupBy(i => (i.StoreId, Cat: products[i.ProductId].Category))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Stock));

            var storeNameToId = storeMap.ToDictionary(kv => kv.Value, kv => kv.Key,
                StringComparer.OrdinalIgnoreCase);

            var urgent      = new List<StrategicAction>();
            var opportunity = new List<StrategicAction>();
            var optimize    = new List<StrategicAction>();
            var monitor     = new List<StrategicAction>();

            var urgentCombos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── URGENT 1: Inventory gaps from forecast ──────────────────
            if (forecasts.Any())
            {
                var fByCombo = forecasts
                    .GroupBy(f => (f.StoreName, f.Category))
                    .Select(g => (
                        g.Key.StoreName, g.Key.Category,
                        Units:   g.Sum(x => x.UnitsForecast),
                        Revenue: g.Sum(x => x.RevForecast)))
                    .OrderByDescending(x => x.Revenue)
                    .ToList();

                foreach (var f in fByCombo)
                {
                    if (!storeNameToId.TryGetValue(f.StoreName, out var sid)) continue;
                    invByStCat.TryGetValue((sid, f.Category), out var stock);

                    if (f.Units <= stock) continue;

                    int     gap    = f.Units - stock;
                    decimal price  = avgPriceByCat.GetValueOrDefault(f.Category, 50m);
                    decimal conf   = forecastAccuracy > 0 ? forecastAccuracy : 70m;
                    decimal impact = gap * price * (conf / 100m);

                    if (impact < 150m) continue;

                    decimal coverage = f.Units > 0
                        ? Math.Round((decimal)stock / (f.Units / 3m), 1) : 0m;

                    urgent.Add(new StrategicAction {
                        ActionCategory  = "Urgent",
                        Icon            = "fa-triangle-exclamation",
                        Title           = $"Restock {f.Category} at {f.StoreName}",
                        Explanation     = $"Forecasted 3-month demand is {f.Units:N0} units but only {stock:N0} units are in stock " +
                                          $"({coverage:N1} months of coverage). The {gap:N0}-unit shortfall puts " +
                                          $"€{impact:N0} of forecasted revenue directly at risk.",
                        SuggestedAction = $"Place a purchase order for a minimum of {gap:N0} units of {f.Category} " +
                                          $"at {f.StoreName}. Prioritise fulfilment before the next replenishment cycle.",
                        EstimatedImpact = Math.Round(impact, 0),
                        ImpactLabel     = "Revenue at Risk",
                        Store           = f.StoreName,
                        ProductCategory = f.Category,
                        DataBadge       = "Forecasting · Inventory"
                    });
                    urgentCombos.Add($"{f.StoreName}|{f.Category}");
                }
            }

            // ── URGENT 2: OOS with recent sales velocity ────────────────
            var recentCutoff = today.AddDays(-30);
            var recentByProd = sales
                .Where(s => s.SaleDate >= recentCutoff)
                .GroupBy(s => s.ProductId)
                .ToDictionary(g => g.Key, g => (
                    Units:   g.Sum(x => x.Quantity),
                    Revenue: g.Sum(x => x.Revenue)));

            var oosWithVelocity = inventories
                .Where(i => i.Stock == 0 && recentByProd.ContainsKey(i.ProductId))
                .GroupBy(i => i.ProductId)
                .Take(4);

            foreach (var grp in oosWithVelocity)
            {
                if (!products.TryGetValue(grp.Key, out var prod)) continue;
                var recent = recentByProd[grp.Key];
                if (recent.Revenue < 200m) continue;

                string storeName = storeMap.TryGetValue(grp.First().StoreId, out var sn) ? sn : "store";

                urgent.Add(new StrategicAction {
                    ActionCategory  = "Urgent",
                    Icon            = "fa-circle-xmark",
                    Title           = $"{prod.Name} — Out of Stock",
                    Explanation     = $"{prod.Name} sold {recent.Units:N0} units (€{recent.Revenue:N0}) in the last 30 days " +
                                      $"but is now fully out of stock at {storeName}. Every day without stock is lost revenue.",
                    SuggestedAction = $"Urgently replenish {prod.Name} (SKU: {prod.Code}). " +
                                      $"Estimated monthly revenue cost of this stockout: €{recent.Revenue:N0}.",
                    EstimatedImpact = Math.Round(recent.Revenue, 0),
                    ImpactLabel     = "Monthly Revenue Lost",
                    Store           = storeName,
                    ProductCategory = prod.Category,
                    DataBadge       = "Sales · Inventory"
                });
            }

            // ── OPPORTUNITY 1: High forecast growth + adequate stock ─────
            if (forecasts.Any())
            {
                var fRevByCombo = forecasts
                    .GroupBy(f => (f.StoreName, f.Category))
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.RevForecast));

                foreach (var kvp in fRevByCombo.OrderByDescending(x => x.Value))
                {
                    if (urgentCombos.Contains($"{kvp.Key.StoreName}|{kvp.Key.Category}")) continue;
                    if (!storeNameToId.TryGetValue(kvp.Key.StoreName, out var sid)) continue;

                    // Find prior 3-month actual for this store+category
                    var prevRev = sales
                        .Where(s => s.SaleDate >= cur3Start && s.SaleDate < cur3Start.AddMonths(3)
                                 && s.StoreId == sid && products.ContainsKey(s.ProductId)
                                 && products[s.ProductId].Category == kvp.Key.Category)
                        .Sum(s => s.Revenue);

                    if (prevRev <= 0) continue;
                    double growth = (double)((kvp.Value - prevRev) / prevRev * 100m);
                    if (growth < 8.0) continue;

                    decimal impact = kvp.Value - prevRev;
                    if (impact < 300m) continue;

                    opportunity.Add(new StrategicAction {
                        ActionCategory  = "Opportunity",
                        Icon            = "fa-arrow-trend-up",
                        Title           = $"Scale Up {kvp.Key.Category} at {kvp.Key.StoreName}",
                        Explanation     = $"Demand for {kvp.Key.Category} at {kvp.Key.StoreName} is forecast to grow {growth:N0}% " +
                                          $"next quarter (€{prevRev:N0} → €{kvp.Value:N0}). " +
                                          $"Current inventory is adequate — this is a growth window.",
                        SuggestedAction = $"Increase purchase allocation for {kvp.Key.Category} at {kvp.Key.StoreName}. " +
                                          $"Capturing this forecast fully represents €{impact:N0} in additional quarterly revenue.",
                        EstimatedImpact = Math.Round(impact, 0),
                        ImpactLabel     = "Revenue Upside",
                        Store           = kvp.Key.StoreName,
                        ProductCategory = kvp.Key.Category,
                        DataBadge       = "Forecasting · Sales"
                    });
                }
            }

            // ── OPPORTUNITY 2: Fastest-growing category (actual trend) ───
            foreach (var cat in catCur.Keys.OrderByDescending(c => catCur[c].Revenue))
            {
                if (!catPrev.TryGetValue(cat, out var prev) || prev <= 0) continue;
                decimal cur = catCur[cat].Revenue;
                double growth = (double)((cur - prev) / prev * 100m);
                if (growth < 12.0) continue;
                if (opportunity.Any(o => o.ProductCategory == cat)) continue;
                decimal impact = cur - prev;
                if (impact < 400m) continue;

                opportunity.Add(new StrategicAction {
                    ActionCategory  = "Opportunity",
                    Icon            = "fa-fire",
                    Title           = $"{cat} Is the Fastest-Growing Category",
                    Explanation     = $"{cat} revenue grew {growth:N0}% quarter-over-quarter (€{prev:N0} → €{cur:N0}). " +
                                      $"This is the strongest short-term demand signal across the entire product portfolio.",
                    SuggestedAction = $"Prioritise new arrivals and wider ranging for {cat}. " +
                                      $"Review open-to-buy allocations and ensure stock depth matches this growth trajectory.",
                    EstimatedImpact = Math.Round(impact, 0),
                    ImpactLabel     = "Growth vs Prior Quarter",
                    ProductCategory = cat,
                    DataBadge       = "Sales"
                });
                break;
            }

            // ── OPPORTUNITY 3: High-AOV underweighted channel ───────────
            var chanMetrics = sales
                .Where(s => s.SaleDate >= cur3Start && !string.IsNullOrEmpty(s.Channel))
                .GroupBy(s => s.Channel!)
                .Select(g => (
                    Channel: g.Key,
                    Revenue: g.Sum(x => x.Revenue),
                    Orders:  g.Where(x => x.OrderId.HasValue).Select(x => x.OrderId!.Value).Distinct().Count()))
                .Where(c => c.Orders > 0)
                .ToList();

            decimal totalChanRev = chanMetrics.Sum(c => c.Revenue);
            decimal netAov       = chanMetrics.Sum(c => c.Revenue) /
                                   Math.Max(1, chanMetrics.Sum(c => c.Orders));

            foreach (var ch in chanMetrics.OrderByDescending(c => c.Revenue / c.Orders))
            {
                decimal aov   = ch.Revenue / ch.Orders;
                decimal share = totalChanRev > 0 ? ch.Revenue / totalChanRev * 100m : 0m;
                if (aov <= netAov * 1.12m || share >= 30m || ch.Revenue < 200m) continue;

                decimal potentialRev = totalChanRev * 0.30m - ch.Revenue;
                if (potentialRev < 300m) break;

                opportunity.Add(new StrategicAction {
                    ActionCategory  = "Opportunity",
                    Icon            = "fa-store",
                    Title           = $"Grow {ch.Channel} Channel — Highest AOV",
                    Explanation     = $"The {ch.Channel} channel generates an AOV of €{aov:N2}, " +
                                      $"{(aov / netAov - 1) * 100:N0}% above the network average of €{netAov:N2}, " +
                                      $"yet accounts for only {share:N1}% of revenue.",
                    SuggestedAction = $"Invest in {ch.Channel} channel marketing and experience improvements. " +
                                      $"Growing its revenue share to 30% could unlock ~€{potentialRev:N0} in additional quarterly revenue.",
                    EstimatedImpact = Math.Round(potentialRev, 0),
                    ImpactLabel     = "Revenue Potential to 30% Share",
                    DataBadge       = "Sales"
                });
                break;
            }

            // ── OPTIMIZE 1: Overstock ────────────────────────────────────
            if (forecasts.Any())
            {
                var fUnitsByCombo = forecasts
                    .GroupBy(f => (f.StoreName, f.Category))
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.UnitsForecast));

                foreach (var kvp in fUnitsByCombo.OrderByDescending(x => x.Value))
                {
                    if (!storeNameToId.TryGetValue(kvp.Key.StoreName, out var sid)) continue;
                    invByStCat.TryGetValue((sid, kvp.Key.Category), out var stock);

                    if (stock <= kvp.Value * 2 || stock <= 30) continue;

                    int     excess   = stock - kvp.Value;
                    decimal price    = avgPriceByCat.GetValueOrDefault(kvp.Key.Category, 50m);
                    decimal recovery = excess * price * 0.35m;
                    if (recovery < 400m) continue;

                    optimize.Add(new StrategicAction {
                        ActionCategory  = "Optimize",
                        Icon            = "fa-boxes-stacked",
                        Title           = $"Reduce Overstock — {kvp.Key.Category} at {kvp.Key.StoreName}",
                        Explanation     = $"Current stock of {stock:N0} units exceeds 3-month forecasted demand of " +
                                          $"{kvp.Value:N0} units by more than 2×. Excess inventory ties up capital " +
                                          $"and increases markdown risk as the season progresses.",
                        SuggestedAction = $"Run a targeted promotion or clearance event for {kvp.Key.Category} at " +
                                          $"{kvp.Key.StoreName} to move {excess:N0} excess units. " +
                                          $"Estimated recovery at 35% markdown: €{recovery:N0}.",
                        EstimatedImpact = Math.Round(recovery, 0),
                        ImpactLabel     = "Markdown Recovery Value",
                        Store           = kvp.Key.StoreName,
                        ProductCategory = kvp.Key.Category,
                        DataBadge       = "Inventory · Forecasting"
                    });
                }
            }

            // ── OPTIMIZE 2: Margin erosion by category ───────────────────
            foreach (var cat in catCur.Keys)
            {
                var d = catCur[cat];
                if (d.Revenue < 500m) continue;
                decimal margin = d.Revenue > 0 ? d.Profit / d.Revenue * 100m : 0m;
                if (margin >= networkMargin - 8m) continue;
                if (optimize.Any(o => o.ProductCategory == cat && o.Icon == "fa-percent")) continue;

                decimal marginGap    = networkMargin - margin;
                decimal recoverValue = d.Revenue * (marginGap / 2m) / 100m;
                if (recoverValue < 200m) continue;

                optimize.Add(new StrategicAction {
                    ActionCategory  = "Optimize",
                    Icon            = "fa-percent",
                    Title           = $"Address Margin Erosion in {cat}",
                    Explanation     = $"{cat} is running at a {margin:N1}% profit margin, {marginGap:N1} percentage points " +
                                      $"below the {networkMargin:N1}% network average. This suggests discount depth, " +
                                      $"cost structure, or pricing issues specific to this category.",
                    SuggestedAction = $"Review discount ceilings, supplier costs, and pricing strategy for {cat}. " +
                                      $"Recovering half the margin gap represents €{recoverValue:N0} in additional profit.",
                    EstimatedImpact = Math.Round(recoverValue, 0),
                    ImpactLabel     = "Profit Recovery (50% of gap)",
                    ProductCategory = cat,
                    DataBadge       = "Sales"
                });
            }

            // ── MONITOR 1: Declining category trend ─────────────────────
            foreach (var cat in catCur.Keys.OrderByDescending(c => catCur[c].Revenue))
            {
                if (!catPrev.TryGetValue(cat, out var prev) || prev <= 0) continue;
                decimal cur    = catCur[cat].Revenue;
                double decline = (double)((cur - prev) / prev * 100m);
                if (decline >= -5.0 || decline <= -25.0) continue;
                if (cur < 300m) continue;

                monitor.Add(new StrategicAction {
                    ActionCategory  = "Monitor",
                    Icon            = "fa-arrow-trend-down",
                    Title           = $"Declining Trend in {cat}",
                    Explanation     = $"{cat} revenue declined {Math.Abs(decline):N0}% quarter-over-quarter " +
                                      $"(€{prev:N0} → €{cur:N0}). The decline is not yet at a critical threshold " +
                                      $"but warrants close monitoring before it deepens.",
                    SuggestedAction = $"Review product range, pricing, and customer feedback for {cat}. " +
                                      $"Monitor sell-through rates weekly and adjust promotional cadence if the trend continues.",
                    EstimatedImpact = Math.Round(prev - cur, 0),
                    ImpactLabel     = "Revenue Shortfall vs Prior Quarter",
                    ProductCategory = cat,
                    DataBadge       = "Sales"
                });
            }

            // ── MONITOR 2: Low-stock below threshold (not OOS) ──────────
            int warnSkus = inventories.Count(i => i.Stock > 0 && i.Stock <= i.MinThreshold);
            if (warnSkus > 0)
            {
                monitor.Add(new StrategicAction {
                    ActionCategory  = "Monitor",
                    Icon            = "fa-gauge-high",
                    Title           = $"{warnSkus} SKUs Below Minimum Stock Threshold",
                    Explanation     = $"{warnSkus} products across all stores are at or below their minimum stock threshold " +
                                      $"but not yet out of stock. Without replenishment, these will become stockouts " +
                                      $"before the next regular replenishment cycle.",
                    SuggestedAction = "Run an inventory review and initiate replenishment orders for all products " +
                                      "currently at or below their minimum threshold. Use the Inventory Intelligence module for the full list.",
                    EstimatedImpact = 0,
                    ImpactLabel     = "Preventive Action",
                    DataBadge       = "Inventory"
                });
            }

            // ── Sort each category by impact descending and cap ─────────
            vm.UrgentActions      = urgent.OrderByDescending(a => a.EstimatedImpact).Take(6).ToList();
            vm.OpportunityActions = opportunity.OrderByDescending(a => a.EstimatedImpact).Take(6).ToList();
            vm.OptimizeActions    = optimize.OrderByDescending(a => a.EstimatedImpact).Take(6).ToList();
            vm.MonitorActions     = monitor.OrderByDescending(a => a.EstimatedImpact).Take(6).ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION 3 — KPI EXCEPTION DETECTION
        // ════════════════════════════════════════════════════════════════════
        private static void BuildKpiExceptions(
            SmartInsightsViewModel vm,
            List<RSale> sales,
            Dictionary<int, string> storeMap,
            Dictionary<int, RProd> products)
        {
            var mostRecent    = sales.Max(s => s.SaleDate);
            var currentMonth  = new DateTime(mostRecent.Year, mostRecent.Month, 1);
            var windowStart   = currentMonth.AddMonths(-12);
            var windowSales   = sales.Where(s => s.SaleDate >= windowStart).ToList();

            var exceptions = new List<KpiException>();

            // ── Pre-aggregate: (storeId, year, month) → revenue ────────
            var storeMthRev = windowSales
                .GroupBy(s => (s.StoreId, s.SaleDate.Year, s.SaleDate.Month))
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Revenue));

            // ── Pre-aggregate: (category, year, month) → revenue + profit
            var catMthData = windowSales
                .Where(s => products.ContainsKey(s.ProductId))
                .GroupBy(s => (Cat: products[s.ProductId].Category,
                               s.SaleDate.Year, s.SaleDate.Month))
                .ToDictionary(g => g.Key, g => (
                    Revenue: g.Sum(x => x.Revenue),
                    Profit:  g.Sum(x => x.Profit)));

            // ── Revenue anomaly by store ────────────────────────────────
            foreach (var (storeId, storeName) in storeMap)
            {
                var baseline = Enumerable.Range(1, 11)
                    .Select(i => { var m = currentMonth.AddMonths(-i); return storeMthRev.GetValueOrDefault((storeId, m.Year, m.Month)); })
                    .Where(v => v > 0)
                    .ToList();

                if (baseline.Count < 4) continue;

                decimal mean   = baseline.Average();
                double  stddev = StdDev(baseline.Select(v => (double)v));
                if (stddev < 1.0 || mean < 200m) continue;

                decimal actual = storeMthRev.GetValueOrDefault((storeId, currentMonth.Year, currentMonth.Month));
                double  sigma  = ((double)actual - (double)mean) / stddev;
                if (Math.Abs(sigma) < 1.5) continue;

                decimal devPct = mean > 0 ? (actual - mean) / mean * 100m : 0m;
                bool    isNeg  = sigma < 0;
                string  sev    = Math.Abs(sigma) >= 2.0 ? (isNeg ? "Critical" : "Positive") : (isNeg ? "Warning" : "Positive");

                exceptions.Add(new KpiException {
                    MetricName     = "Revenue",
                    Dimension      = storeName,
                    DimensionType  = "Store",
                    ExpectedValue  = Math.Round(mean,   0),
                    ActualValue    = Math.Round(actual, 0),
                    DeviationPct   = Math.Round(devPct, 1),
                    DeviationSigma = Math.Round(sigma,  2),
                    Severity       = sev,
                    SeverityColor  = sev == "Critical" ? "red" : sev == "Warning" ? "amber" : "emerald",
                    Icon           = isNeg ? "fa-arrow-trend-down" : "fa-arrow-trend-up",
                    Unit           = "€",
                    Explanation    = isNeg
                        ? $"{storeName} revenue of €{actual:N0} this month is {Math.Abs(devPct):N1}% below the 11-month " +
                          $"baseline of €{mean:N0} ({Math.Abs(sigma):N1}σ). Investigate sales velocity, stock levels, and channel performance."
                        : $"{storeName} revenue of €{actual:N0} is {devPct:N1}% above baseline ({sigma:N1}σ). " +
                          "Investigate what is driving outperformance so the approach can be replicated."
                });
            }

            // ── Revenue anomaly by category ─────────────────────────────
            var allCats = products.Values.Select(p => p.Category).Distinct();

            foreach (var cat in allCats)
            {
                var baseline = Enumerable.Range(1, 11)
                    .Select(i => { var m = currentMonth.AddMonths(-i); return catMthData.TryGetValue((cat, m.Year, m.Month), out var d) ? d.Revenue : 0m; })
                    .Where(v => v > 0)
                    .ToList();

                if (baseline.Count < 4) continue;

                decimal mean   = baseline.Average();
                double  stddev = StdDev(baseline.Select(v => (double)v));
                if (stddev < 1.0 || mean < 300m) continue;

                decimal actual = catMthData.TryGetValue((cat, currentMonth.Year, currentMonth.Month), out var cd) ? cd.Revenue : 0m;
                double  sigma  = ((double)actual - (double)mean) / stddev;
                if (Math.Abs(sigma) < 1.5) continue;

                decimal devPct = mean > 0 ? (actual - mean) / mean * 100m : 0m;
                bool    isNeg  = sigma < 0;
                string  sev    = Math.Abs(sigma) >= 2.0 ? (isNeg ? "Critical" : "Positive") : (isNeg ? "Warning" : "Positive");

                exceptions.Add(new KpiException {
                    MetricName     = "Revenue",
                    Dimension      = cat,
                    DimensionType  = "Category",
                    ExpectedValue  = Math.Round(mean,   0),
                    ActualValue    = Math.Round(actual, 0),
                    DeviationPct   = Math.Round(devPct, 1),
                    DeviationSigma = Math.Round(sigma,  2),
                    Severity       = sev,
                    SeverityColor  = sev == "Critical" ? "red" : sev == "Warning" ? "amber" : "emerald",
                    Icon           = isNeg ? "fa-tag" : "fa-tag",
                    Unit           = "€",
                    Explanation    = isNeg
                        ? $"{cat} revenue of €{actual:N0} this month is {Math.Abs(devPct):N1}% below the 11-month baseline " +
                          $"of €{mean:N0} ({Math.Abs(sigma):N1}σ). Review product range, pricing, and stock depth."
                        : $"{cat} revenue of €{actual:N0} is {devPct:N1}% above baseline ({sigma:N1}σ). " +
                          "Demand is unusually strong — ensure stock is sufficient to capture the opportunity."
                });
            }

            // ── Margin anomaly by category ──────────────────────────────
            foreach (var cat in allCats)
            {
                var baselineMargins = Enumerable.Range(1, 11)
                    .Select(i => {
                        var m = currentMonth.AddMonths(-i);
                        return catMthData.TryGetValue((cat, m.Year, m.Month), out var d) && d.Revenue > 0
                            ? (double)(d.Profit / d.Revenue * 100m) : (double?)null;
                    })
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                if (baselineMargins.Count < 4) continue;

                double meanM   = baselineMargins.Average();
                double stddevM = StdDev(baselineMargins);
                if (stddevM < 0.5) continue;

                double actualMargin = catMthData.TryGetValue((cat, currentMonth.Year, currentMonth.Month), out var cm)
                    && cm.Revenue > 0 ? (double)(cm.Profit / cm.Revenue * 100m) : 0.0;

                double sigmaM  = (actualMargin - meanM) / stddevM;
                if (Math.Abs(sigmaM) < 1.5) continue;

                bool   isNeg = sigmaM < 0;
                double devM  = meanM > 0 ? (actualMargin - meanM) / meanM * 100.0 : 0.0;
                string sev   = Math.Abs(sigmaM) >= 2.0 ? (isNeg ? "Critical" : "Positive") : (isNeg ? "Warning" : "Positive");

                exceptions.Add(new KpiException {
                    MetricName     = "Margin",
                    Dimension      = cat,
                    DimensionType  = "Category",
                    ExpectedValue  = Math.Round((decimal)meanM, 1),
                    ActualValue    = Math.Round((decimal)actualMargin, 1),
                    DeviationPct   = Math.Round((decimal)devM, 1),
                    DeviationSigma = Math.Round(sigmaM, 2),
                    Severity       = sev,
                    SeverityColor  = sev == "Critical" ? "red" : sev == "Warning" ? "amber" : "emerald",
                    Icon           = isNeg ? "fa-percent" : "fa-percent",
                    Unit           = "%",
                    Explanation    = isNeg
                        ? $"{cat} profit margin of {actualMargin:N1}% is {Math.Abs(devM):N1}% below the 11-month " +
                          $"baseline of {meanM:N1}% ({Math.Abs(sigmaM):N1}σ). Review discounting, cost pressures, and pricing for this category."
                        : $"{cat} margin of {actualMargin:N1}% is significantly above baseline ({meanM:N1}%). " +
                          "Investigate whether pricing improvements or cost reductions can be sustained."
                });
            }

            vm.Exceptions = exceptions
                .OrderByDescending(e => Math.Abs(e.DeviationSigma))
                .ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION 4 — ABC/XYZ PORTFOLIO MATRIX
        // ════════════════════════════════════════════════════════════════════
        private static void BuildAbcXyz(
            SmartInsightsViewModel vm,
            List<RSale> sales,
            Dictionary<int, RProd> products,
            List<RInv> inventories)
        {
            var cutoff   = sales.Max(s => s.SaleDate).AddMonths(-12);
            var sales12m = sales.Where(s => s.SaleDate >= cutoff).ToList();

            // ── Revenue per product (ABC) ───────────────────────────────
            var prodRevenue = sales12m
                .GroupBy(s => s.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Revenue));

            // Include products with zero sales
            foreach (var pid in products.Keys)
                if (!prodRevenue.ContainsKey(pid))
                    prodRevenue[pid] = 0m;

            // ── Monthly revenue per product (XYZ) ──────────────────────
            var prodMonthly = sales12m
                .GroupBy(s => new { s.ProductId, s.SaleDate.Year, s.SaleDate.Month })
                .Select(g => new { g.Key.ProductId, Revenue = g.Sum(x => x.Revenue) })
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Revenue).ToList());

            // ── Inventory per product (for drill-down display) ──────────
            var stockByProd = inventories
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Stock));

            // ── ABC classification ──────────────────────────────────────
            decimal totalRev = prodRevenue.Values.Sum();
            var     sorted   = prodRevenue.OrderByDescending(kv => kv.Value).ToList();
            var     abcClass = new Dictionary<int, string>();
            decimal cumul    = 0m;

            foreach (var kv in sorted)
            {
                string cls = cumul < totalRev * 0.80m ? "A"
                           : cumul < totalRev * 0.95m ? "B"
                           : "C";
                abcClass[kv.Key] = cls;
                cumul += kv.Value;
            }

            // ── XYZ classification ──────────────────────────────────────
            var xyzClass = new Dictionary<int, (string Cls, double Cv)>();
            foreach (var pid in prodRevenue.Keys)
            {
                if (!prodMonthly.TryGetValue(pid, out var monthly) || monthly.Count < 3)
                {
                    xyzClass[pid] = ("Z", 99.0);
                    continue;
                }
                double mean = (double)monthly.Average();
                if (mean < 1.0) { xyzClass[pid] = ("Z", 99.0); continue; }

                double stddev = StdDev(monthly.Select(v => (double)v));
                double cv     = stddev / mean;
                xyzClass[pid] = cv <= 0.3 ? ("X", cv) : cv <= 0.6 ? ("Y", cv) : ("Z", cv);
            }

            // ── Build 3×3 matrix ────────────────────────────────────────
            var keys = new[] { "AX", "AY", "AZ", "BX", "BY", "BZ", "CX", "CY", "CZ" };

            var (labels, bgs, texts, recs) = GetMatrixMeta();
            var matrix = keys.ToDictionary(k => k, k => new AbcXyzCell {
                AbcClass      = k[..1],
                XyzClass      = k[1..],
                StrategicLabel = labels[k],
                Recommendation = recs[k],
                CellBg         = bgs[k],
                CellText       = texts[k]
            });

            foreach (var (pid, rev) in prodRevenue)
            {
                if (!abcClass.TryGetValue(pid, out var abc))  continue;
                if (!xyzClass.TryGetValue(pid, out var xyz))  continue;
                if (!products.TryGetValue(pid, out var prod)) continue;

                string key = abc + xyz.Cls;
                if (!matrix.TryGetValue(key, out var cell)) continue;

                cell.ProductCount++;
                cell.Revenue += rev;
                stockByProd.TryGetValue(pid, out var stock);

                if (cell.Products.Count < 20)
                    cell.Products.Add(new AbcXyzProductEntry {
                        ProductId   = pid,
                        ProductName = prod.Name,
                        ProductCode = prod.Code,
                        Category    = prod.Category,
                        Revenue     = Math.Round(rev, 2),
                        Cv          = Math.Round(xyz.Cv, 3),
                        CurrentStock = stock,
                        AbcClass    = abc,
                        XyzClass    = xyz.Cls
                    });
            }

            // Compute revenue % per cell
            foreach (var cell in matrix.Values)
            {
                cell.RevenuePercent = totalRev > 0
                    ? Math.Round(cell.Revenue / totalRev * 100m, 1) : 0m;
                cell.Products = cell.Products.OrderByDescending(p => p.Revenue).ToList();
            }

            vm.Matrix               = matrix;
            vm.TotalProductCount    = prodRevenue.Count;
            vm.TotalPortfolioRevenue = Math.Round(totalRev, 2);
        }

        // ── Matrix labels/colours/recommendations ─────────────────────────
        private static (Dictionary<string, string> Labels,
                        Dictionary<string, string> Bgs,
                        Dictionary<string, string> Texts,
                        Dictionary<string, string> Recs)
            GetMatrixMeta()
        {
            var L = new Dictionary<string, string> {
                ["AX"] = "Protect",     ["AY"] = "Invest",      ["AZ"] = "Manage",
                ["BX"] = "Maintain",    ["BY"] = "Review",      ["BZ"] = "Reduce",
                ["CX"] = "Consolidate", ["CY"] = "Defer",       ["CZ"] = "Exit"
            };
            var B = new Dictionary<string, string> {
                ["AX"] = "#d1fae5", ["AY"] = "#a7f3d0", ["AZ"] = "#fef3c7",
                ["BX"] = "#dbeafe", ["BY"] = "#e0e7ff", ["BZ"] = "#fde8d8",
                ["CX"] = "#f1f5f9", ["CY"] = "#f1f5f9", ["CZ"] = "#fee2e2"
            };
            var T = new Dictionary<string, string> {
                ["AX"] = "#065f46", ["AY"] = "#065f46", ["AZ"] = "#92400e",
                ["BX"] = "#1e40af", ["BY"] = "#3730a3", ["BZ"] = "#9a3412",
                ["CX"] = "#475569", ["CY"] = "#475569", ["CZ"] = "#991b1b"
            };
            var R = new Dictionary<string, string> {
                ["AX"] = "High-value, stable. Maintain deep stock. Never allow stockouts.",
                ["AY"] = "High-value, variable. Add safety stock. Monitor sell-through weekly.",
                ["AZ"] = "High-value, unpredictable. Use event-driven replenishment. Buffer carefully.",
                ["BX"] = "Mid-value, stable. Standard replenishment cycle. Optimise order quantities.",
                ["BY"] = "Mid-value, variable. Assess cost-to-serve. Consider range rationalisation.",
                ["BZ"] = "Mid-value, unpredictable. Limit exposure. Reduce open-to-buy.",
                ["CX"] = "Low-value, stable. Consider consolidation or phasing out. Low priority.",
                ["CY"] = "Low-value, variable. Defer investment. Monitor for decline signals.",
                ["CZ"] = "Low-value, unpredictable. Mark down and exit the range. Free capital."
            };
            return (L, B, T, R);
        }

        // ── Helper: population standard deviation ─────────────────────────
        private static double StdDev(IEnumerable<double> values)
        {
            var list = values.ToList();
            if (list.Count < 2) return 0.0;
            double mean = list.Average();
            return Math.Sqrt(list.Average(v => Math.Pow(v - mean, 2)));
        }
    }
}
