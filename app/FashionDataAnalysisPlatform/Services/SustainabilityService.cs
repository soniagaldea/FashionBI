using FashionDataAnalysisPlatform.Data;
using FashionDataAnalysisPlatform.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FashionDataAnalysisPlatform.Services
{
    public class SustainabilityService
    {
        private readonly AppDbContext _context;
        public SustainabilityService(AppDbContext context) => _context = context;

        // ── In-memory projection types ─────────────────────────────────────
        private sealed record RProd(
            int ProductId, int? StoreId, string Name, string Code,
            string Category, string? Season, bool IsSeasonal,
            decimal BaseCost, decimal UnitPrice);

        private sealed record RInv(
            int ProductId, int? StoreId, int Stock, DateTime? LastRestockDate);

        private sealed record RSale(
            int ProductId, int? StoreId, DateTime SaleDate,
            int Quantity, decimal? DiscountPercent);

        // ── Season end date lookup ─────────────────────────────────────────
        private static readonly Dictionary<string, DateTime> SeasonEnds =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["AW24"] = new DateTime(2025, 3,  1),
                ["SS25"] = new DateTime(2025, 9,  1),
                ["AW25"] = new DateTime(2026, 3,  1),
                ["SS26"] = new DateTime(2026, 9,  1),
            };

        private static DateTime? GetSeasonEnd(string? season)
        {
            if (string.IsNullOrWhiteSpace(season)) return null;
            return SeasonEnds.TryGetValue(season.Trim(), out var d) ? d : null;
        }

        private static int DaysToSeasonEnd(string? season, DateTime today)
        {
            var end = GetSeasonEnd(season);
            if (end == null) return 9999;   // Core / unknown = no deadline
            return (int)(end.Value - today).TotalDays;
        }

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC ENTRY POINT
        // ════════════════════════════════════════════════════════════════════
        public async Task<SustainabilityViewModel> BuildViewModelAsync(List<int> storeIds)
        {
            var vm = new SustainabilityViewModel { LastRefreshedAt = DateTime.Now };

            // ── Products (all — no store filter; season data is global) ────
            var products = await _context.Products.AsNoTracking()
                .Select(p => new RProd(p.ProductId, p.StoreId, p.ProductName,
                    p.ProductCode, p.Category, p.Season, p.IsSeasonal,
                    p.BaseCost, p.UnitPrice))
                .ToListAsync();

            if (!products.Any()) return vm;

            // ── Inventories ────────────────────────────────────────────────
            var invQuery = _context.Inventories.AsNoTracking()
                .Where(i => i.StoreId != null);
            if (storeIds.Any())
                invQuery = invQuery.Where(i => storeIds.Contains(i.StoreId!.Value));

            var inventories = await invQuery
                .Select(i => new RInv(i.ProductId, i.StoreId,
                    i.CurrentStock, i.LastRestockDate))
                .ToListAsync();

            // ── Sales (all time — sustainability needs full history) ────────
            var salesQuery = _context.Sales.AsNoTracking()
                .Where(s => s.StoreId != null);
            if (storeIds.Any())
                salesQuery = salesQuery.Where(s => storeIds.Contains(s.StoreId!.Value));

            var sales = await salesQuery
                .Select(s => new RSale(s.ProductId, s.StoreId,
                    s.SaleDate, s.Quantity, s.DiscountPercent))
                .ToListAsync();

            // ── Store name lookup ──────────────────────────────────────────
            var storeQuery = _context.Stores.AsNoTracking().AsQueryable();
            if (storeIds.Any())
                storeQuery = storeQuery.Where(s => storeIds.Contains(s.StoreId));
            var storeMap = await storeQuery
                .ToDictionaryAsync(s => s.StoreId, s => s.StoreName);

            if (!sales.Any() && !inventories.Any()) return vm;
            vm.HasData = true;

            var today          = DateTime.Today;
            var deadStockCutoff = today.AddDays(-45);

            // ── Pre-aggregate sales per product ────────────────────────────
            var soldByProd = sales
                .GroupBy(s => s.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            var recentSoldByProd = sales
                .Where(s => s.SaleDate >= deadStockCutoff)
                .GroupBy(s => s.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            var markdownSoldByProd = sales
                .Where(s => s.DiscountPercent >= 20m)
                .GroupBy(s => s.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            // ── Inventory per product (sum across stores if all-store view) ─
            var stockByProd = inventories
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Stock));

            var prodMap = products.ToDictionary(p => p.ProductId);

            // ── Build per-SKU metrics ──────────────────────────────────────
            var skuMetrics = new List<SkuMetric>();

            foreach (var inv in inventories)
            {
                if (!prodMap.TryGetValue(inv.ProductId, out var prod)) continue;

                int     soldUnits   = soldByProd.GetValueOrDefault(inv.ProductId, 0);
                int     totalUnits  = soldUnits + inv.Stock;
                decimal str         = totalUnits > 0 ? (decimal)soldUnits / totalUnits * 100m : 0m;
                int     daysToEnd   = DaysToSeasonEnd(prod.Season, today);
                decimal atRiskValue = inv.Stock * prod.BaseCost;

                int     recentSold  = recentSoldByProd.GetValueOrDefault(inv.ProductId, 0);
                bool    isDeadStock = inv.Stock > 0 && recentSold == 0;

                int     mdSold      = markdownSoldByProd.GetValueOrDefault(inv.ProductId, 0);
                decimal mdRate      = soldUnits > 0 ? (decimal)mdSold / soldUnits * 100m : 0m;

                int wasteScore = ComputeWasteRiskScore(str, daysToEnd, atRiskValue, isDeadStock, prod.IsSeasonal);

                string storeName = inv.StoreId.HasValue && storeMap.TryGetValue(inv.StoreId.Value, out var sn)
                    ? sn : "All Stores";

                skuMetrics.Add(new SkuMetric(
                    prod.ProductId, prod.Name, prod.Code, prod.Category,
                    prod.Season ?? "Core", prod.IsSeasonal,
                    prod.BaseCost, inv.Stock, soldUnits,
                    str, daysToEnd, atRiskValue, isDeadStock,
                    mdRate, wasteScore, storeName));
            }

            // ── TOP KPI ROW ────────────────────────────────────────────────
            BuildKpis(vm, skuMetrics, sales, today);

            // ── SECTION 2: Season Timeline ─────────────────────────────────
            BuildSeasonTimeline(vm, skuMetrics, today);
            BuildCollectionSummary(vm);

            // ── SECTION 3: Waste Risk Matrix ──────────────────────────────
            BuildWasteRiskMatrix(vm, skuMetrics);

            // ── SECTION 4: Sell-Through by Category ───────────────────────
            BuildCategorySellThrough(vm, skuMetrics);

            // ── SECTION 5: Markdown Dependency ────────────────────────────
            BuildMarkdownDependency(vm, sales, prodMap);

            // ── SECTION 6: At-Risk SKU Table ──────────────────────────────
            BuildSkuTable(vm, skuMetrics);

            // ── SECTION 7: Recommendations ────────────────────────────────
            BuildRecommendations(vm, skuMetrics, vm.CategoryMarkdowns);

            return vm;
        }

        // ── Waste Risk Score: 0–100 ────────────────────────────────────────
        // Transparent formula documented in the Methodology modal.
        // STR deficit (50 pts) + Time pressure (30 pts) + Dead stock bonus (20 pts)
        private static int ComputeWasteRiskScore(
            decimal str, int daysToEnd, decimal atRiskValue,
            bool isDeadStock, bool isSeasonal)
        {
            // Component 1: STR deficit (higher unsold = higher risk)
            double strComponent = (double)(1m - str / 100m) * 50.0;

            // Component 2: Time pressure — only for seasonal items
            double timeComponent = 0.0;
            if (isSeasonal && daysToEnd != 9999)
            {
                timeComponent = daysToEnd <= 0   ? 30.0 :
                                daysToEnd <= 30  ? 26.0 :
                                daysToEnd <= 60  ? 20.0 :
                                daysToEnd <= 90  ? 14.0 :
                                daysToEnd <= 180 ?  7.0 : 3.0;
            }

            // Component 3: Dead stock penalty
            double deadComponent = isDeadStock ? 20.0 : 0.0;

            int score = (int)Math.Min(100, strComponent + timeComponent + deadComponent);
            return score;
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION: KPIs
        // ════════════════════════════════════════════════════════════════════
        private static void BuildKpis(
            SustainabilityViewModel vm,
            List<SkuMetric> skuMetrics,
            List<RSale> sales,
            DateTime today)
        {
            // Avg STR across all SKUs with stock or sales
            var withData = skuMetrics.Where(s => s.SoldUnits + s.CurrentStock > 0).ToList();
            vm.AvgSellThroughRate = withData.Any()
                ? Math.Round(withData.Average(s => s.SellThroughRate), 1) : 0m;

            // At-risk: seasonal items with STR < 70% and daysToEnd <= 180 (or expired)
            vm.AtRiskInventoryValue = Math.Round(skuMetrics
                .Where(s => s.IsSeasonal && s.SellThroughRate < 70m
                         && (s.DaysToEnd <= 180 || s.DaysToEnd < 0))
                .Sum(s => s.AtRiskValue), 0);

            vm.DeadStockCount = skuMetrics.Count(s => s.IsDeadStock && s.CurrentStock > 0);

            // Markdown dependency — network-wide rate
            int totalSold    = sales.Sum(s => s.Quantity);
            int mdSold       = sales.Where(s => s.DiscountPercent >= 20m).Sum(s => s.Quantity);
            vm.MarkdownDependencyRate = totalSold > 0
                ? Math.Round((decimal)mdSold / totalSold * 100m, 1) : 0m;

            // Seasonal overstock value: remaining stock in expired seasons
            vm.SeasonalOverstockValue = Math.Round(skuMetrics
                .Where(s => s.IsSeasonal && s.DaysToEnd < 0 && s.CurrentStock > 0)
                .Sum(s => s.AtRiskValue), 0);
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION 2: Season Timeline
        // ════════════════════════════════════════════════════════════════════
        private static void BuildSeasonTimeline(
            SustainabilityViewModel vm,
            List<SkuMetric> skuMetrics,
            DateTime today)
        {
            var seasonOrder = new[] { "AW24", "SS25", "AW25", "SS26", "Core" };

            foreach (var season in seasonOrder)
            {
                var rows = skuMetrics
                    .Where(s => string.Equals(s.SeasonName, season, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (!rows.Any()) continue;

                int  sold      = rows.Sum(r => r.SoldUnits);
                int  remaining = rows.Sum(r => r.CurrentStock);
                int  total     = sold + remaining;
                decimal str    = total > 0 ? (decimal)sold / total * 100m : 0m;
                decimal atRisk = rows.Sum(r => r.AtRiskValue);

                bool isCore  = string.Equals(season, "Core", StringComparison.OrdinalIgnoreCase);
                var  end     = isCore ? (DateTime?)null : GetSeasonEnd(season);
                int  daysToE = isCore ? 9999 : DaysToSeasonEnd(season, today);

                string status, color;
                if (isCore)
                {
                    status = str >= 30m ? "Healthy" : "Monitor";
                    color  = str >= 30m ? "emerald" : "amber";
                }
                else if (daysToE < 0)
                {
                    status = "Closed";
                    color  = "slate";
                }
                else if (str >= 80m)
                {
                    status = "Healthy";
                    color  = "emerald";
                }
                else if (str >= 60m || daysToE > 90)
                {
                    status = "Monitor";
                    color  = "amber";
                }
                else
                {
                    status = "Critical";
                    color  = "red";
                }

                vm.Seasons.Add(new SeasonSummary {
                    SeasonName      = season,
                    IsCore          = isCore,
                    SeasonEnd       = end,
                    DaysToEnd       = daysToE,
                    SellThroughRate = Math.Round(str, 1),
                    SoldUnits       = sold,
                    RemainingUnits  = remaining,
                    AtRiskValue     = Math.Round(atRisk, 0),
                    Status          = status,
                    StatusColor     = color
                });
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // COLLECTION SUMMARY — one executive sentence
        // ════════════════════════════════════════════════════════════════════
        private static void BuildCollectionSummary(SustainabilityViewModel vm)
        {
            var active  = vm.Seasons.Where(s => !s.IsCore && s.Status != "Closed")
                                    .OrderBy(s => s.SellThroughRate).ToList();
            var core    = vm.Seasons.FirstOrDefault(s => s.IsCore);
            var closed  = vm.Seasons.Where(s => !s.IsCore && s.Status == "Closed")
                                    .OrderByDescending(s => s.SellThroughRate).ToList();

            var parts = new List<string>();

            if (active.Any())
            {
                var worst = active.First();
                string riskWord = worst.Status == "Critical" ? "is the highest-risk active collection"
                                : worst.Status == "Monitor"  ? "requires close monitoring"
                                :                              "is on track";

                string riskPart = worst.AtRiskValue > 0
                    ? $" with €{worst.AtRiskValue:N0} in inventory at risk"
                    : string.Empty;

                parts.Add($"{worst.SeasonName} {riskWord} at {worst.SellThroughRate:N1}% sell-through{riskPart}");

                if (active.Count > 1)
                {
                    var best = active.Last();
                    if (best.SellThroughRate >= 75m)
                        parts.Add($"{best.SeasonName} is tracking well at {best.SellThroughRate:N1}%");
                }
            }
            else if (closed.Any())
            {
                var best = closed.First();
                parts.Add($"{best.SeasonName} closed at {best.SellThroughRate:N1}% sell-through");
            }

            if (core != null)
            {
                string coreWord = core.SellThroughRate >= 70m ? "healthy" : "below target";
                parts.Add($"Core products remain {coreWord} at {core.SellThroughRate:N1}% sell-through");
            }

            vm.CollectionSummary = parts.Any()
                ? string.Join(". ", parts) + "."
                : "All collections are performing within target sell-through benchmarks.";
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION 3: Waste Risk Matrix
        // ════════════════════════════════════════════════════════════════════
        private static void BuildWasteRiskMatrix(
            SustainabilityViewModel vm,
            List<SkuMetric> skuMetrics)
        {
            // Aggregate per Category × Season
            var groups = skuMetrics
                .GroupBy(s => (s.Category, s.SeasonName))
                .Select(g => {
                    int  sold      = g.Sum(x => x.SoldUnits);
                    int  remaining = g.Sum(x => x.CurrentStock);
                    int  total     = sold + remaining;
                    decimal str    = total > 0 ? (decimal)sold / total * 100m : 0m;
                    int  daysToE   = g.Min(x => x.DaysToEnd);   // most urgent
                    decimal atRisk = g.Sum(x => x.AtRiskValue);
                    int  score     = g.Max(x => x.WasteRiskScore);

                    string color = score >= 65 ? "red" : score >= 35 ? "amber" : "green";

                    return new WasteRiskPoint {
                        Label           = $"{g.Key.Category} — {g.Key.SeasonName}",
                        Category        = g.Key.Category,
                        SeasonName      = g.Key.SeasonName,
                        DaysToEnd       = daysToE,
                        SellThroughRate = Math.Round(str, 1),
                        AtRiskValue     = Math.Round(atRisk, 0),
                        WasteRiskScore  = score,
                        RiskColor       = color
                    };
                })
                .Where(p => p.AtRiskValue > 0 || p.WasteRiskScore > 0)
                .OrderByDescending(p => p.WasteRiskScore)
                .ToList();

            vm.WasteRiskPoints = groups;
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION 4: Category Sell-Through
        // ════════════════════════════════════════════════════════════════════
        private static void BuildCategorySellThrough(
            SustainabilityViewModel vm,
            List<SkuMetric> skuMetrics)
        {
            var rows = skuMetrics
                .GroupBy(s => s.Category)
                .Select(g => {
                    int  sold      = g.Sum(x => x.SoldUnits);
                    int  remaining = g.Sum(x => x.CurrentStock);
                    int  total     = sold + remaining;
                    decimal str    = total > 0 ? (decimal)sold / total * 100m : 0m;
                    string color   = str >= 80m ? "emerald" : str >= 60m ? "amber" : "red";
                    return new CategorySellThrough {
                        Category        = g.Key,
                        SoldUnits       = sold,
                        RemainingUnits  = remaining,
                        SellThroughRate = Math.Round(str, 1),
                        StatusColor     = color
                    };
                })
                .OrderBy(r => r.SellThroughRate)
                .ToList();

            vm.CategorySellThroughs = rows;
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION 5: Markdown Dependency
        // ════════════════════════════════════════════════════════════════════
        private static void BuildMarkdownDependency(
            SustainabilityViewModel vm,
            List<RSale> sales,
            Dictionary<int, RProd> prodMap)
        {
            var rows = sales
                .Where(s => prodMap.ContainsKey(s.ProductId))
                .GroupBy(s => prodMap[s.ProductId].Category)
                .Select(g => {
                    int total = g.Sum(x => x.Quantity);
                    int mdQty = g.Where(x => x.DiscountPercent >= 20m).Sum(x => x.Quantity);
                    decimal mdRate = total > 0 ? (decimal)mdQty / total * 100m : 0m;
                    decimal avgDisc = total > 0
                        ? g.Where(x => x.DiscountPercent.HasValue)
                           .Sum(x => (x.DiscountPercent ?? 0m) * x.Quantity) / total
                        : 0m;

                    string interpretation =
                        mdRate >= 60m ? "Systematic overproduction signal — review buy quantities" :
                        mdRate >= 40m ? "High discount reliance — pricing or demand mismatch" :
                        mdRate >= 20m ? "Moderate markdown use — acceptable for fashion retail" :
                                        "Low markdown dependency — strong demand alignment";

                    string color = mdRate >= 60m ? "red" : mdRate >= 40m ? "amber" : "emerald";

                    return new CategoryMarkdown {
                        Category           = g.Key,
                        MarkdownDependency = Math.Round(mdRate, 1),
                        AvgDiscountPercent = Math.Round(avgDisc, 1),
                        TotalUnitsSold     = total,
                        MarkdownUnitsSold  = mdQty,
                        Interpretation     = interpretation,
                        StatusColor        = color
                    };
                })
                .OrderByDescending(r => r.MarkdownDependency)
                .ToList();

            vm.CategoryMarkdowns = rows;
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION 6: At-Risk SKU Table
        // ════════════════════════════════════════════════════════════════════
        private static void BuildSkuTable(
            SustainabilityViewModel vm,
            List<SkuMetric> skuMetrics)
        {
            var rows = skuMetrics
                .Where(s => s.WasteRiskScore > 10 || s.IsDeadStock || s.CurrentStock > 0)
                .OrderByDescending(s => s.WasteRiskScore)
                .Take(20)
                .Select(s => {
                    string action =
                        s.IsDeadStock && s.DaysToEnd < 0              ? "Liquidate / donate" :
                        s.IsDeadStock                                  ? "Apply markdown" :
                        s.WasteRiskScore >= 65 && s.DaysToEnd <= 60   ? "Apply markdown" :
                        s.WasteRiskScore >= 65                         ? "Apply early markdown" :
                        s.MarkdownDependency >= 60m                    ? "Reduce next-season buy quantity" :
                        s.WasteRiskScore >= 35                         ? "Monitor demand" :
                                                                         "Maintain current strategy";

                    string color = s.WasteRiskScore >= 65 ? "red"
                                 : s.WasteRiskScore >= 35 ? "amber"
                                 : "emerald";

                    return new SkuRiskEntry {
                        ProductName        = s.ProductName,
                        StoreName          = s.StoreName,
                        Category           = s.Category,
                        SeasonName         = s.SeasonName,
                        SellThroughRate    = Math.Round(s.SellThroughRate, 1),
                        CurrentStock       = s.CurrentStock,
                        AtRiskValue        = Math.Round(s.AtRiskValue, 0),
                        DaysToSeasonEnd    = s.DaysToEnd,
                        MarkdownDependency = Math.Round(s.MarkdownDependency, 1),
                        WasteRiskScore     = s.WasteRiskScore,
                        RecommendedAction  = action,
                        RiskColor          = color,
                        IsDeadStock        = s.IsDeadStock
                    };
                })
                .ToList();

            vm.AtRiskSkus = rows;
        }

        // ════════════════════════════════════════════════════════════════════
        // SECTION 7: Recommendations
        // ════════════════════════════════════════════════════════════════════
        private static void BuildRecommendations(
            SustainabilityViewModel vm,
            List<SkuMetric> skuMetrics,
            List<CategoryMarkdown> markdowns)
        {
            var actNow     = new List<SustainabilityRecommendation>();
            var monitor    = new List<SustainabilityRecommendation>();
            var planBetter = new List<SustainabilityRecommendation>();

            // ── ACT NOW 1: Critical seasonal waste risk ───────────────────
            var criticalGroups = skuMetrics
                .Where(s => s.IsSeasonal && s.WasteRiskScore >= 65 && s.DaysToEnd <= 90 && s.DaysToEnd >= 0)
                .GroupBy(s => (s.SeasonName, s.Category))
                .OrderByDescending(g => g.Sum(x => x.AtRiskValue))
                .Take(3);

            foreach (var g in criticalGroups)
            {
                decimal atRisk = g.Sum(x => x.AtRiskValue);
                int daysLeft   = g.Min(x => x.DaysToEnd);
                decimal avgStr = g.Average(x => x.SellThroughRate);
                decimal recovery = atRisk * 0.45m; // estimated recovery via 30% markdown

                actNow.Add(new SustainabilityRecommendation {
                    Group           = "ActNow",
                    Icon            = "fa-fire",
                    Title           = $"Markdown {g.Key.Category} ({g.Key.SeasonName}) — {daysLeft} days to season end",
                    Explanation     = $"{g.Key.Category} in the {g.Key.SeasonName} season has a {avgStr:N0}% sell-through rate " +
                                      $"with only {daysLeft} days remaining before season close. " +
                                      $"At-risk inventory value: €{atRisk:N0}.",
                    SuggestedAction = $"Initiate a 25–30% markdown on {g.Key.Category} ({g.Key.SeasonName}) immediately. " +
                                      $"Estimated cost recovery at 30% markdown: €{recovery:N0}. " +
                                      "Delaying past 30 days will require deeper discounts to move stock.",
                    FinancialImpact = Math.Round(atRisk, 0),
                    ImpactLabel     = "At-Risk Inventory Value",
                    DataBadge       = "Inventory · Sales"
                });
            }

            // ── ACT NOW 2: Dead stock — single aggregated recommendation ──
            var allDeadStock = skuMetrics
                .Where(s => s.IsDeadStock && s.CurrentStock > 0)
                .ToList();

            if (allDeadStock.Any())
            {
                int     dsCount = allDeadStock.Count;
                decimal dsValue = allDeadStock.Sum(x => x.AtRiskValue);
                string  topCats = string.Join(", ", allDeadStock
                    .GroupBy(x => x.Category)
                    .OrderByDescending(g => g.Sum(x => x.AtRiskValue))
                    .Take(3)
                    .Select(g => g.Key));

                actNow.Add(new SustainabilityRecommendation {
                    Group           = "ActNow",
                    Icon            = "fa-box-archive",
                    Title           = $"Dead Stock Alert — {dsCount} SKU{(dsCount != 1 ? "s" : "")} with zero sales in 45 days",
                    Explanation     = $"{dsCount} products across {topCats} have recorded zero sales in the last 45 days " +
                                      $"while holding a combined inventory cost of €{dsValue:N0}. " +
                                      "Dead stock ties up capital and requires increasingly deep markdowns the longer it sits unsold.",
                    SuggestedAction = "Apply a 40–60% liquidation markdown, initiate inter-store stock transfers " +
                                      "to higher-traffic locations, or arrange charity donation for any units that cannot be sold commercially.",
                    FinancialImpact = Math.Round(dsValue, 0),
                    ImpactLabel     = "Dead Stock Cost Value",
                    DataBadge       = "Inventory"
                });
            }

            // ── ACT NOW 3: Expired season stock ───────────────────────────
            var expiredValue = skuMetrics
                .Where(s => s.IsSeasonal && s.DaysToEnd < 0 && s.CurrentStock > 0)
                .ToList();

            if (expiredValue.Any())
            {
                decimal total = expiredValue.Sum(x => x.AtRiskValue);
                int     count = expiredValue.Count;
                actNow.Add(new SustainabilityRecommendation {
                    Group           = "ActNow",
                    Icon            = "fa-calendar-xmark",
                    Title           = $"Expired Season Stock — {count} SKUs still in inventory",
                    Explanation     = $"{count} seasonal products remain in stock past their season end date, " +
                                      $"representing €{total:N0} in unsold inventory cost. " +
                                      "This stock has no natural sell-through opportunity in the current seasonal cycle.",
                    SuggestedAction = "Immediately exit all expired-season stock via clearance promotion, " +
                                      "staff sale, or charity donation. " +
                                      "This capital should be redirected to next-season buying.",
                    FinancialImpact = Math.Round(total, 0),
                    ImpactLabel     = "Expired Season Cost Value",
                    DataBadge       = "Inventory · Season"
                });
            }

            // ── MONITOR 1: Approaching season risk ────────────────────────
            var approachingGroups = skuMetrics
                .Where(s => s.IsSeasonal && s.DaysToEnd > 90 && s.DaysToEnd <= 180
                         && s.SellThroughRate < 65m)
                .GroupBy(s => s.SeasonName)
                .OrderByDescending(g => g.Sum(x => x.AtRiskValue))
                .Take(2);

            foreach (var g in approachingGroups)
            {
                decimal atRisk = g.Sum(x => x.AtRiskValue);
                decimal avgStr = g.Average(x => x.SellThroughRate);
                int daysLeft   = g.Min(x => x.DaysToEnd);

                monitor.Add(new SustainabilityRecommendation {
                    Group           = "Monitor",
                    Icon            = "fa-clock",
                    Title           = $"{g.Key} Season: {avgStr:N0}% sell-through with {daysLeft} days remaining",
                    Explanation     = $"The {g.Key} season is tracking at {avgStr:N0}% sell-through rate " +
                                      $"with {daysLeft} days remaining. At current velocity, " +
                                      $"€{atRisk:N0} of inventory may not sell through before season end.",
                    SuggestedAction = $"Monitor weekly velocity for {g.Key} products. " +
                                      "If sell-through does not reach 70% within 6 weeks, initiate a pre-emptive markdown.",
                    FinancialImpact = Math.Round(atRisk, 0),
                    ImpactLabel     = "Potential At-Risk Value",
                    DataBadge       = "Inventory · Season"
                });
            }

            // ── MONITOR 2: High-risk categories with moderate lead time ───
            var moderateRisk = skuMetrics
                .Where(s => s.WasteRiskScore >= 40 && s.WasteRiskScore < 65)
                .GroupBy(s => s.Category)
                .OrderByDescending(g => g.Sum(x => x.AtRiskValue))
                .Take(2);

            foreach (var g in moderateRisk)
            {
                decimal atRisk = g.Sum(x => x.AtRiskValue);
                decimal avgStr = g.Average(x => x.SellThroughRate);

                monitor.Add(new SustainabilityRecommendation {
                    Group           = "Monitor",
                    Icon            = "fa-gauge",
                    Title           = $"{g.Key}: Moderate waste risk — {avgStr:N0}% sell-through",
                    Explanation     = $"{g.Key} has a moderate waste risk score. " +
                                      $"Average sell-through is {avgStr:N0}% with €{atRisk:N0} in remaining inventory cost.",
                    SuggestedAction = $"Review {g.Key} weekly demand velocity. Adjust visual merchandising " +
                                      "and promotional placement before committing to a markdown event.",
                    FinancialImpact = Math.Round(atRisk, 0),
                    ImpactLabel     = "Inventory at Moderate Risk",
                    DataBadge       = "Inventory · Sales"
                });
            }

            // ── PLAN BETTER 1: Chronic markdown dependency ────────────────
            // Capped at 2 to guarantee space for the retrospective rec below.
            var chronicMd = markdowns
                .Where(m => m.MarkdownDependency >= 50m)
                .OrderByDescending(m => m.MarkdownDependency)
                .Take(2);

            foreach (var m in chronicMd)
            {
                planBetter.Add(new SustainabilityRecommendation {
                    Group           = "PlanBetter",
                    Icon            = "fa-scissors",
                    Title           = $"Reduce {m.Category} Buy Quantity — {m.MarkdownDependency:N0}% markdown dependency",
                    Explanation     = $"{m.MarkdownDependency:N0}% of {m.Category} units were sold with a discount " +
                                      $"of 20% or more (avg discount: {m.AvgDiscountPercent:N1}%). " +
                                      "This is a systematic overproduction signal: the market cannot absorb this category " +
                                      "at full price, meaning the buy quantity consistently exceeds natural demand.",
                    SuggestedAction = $"Reduce the next-season open-to-buy allocation for {m.Category} " +
                                      $"by 15–20%. Use the Forecasting module to align buy quantity with " +
                                      "demand predictions rather than historical order volumes.",
                    FinancialImpact = 0m,
                    ImpactLabel     = "Next-Season Overproduction Risk",
                    DataBadge       = "Sales · Markdown"
                });
            }

            // ── PLAN BETTER 2: Post-season learning retrospective ─────────
            // Always generated if any season has closed. Covers both
            // underperforming and healthy closed seasons so Plan Better
            // is never empty — demonstrating strategic learning, not just
            // operational intervention.
            var closedForLearning = vm.Seasons
                .Where(s => !s.IsCore && s.Status == "Closed")
                .Select(s => new { Summary = s, End = GetSeasonEnd(s.SeasonName) ?? DateTime.MinValue })
                .OrderByDescending(x => x.End)   // most recently closed first
                .Take(2)
                .Select(x => x.Summary)
                .ToList();

            foreach (var s in closedForLearning)
            {
                bool underperformed  = s.SellThroughRate < 80m;
                string seasonType    = s.SeasonName.StartsWith("AW", StringComparison.OrdinalIgnoreCase)
                                       ? "Autumn/Winter" : "Spring/Summer";
                string reductionPct  = s.SellThroughRate < 60m ? "20–25%"
                                     : s.SellThroughRate < 75m ? "10–15%"
                                     :                            "5–10%";

                string explanation = underperformed
                    ? $"The {s.SeasonName} season closed at {s.SellThroughRate:N1}% sell-through" +
                      (s.AtRiskValue > 0 ? $" with €{s.AtRiskValue:N0} in unsold inventory cost" : "") +
                      " — below the 80% industry benchmark. The buy quantity for this collection exceeded actual market demand."
                    : $"The {s.SeasonName} season closed at {s.SellThroughRate:N1}% sell-through, " +
                      "meeting the 80% benchmark for sustainable inventory management. " +
                      "Buy quantities were well-calibrated to demand and serve as a reliable planning reference.";

                string action = underperformed
                    ? $"Reduce the equivalent {seasonType} open-to-buy allocation by {reductionPct} relative to {s.SeasonName} volumes. " +
                      "Cross-reference with the Forecasting module's demand predictions before finalising next-season buy quantities."
                    : $"Use the {s.SeasonName} buy quantities and category mix as the reference baseline for the equivalent next {seasonType} season. " +
                      "Apply Forecasting module adjustments for trend and growth rather than inflating volumes arbitrarily.";

                planBetter.Add(new SustainabilityRecommendation {
                    Group           = "PlanBetter",
                    Icon            = underperformed ? "fa-book-open" : "fa-star",
                    Title           = underperformed
                        ? $"Post-Season Learning: {s.SeasonName} closed at {s.SellThroughRate:N1}% — reduce equivalent next-season buy"
                        : $"Buying Benchmark: {s.SeasonName} closed at {s.SellThroughRate:N1}% — use as next-season reference",
                    Explanation     = explanation,
                    SuggestedAction = action,
                    FinancialImpact = Math.Round(s.AtRiskValue, 0),
                    ImpactLabel     = underperformed ? "Historical Unsold Cost" : "Buying Efficiency Benchmark",
                    DataBadge       = "Season · Post-Mortem"
                });
            }

            // ── Fallback: guarantee Plan Better is never empty ────────────
            if (!planBetter.Any())
            {
                planBetter.Add(new SustainabilityRecommendation {
                    Group           = "PlanBetter",
                    Icon            = "fa-lightbulb",
                    Title           = "Align Buying Decisions with Demand Forecasting",
                    Explanation     = "No critical overproduction signals detected in the current period. " +
                                      "Use this stable period to build demand-forecasting baselines for future buying cycles.",
                    SuggestedAction = "Connect Forecasting module demand predictions to open-to-buy planning. " +
                                      "Set a rule: buy quantities should not exceed 110% of forecast demand to maintain sustainability targets.",
                    FinancialImpact = 0m,
                    ImpactLabel     = "Process Improvement",
                    DataBadge       = "Forecasting"
                });
            }

            vm.ActNowRecs     = actNow.Take(3).ToList();
            vm.MonitorRecs    = monitor.Take(3).ToList();
            vm.PlanBetterRecs = planBetter.Take(3).ToList();
        }

        // ── Private record for per-SKU working data ───────────────────────
        private sealed record SkuMetric(
            int ProductId, string ProductName, string ProductCode,
            string Category, string SeasonName, bool IsSeasonal,
            decimal BaseCost, int CurrentStock, int SoldUnits,
            decimal SellThroughRate, int DaysToEnd, decimal AtRiskValue,
            bool IsDeadStock, decimal MarkdownDependency, int WasteRiskScore,
            string StoreName);
    }
}
