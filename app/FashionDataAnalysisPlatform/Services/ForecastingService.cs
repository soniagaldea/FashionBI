using System.Diagnostics;
using System.Text.Json;
using FashionDataAnalysisPlatform.Data;
using FashionDataAnalysisPlatform.Models;
using FashionDataAnalysisPlatform.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FashionDataAnalysisPlatform.Services
{
    public class ForecastingService
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ForecastingService> _logger;

        public ForecastingService(AppDbContext context, IWebHostEnvironment env, ILogger<ForecastingService> logger)
        {
            _context = context;
            _env = env;
            _logger = logger;
        }

        // ── Trigger Python ML script ──────────────────────────────────────────
        public async Task<(bool Success, string Message)> TriggerRefreshAsync()
        {
            var mlDir      = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "ml"));
            var scriptPath = Path.Combine(mlDir, "fashion_forecaster.py");

            if (!File.Exists(scriptPath))
                return (false, $"Script not found at: {scriptPath}. Ensure the ml/ folder exists.");

            _logger.LogInformation("Starting forecast generation: {ScriptPath}", scriptPath);

            var psi = new ProcessStartInfo
            {
                FileName               = "python",
                Arguments              = $"\"{scriptPath}\"",
                WorkingDirectory       = mlDir,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };

            using var process = new Process { StartInfo = psi };

            try
            {
                process.Start();

                using var cts  = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask  = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync(cts.Token);

                var output = await outputTask;
                var error  = await errorTask;

                _logger.LogInformation("Forecast script output:\n{Output}", output);

                if (process.ExitCode != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(error) ? output : error;
                    _logger.LogError("Forecast script failed (exit {Code}):\n{Detail}", process.ExitCode, detail);
                    return (false,
                        $"Forecast generation failed. Ensure Python and required packages (scikit-learn, pandas, pyodbc) are installed.\n\n{detail.Trim()}");
                }

                return (true, "Forecasts generated successfully.");
            }
            catch (OperationCanceledException)
            {
                return (false, "Forecast generation timed out after 5 minutes.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception invoking forecast script");
                return (false,
                    $"Could not start Python. Ensure Python is installed and available on PATH.\n\n{ex.Message}");
            }
        }

        // ── Build the full ViewModel ──────────────────────────────────────────
        public async Task<ForecastingViewModel> BuildViewModelAsync(string? storeName, string? category)
        {
            storeName = string.IsNullOrWhiteSpace(storeName) ? null : storeName;
            category  = string.IsNullOrWhiteSpace(category)  ? null : category;

            var vm = new ForecastingViewModel
            {
                SelectedStore    = storeName,
                SelectedCategory = category
            };

            vm.AvailableStores = await _context.Stores
                .AsNoTracking().Select(s => s.StoreName).Distinct().OrderBy(s => s).ToListAsync();

            vm.AvailableCategories = await _context.Products
                .AsNoTracking().Where(p => p.Category != null).Select(p => p.Category!)
                .Distinct().OrderBy(c => c).ToListAsync();

            var latestResult = await _context.ForecastResults
                .AsNoTracking().OrderByDescending(f => f.GeneratedAt).FirstOrDefaultAsync();

            if (latestResult == null) { vm.HasForecasts = false; return vm; }

            vm.HasForecasts    = true;
            vm.LastGeneratedAt = latestResult.GeneratedAt;
            vm.ShowStoreChart  = storeName == null;
            vm.SelectedModel   = latestResult.ModelName ?? "Random Forest";

            var forecastQuery = _context.ForecastResults.AsNoTracking().AsQueryable();
            if (storeName != null) forecastQuery = forecastQuery.Where(f => f.StoreName == storeName);
            if (category  != null) forecastQuery = forecastQuery.Where(f => f.Category  == category);

            // ── Forecast dates ───────────────────────────────────────────────
            var rawMonths = await forecastQuery
                .Select(f => new { f.ForecastMonth.Year, f.ForecastMonth.Month })
                .Distinct().OrderBy(m => m.Year).ThenBy(m => m.Month).Take(3).ToListAsync();

            if (!rawMonths.Any()) { vm.HasForecasts = false; return vm; }

            var forecastDates = rawMonths.Select(m => new DateTime(m.Year, m.Month, 1)).ToList();
            var next          = forecastDates[0];
            vm.NextMonthLabel = next.ToString("MMMM yyyy");

            var nextMonthAgg = await forecastQuery
                .Where(f => f.ForecastMonth.Year == next.Year && f.ForecastMonth.Month == next.Month)
                .GroupBy(_ => 1)
                .Select(g => new { Revenue = g.Sum(x => x.RevenueForecast), Orders = g.Sum(x => x.OrdersForecast), Units = g.Sum(x => x.UnitsForecast) })
                .FirstOrDefaultAsync();

            vm.NextMonthRevenue      = nextMonthAgg?.Revenue ?? 0;
            vm.NextMonthOrders       = nextMonthAgg?.Orders  ?? 0;
            vm.NextMonthUnits        = nextMonthAgg?.Units   ?? 0;
            vm.NextThreeMonthRevenue = await forecastQuery.SumAsync(f => (decimal?)f.RevenueForecast) ?? 0m;

            // ── Accuracy ─────────────────────────────────────────────────────
            var latestAccAt = await _context.ForecastAccuracies.AsNoTracking().MaxAsync(a => (DateTime?)a.GeneratedAt);
            var allAccRows  = latestAccAt.HasValue
                ? await _context.ForecastAccuracies.AsNoTracking().Where(a => a.GeneratedAt == latestAccAt.Value).ToListAsync()
                : new List<ForecastAccuracy>();

            var bestRows = allAccRows.Where(a => a.ModelName == vm.SelectedModel).ToList();
            if (!bestRows.Any()) bestRows = allAccRows.Take(3).ToList();

            var revAcc = bestRows.FirstOrDefault(a => a.Target == "Revenue");
            var ordAcc = bestRows.FirstOrDefault(a => a.Target == "Orders");
            var untAcc = bestRows.FirstOrDefault(a => a.Target == "Units");

            vm.RevenueMAE              = revAcc?.MAE             ?? 0;
            vm.RevenueRMSE             = revAcc?.RMSE            ?? 0;
            vm.RevenueMAPE             = revAcc?.MAPE            ?? 0;
            vm.ForecastAccuracyPercent = revAcc?.AccuracyPercent ?? 0;
            vm.OrdersAccuracyPercent   = ordAcc?.AccuracyPercent ?? 0;
            vm.UnitsAccuracyPercent    = untAcc?.AccuracyPercent ?? 0;

            vm.ConfidenceLabel = vm.ForecastAccuracyPercent >= 70 ? "High Confidence"
                               : vm.ForecastAccuracyPercent >= 50 ? "Medium Confidence"
                               : "Low Confidence";
            vm.ConfidenceColorClass = vm.ForecastAccuracyPercent >= 70 ? "emerald"
                                    : vm.ForecastAccuracyPercent >= 50 ? "amber" : "red";

            vm.ModelComparison = allAccRows
                .Where(a => a.Target == "Revenue" && a.ModelName != null)
                .OrderBy(a => a.MAPE)
                .Select(a => new ModelComparisonEntry
                {
                    ModelName       = a.ModelName!,
                    RevenueMAPE     = a.MAPE,
                    RevenueAccuracy = a.AccuracyPercent,
                    IsSelected      = a.ModelName == vm.SelectedModel
                }).ToList();

            // ── YoY ──────────────────────────────────────────────────────────
            await BuildYoYAsync(vm, forecastDates, storeName, category);

            // ── Chart data ───────────────────────────────────────────────────
            BuildActualVsForecastLabels(vm, forecastDates);
            await FillActualVsForecastAsync(vm, forecastQuery, forecastDates, storeName, category);

            // ── Growth charts ─────────────────────────────────────────────────
            await BuildGrowthChartsAsync(vm, forecastQuery, storeName, category);

            // ── Feature Importance + Driver Groups ───────────────────────────
            var importances = await _context.ForecastFeatureImportances
                .AsNoTracking().Where(f => f.Target == "Revenue")
                .OrderByDescending(f => f.Importance).Take(14).ToListAsync();

            vm.FeatureNamesJson      = JsonSerializer.Serialize(importances.Select(f => f.FeatureName).ToArray());
            vm.FeatureImportancesJson = JsonSerializer.Serialize(importances.Select(f => Math.Round((double)f.Importance * 100, 2)).ToArray());
            BuildFeatureInterpretation(vm, importances);
            BuildDriverGroups(vm, importances);

            // ── 3-month rows ──────────────────────────────────────────────────
            await BuildForecastMonthRowsAsync(vm, forecastQuery, forecastDates, storeName, category);

            // ── Inventory alerts ──────────────────────────────────────────────
            await BuildInventoryAlertsAsync(vm, forecastQuery, storeName, category);

            // ── Revenue change ────────────────────────────────────────────────
            await BuildRevenueChangeAsync(vm, forecastQuery, storeName, category, forecastDates);

            // ── Track record ──────────────────────────────────────────────────
            await BuildTrackRecordAsync(vm);

            // ── Strategic Outlook ─────────────────────────────────────────────
            await BuildStrategicOutlookAsync(vm, forecastQuery, storeName, category, next);

            // ── Dynamic subtitle (needs all data above) ────────────────────────
            BuildDynamicSubtitle(vm);

            return vm;
        }

        // ── Chart labels ─────────────────────────────────────────────────────
        private static void BuildActualVsForecastLabels(ForecastingViewModel vm, List<DateTime> forecastDates)
        {
            var labels = new List<string>();
            for (int i = 11; i >= 0; i--)
                labels.Add(DateTime.Today.AddMonths(-i).ToString("MMM yy"));
            foreach (var d in forecastDates) labels.Add(d.ToString("MMM yy"));
            vm.AllLabelsJson = JsonSerializer.Serialize(labels.ToArray());
        }

        // ── Actual vs Forecast + Confidence Bands ────────────────────────────
        private async Task FillActualVsForecastAsync(
            ForecastingViewModel vm,
            IQueryable<ForecastResult> forecastQuery,
            List<DateTime> forecastDates,
            string? storeName, string? category)
        {
            var cutoff     = DateTime.Today.AddMonths(-12);
            var salesQuery = _context.Sales.AsNoTracking().Include(s => s.Store).Include(s => s.Product)
                .Where(s => s.SaleDate >= cutoff && s.StoreId != null);

            if (storeName != null) salesQuery = salesQuery.Where(s => s.Store!.StoreName == storeName);
            if (category  != null) salesQuery = salesQuery.Where(s => s.Product!.Category == category);

            var actualsByMonth = await salesQuery
                .GroupBy(s => new { s.SaleDate.Year, s.SaleDate.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(x => x.Revenue) })
                .ToListAsync();

            var actualValues = new List<decimal?>();
            for (int i = 11; i >= 0; i--)
            {
                var m     = DateTime.Today.AddMonths(-i);
                var match = actualsByMonth.FirstOrDefault(a => a.Year == m.Year && a.Month == m.Month);
                actualValues.Add(match?.Revenue ?? 0m);
            }

            var actualDataset   = actualValues.Concat(new decimal?[] { null, null, null }).ToArray();
            var forecastDataset = new decimal?[15];
            var upperBound      = new decimal?[15];
            var lowerBound      = new decimal?[15];

            forecastDataset[11] = actualValues[11];

            var errorFraction = Math.Max((double)vm.RevenueMAPE / 100.0, 0.05);

            for (int i = 0; i < forecastDates.Count; i++)
            {
                var fd  = forecastDates[i];
                var sum = await forecastQuery
                    .Where(f => f.ForecastMonth.Year == fd.Year && f.ForecastMonth.Month == fd.Month)
                    .SumAsync(f => (decimal?)f.RevenueForecast) ?? 0m;

                var rounded        = Math.Round(sum, 2);
                forecastDataset[12 + i] = rounded;
                upperBound[12 + i]      = Math.Round(rounded * (1m + (decimal)errorFraction), 2);
                lowerBound[12 + i]      = Math.Max(0m, Math.Round(rounded * (1m - (decimal)errorFraction), 2));
            }

            vm.ActualDatasetJson     = JsonSerializer.Serialize(actualDataset);
            vm.ForecastDatasetJson   = JsonSerializer.Serialize(forecastDataset);
            vm.UpperBoundDatasetJson = JsonSerializer.Serialize(upperBound);
            vm.LowerBoundDatasetJson = JsonSerializer.Serialize(lowerBound);
        }

        // ── YoY ──────────────────────────────────────────────────────────────
        private async Task BuildYoYAsync(
            ForecastingViewModel vm, List<DateTime> forecastDates,
            string? storeName, string? category)
        {
            var next    = forecastDates[0];
            var yoyDate = new DateTime(next.Year - 1, next.Month, 1);
            vm.SameMonthLastYearLabel = yoyDate.ToString("MMM yyyy");

            var yoySalesQ = _context.Sales.AsNoTracking().Include(s => s.Store).Include(s => s.Product)
                .Where(s => s.SaleDate.Year == yoyDate.Year && s.SaleDate.Month == yoyDate.Month && s.StoreId != null);
            if (storeName != null) yoySalesQ = yoySalesQ.Where(s => s.Store!.StoreName == storeName);
            if (category  != null) yoySalesQ = yoySalesQ.Where(s => s.Product!.Category == category);

            var yoyAgg = await yoySalesQ.GroupBy(_ => 1)
                .Select(g => new {
                    Revenue = g.Sum(x => x.Revenue),
                    Orders  = g.Select(x => x.ExternalOrderId).Distinct().Count(),
                    Units   = g.Sum(x => x.Quantity) })
                .FirstOrDefaultAsync();

            vm.SameMonthLastYearRevenue = yoyAgg?.Revenue ?? 0;
            vm.SameMonthLastYearOrders  = yoyAgg?.Orders  ?? 0;
            vm.SameMonthLastYearUnits   = yoyAgg?.Units   ?? 0;
            vm.YoYNextMonthRevenueDelta = vm.NextMonthRevenue - vm.SameMonthLastYearRevenue;

            if (vm.SameMonthLastYearRevenue > 0)
                vm.YoYNextMonthRevenuePct = Math.Round(vm.YoYNextMonthRevenueDelta / vm.SameMonthLastYearRevenue * 100m, 1);
            if (vm.SameMonthLastYearOrders > 0)
                vm.YoYNextMonthOrdersPct = Math.Round(((decimal)vm.NextMonthOrders - vm.SameMonthLastYearOrders) / vm.SameMonthLastYearOrders * 100m, 1);
            if (vm.SameMonthLastYearUnits > 0)
                vm.YoYNextMonthUnitsPct = Math.Round(((decimal)vm.NextMonthUnits - vm.SameMonthLastYearUnits) / vm.SameMonthLastYearUnits * 100m, 1);

            // 3-month YoY
            var periods = forecastDates.Select(d => new DateTime(d.Year - 1, d.Month, 1)).ToList();
            vm.ThreeMonthComparisonLabel = $"{periods[0]:MMM}–{periods[^1]:MMM yyyy}";

            var yoy3Q = _context.Sales.AsNoTracking().Include(s => s.Store).Include(s => s.Product)
                .Where(s => s.StoreId != null && periods.Any(p => s.SaleDate.Year == p.Year && s.SaleDate.Month == p.Month));
            if (storeName != null) yoy3Q = yoy3Q.Where(s => s.Store!.StoreName == storeName);
            if (category  != null) yoy3Q = yoy3Q.Where(s => s.Product!.Category == category);

            vm.SameThreeMonthsLastYearRevenue = await yoy3Q.SumAsync(s => (decimal?)s.Revenue) ?? 0m;
            vm.YoYThreeMonthRevenueDelta       = vm.NextThreeMonthRevenue - vm.SameThreeMonthsLastYearRevenue;

            if (vm.SameThreeMonthsLastYearRevenue > 0)
                vm.YoYThreeMonthRevenuePct = Math.Round(vm.YoYThreeMonthRevenueDelta / vm.SameThreeMonthsLastYearRevenue * 100m, 1);
        }

        // ── Growth Charts (% change vs prior 3 months) ────────────────────────
        private async Task BuildGrowthChartsAsync(
            ForecastingViewModel vm, IQueryable<ForecastResult> forecastQuery,
            string? storeName, string? category)
        {
            var threeMonthsAgo = DateTime.Today.AddMonths(-3);

            // Category growth
            var actCatQ = _context.Sales.AsNoTracking().Include(s => s.Store).Include(s => s.Product)
                .Where(s => s.SaleDate >= threeMonthsAgo && s.Product != null && s.StoreId != null);
            if (storeName != null) actCatQ = actCatQ.Where(s => s.Store!.StoreName == storeName);
            if (category  != null) actCatQ = actCatQ.Where(s => s.Product!.Category == category);

            var actualByCat = await actCatQ
                .GroupBy(s => s.Product!.Category!)
                .Select(g => new { Category = g.Key, Revenue = g.Sum(x => x.Revenue) })
                .ToDictionaryAsync(x => x.Category, x => x.Revenue);

            var forecastByCat = await forecastQuery
                .GroupBy(f => f.Category)
                .Select(g => new { Category = g.Key, Revenue = g.Sum(x => x.RevenueForecast) })
                .ToDictionaryAsync(x => x.Category, x => x.Revenue);

            var catGrowth = forecastByCat.Keys
                .Where(cat => actualByCat.ContainsKey(cat) && actualByCat[cat] > 0)
                .Select(cat => new {
                    Category  = cat,
                    GrowthPct = Math.Round((double)((forecastByCat[cat] - actualByCat[cat]) / actualByCat[cat] * 100m), 1)
                })
                .OrderByDescending(x => x.GrowthPct)
                .ToList();

            vm.CategoryGrowthLabelsJson = JsonSerializer.Serialize(catGrowth.Select(x => x.Category).ToArray());
            vm.CategoryGrowthValuesJson  = JsonSerializer.Serialize(catGrowth.Select(x => x.GrowthPct).ToArray());

            // Store growth (all-stores view only)
            if (!vm.ShowStoreChart) return;

            var actStoreQ = _context.Sales.AsNoTracking().Include(s => s.Store).Include(s => s.Product)
                .Where(s => s.SaleDate >= threeMonthsAgo && s.StoreId != null && s.Store != null);
            if (category != null) actStoreQ = actStoreQ.Where(s => s.Product!.Category == category);

            var actualByStore = await actStoreQ
                .GroupBy(s => s.Store!.StoreName!)
                .Select(g => new { Store = g.Key, Revenue = g.Sum(x => x.Revenue) })
                .ToDictionaryAsync(x => x.Store, x => x.Revenue);

            var fcstStoreQ = _context.ForecastResults.AsNoTracking().AsQueryable();
            if (category != null) fcstStoreQ = fcstStoreQ.Where(f => f.Category == category);

            var forecastByStore = await fcstStoreQ
                .GroupBy(f => f.StoreName)
                .Select(g => new { Store = g.Key, Revenue = g.Sum(x => x.RevenueForecast) })
                .ToDictionaryAsync(x => x.Store, x => x.Revenue);

            var storeGrowth = forecastByStore.Keys
                .Where(s => actualByStore.ContainsKey(s) && actualByStore[s] > 0)
                .Select(s => new {
                    Store     = s,
                    GrowthPct = Math.Round((double)((forecastByStore[s] - actualByStore[s]) / actualByStore[s] * 100m), 1)
                })
                .OrderByDescending(x => x.GrowthPct)
                .ToList();

            vm.StoreGrowthLabelsJson = JsonSerializer.Serialize(storeGrowth.Select(x => x.Store).ToArray());
            vm.StoreGrowthValuesJson  = JsonSerializer.Serialize(storeGrowth.Select(x => x.GrowthPct).ToArray());
        }

        // ── 3-month Forecast Rows ────────────────────────────────────────────
        private async Task BuildForecastMonthRowsAsync(
            ForecastingViewModel vm, IQueryable<ForecastResult> forecastQuery,
            List<DateTime> forecastDates, string? storeName, string? category)
        {
            var prevMonth = forecastDates[0].AddMonths(-1);
            var prevQ     = _context.Sales.AsNoTracking().Include(s => s.Store).Include(s => s.Product)
                .Where(s => s.SaleDate.Year == prevMonth.Year && s.SaleDate.Month == prevMonth.Month && s.StoreId != null);
            if (storeName != null) prevQ = prevQ.Where(s => s.Store!.StoreName == storeName);
            if (category  != null) prevQ = prevQ.Where(s => s.Product!.Category == category);

            var prevRevenue = await prevQ.SumAsync(s => (decimal?)s.Revenue) ?? 0m;

            var tags = new[] { "M+1", "M+2", "M+3" };
            for (int i = 0; i < forecastDates.Count; i++)
            {
                var fd  = forecastDates[i];
                var agg = await forecastQuery
                    .Where(f => f.ForecastMonth.Year == fd.Year && f.ForecastMonth.Month == fd.Month)
                    .GroupBy(_ => 1)
                    .Select(g => new { Revenue = g.Sum(x => x.RevenueForecast), Orders = g.Sum(x => x.OrdersForecast), Units = g.Sum(x => x.UnitsForecast) })
                    .FirstOrDefaultAsync();

                var rev = agg?.Revenue ?? 0m;
                decimal? pct = null;

                if (i == 0 && prevRevenue > 0)
                    pct = Math.Round((rev - prevRevenue) / prevRevenue * 100m, 1);
                else if (i > 0 && vm.ForecastMonthRows[i - 1].Revenue > 0)
                    pct = Math.Round((rev - vm.ForecastMonthRows[i - 1].Revenue) / vm.ForecastMonthRows[i - 1].Revenue * 100m, 1);

                vm.ForecastMonthRows.Add(new ForecastMonthRow
                {
                    Tag                  = tags[i],
                    Label                = fd.ToString("MMM yyyy"),
                    Revenue              = Math.Round(rev, 0),
                    Orders               = agg?.Orders ?? 0,
                    Units                = agg?.Units  ?? 0,
                    RevenuePctVsPrevMonth = pct
                });
            }
        }

        // ── Inventory Alerts + Revenue at Risk ───────────────────────────────
        private async Task BuildInventoryAlertsAsync(
            ForecastingViewModel vm, IQueryable<ForecastResult> forecastQuery,
            string? storeName, string? category)
        {
            var unitsByStoreCat = await forecastQuery
                .GroupBy(f => new { f.StoreName, f.Category })
                .Select(g => new {
                    g.Key.StoreName, g.Key.Category,
                    TotalForecastUnits = g.Sum(x => x.UnitsForecast),
                    TotalForecastRev   = g.Sum(x => x.RevenueForecast) })
                .ToListAsync();

            foreach (var fu in unitsByStoreCat)
            {
                var currentStock = await _context.Inventories.AsNoTracking()
                    .Include(i => i.Product).Include(i => i.Store)
                    .Where(i => i.Product != null && i.Product.Category == fu.Category
                             && i.Store  != null && i.Store.StoreName  == fu.StoreName)
                    .SumAsync(i => (int?)i.CurrentStock) ?? 0;

                if (fu.TotalForecastUnits <= currentStock) continue;

                var monthlyDemand  = fu.TotalForecastUnits / 3.0m;
                var coverageMonths = monthlyDemand > 0 ? Math.Round(currentStock / monthlyDemand, 1) : 0m;
                var coverageLabel  = coverageMonths < 1m ? "Critical" : coverageMonths < 2m ? "Warning" : "Healthy";
                var gap            = fu.TotalForecastUnits - currentStock;
                var avgPrice       = fu.TotalForecastUnits > 0 ? fu.TotalForecastRev / fu.TotalForecastUnits : 0m;
                var revenueAtRisk  = Math.Round(gap * avgPrice, 0);

                vm.InventoryAlerts.Add(new InventoryRiskAlert
                {
                    Category        = fu.Category,
                    StoreName       = fu.StoreName,
                    ForecastedUnits = fu.TotalForecastUnits,
                    CurrentStock    = currentStock,
                    StockGap        = gap,
                    CoverageMonths  = coverageMonths,
                    CoverageLabel   = coverageLabel,
                    RevenueAtRisk   = revenueAtRisk
                });
            }

            vm.InventoryAlerts    = vm.InventoryAlerts.OrderByDescending(a => a.RevenueAtRisk).ToList();
            vm.TotalRevenueAtRisk = vm.InventoryAlerts.Sum(a => a.RevenueAtRisk);
        }

        // ── Revenue change % ─────────────────────────────────────────────────
        private async Task BuildRevenueChangeAsync(
            ForecastingViewModel vm, IQueryable<ForecastResult> forecastQuery,
            string? storeName, string? category, List<DateTime> forecastDates)
        {
            var start    = forecastDates[0].AddMonths(-3);
            var end      = forecastDates[0];
            var prevQuery = _context.Sales.AsNoTracking().Include(s => s.Store).Include(s => s.Product)
                .Where(s => s.SaleDate >= start && s.SaleDate < end && s.StoreId != null);
            if (storeName != null) prevQuery = prevQuery.Where(s => s.Store!.StoreName == storeName);
            if (category  != null) prevQuery = prevQuery.Where(s => s.Product!.Category == category);

            vm.PreviousThreeMonthRevenue = await prevQuery.SumAsync(s => (decimal?)s.Revenue) ?? 0m;

            if (vm.PreviousThreeMonthRevenue > 0)
                vm.RevenueChangePercent = Math.Round(
                    (vm.NextThreeMonthRevenue - vm.PreviousThreeMonthRevenue) / vm.PreviousThreeMonthRevenue * 100m, 1);
        }

        // ── Forecast Track Record ────────────────────────────────────────────
        private async Task BuildTrackRecordAsync(ForecastingViewModel vm)
        {
            var runDates = await _context.ForecastAccuracies.AsNoTracking()
                .Select(a => a.GeneratedAt).Distinct()
                .OrderByDescending(d => d).Take(3).ToListAsync();

            foreach (var runDate in runDates)
            {
                var best = await _context.ForecastAccuracies.AsNoTracking()
                    .Where(a => a.GeneratedAt == runDate && a.Target == "Revenue")
                    .OrderBy(a => a.MAPE)
                    .FirstOrDefaultAsync();

                if (best != null)
                    vm.TrackRecord.Add(new ForecastTrackRecord
                    {
                        GeneratedAt     = runDate,
                        ModelName       = best.ModelName ?? "Unknown",
                        AccuracyPercent = best.AccuracyPercent,
                        MAPE            = best.MAPE
                    });
            }
        }

        // ── Driver Groups (business-language grouping of feature importances) ─
        private static void BuildDriverGroups(ForecastingViewModel vm, List<ForecastFeatureImportance> importances)
        {
            if (!importances.Any()) return;

            var total = (double)importances.Sum(f => f.Importance);
            if (total <= 0) return;

            double Group(params string[] keywords) =>
                (double)importances
                    .Where(f => keywords.Any(k => f.FeatureName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    .Sum(f => f.Importance);

            var momentum = Group("Lag-1", "Lag-3", "Lag-6");
            var yoy      = Group("Lag-12", "YoY");
            var seasonal = Group("Holiday", "Collection", "Summer", "Winter", "Month", "Quarter");
            var identity = total - momentum - yoy - seasonal;

            static int Pct(double val, double tot) => (int)Math.Round(val / tot * 100);

            var momentumPct = Pct(momentum, total);
            var yoyPct      = Pct(yoy,      total);
            var seasonalPct = Pct(seasonal,  total);
            var identityPct = Math.Max(0, 100 - momentumPct - yoyPct - seasonalPct);

            var groups = new[]
            {
                (Name: "Sales Momentum",
                 Desc: "Recent sales performance is the primary predictor of short-term demand. Strong recent sales signal continued demand; disruptions will increase forecast uncertainty.",
                 Pct:  momentumPct, Icon: "fa-bolt",          Color: "var(--indigo-600)"),
                (Name: "Historical Demand Trends",
                 Desc: "Year-over-year performance from the same calendar period last year provides seasonal baselines and long-run demand trajectory.",
                 Pct:  yoyPct,      Icon: "fa-rotate-left",   Color: "#10b981"),
                (Name: "Seasonal Patterns",
                 Desc: "Recurring seasonal behaviour — holiday periods, summer peaks, and collection launches — creates predictable demand cycles.",
                 Pct:  seasonalPct, Icon: "fa-calendar-days", Color: "#f59e0b"),
                (Name: "Calendar & Identity",
                 Desc: "Month, quarter, store identity, and category characteristics provide structural context that shapes baseline demand levels.",
                 Pct:  identityPct, Icon: "fa-layer-group",   Color: "#8b5cf6"),
            };

            vm.DriverGroups = groups
                .Where(g => g.Pct > 0)
                .OrderByDescending(g => g.Pct)
                .Select(g => new ForecastDriverGroup
                {
                    Name        = g.Name,
                    Description = g.Desc,
                    Percentage  = g.Pct,
                    Icon        = g.Icon,
                    Color       = g.Color
                }).ToList();
        }

        // ── Feature Interpretation text ──────────────────────────────────────
        private static void BuildFeatureInterpretation(ForecastingViewModel vm, List<ForecastFeatureImportance> importances)
        {
            if (!importances.Any()) { vm.FeatureInterpretation = string.Empty; return; }

            var top     = importances.First();
            var topPct  = Math.Round((double)top.Importance * 100, 0);
            var topName = top.FeatureName;

            bool isShortLag = topName.Contains("Lag-1") || topName.Contains("Lag-3") || topName.Contains("Lag-6");
            bool isYoYTop   = topName.Contains("YoY")   || topName.Contains("Lag-12");
            bool isCalendar = topName == "Month"         || topName == "Quarter" || topName == "Year";

            var yoyTotal = importances.Where(f => f.FeatureName.Contains("YoY") || f.FeatureName.Contains("Lag-12"))
                                      .Sum(f => (double)f.Importance * 100);
            var shortLagTotal = importances.Where(f => (f.FeatureName.Contains("Lag-1") || f.FeatureName.Contains("Lag-3") ||
                                                         f.FeatureName.Contains("Lag-6")) && !f.FeatureName.Contains("Lag-12"))
                                           .Sum(f => (double)f.Importance * 100);
            var seasonalTotal = importances.Where(f => f.FeatureName.Contains("Month")   || f.FeatureName.Contains("Quarter") ||
                                                        f.FeatureName.Contains("Season")  || f.FeatureName.Contains("Holiday") ||
                                                        f.FeatureName.Contains("Collection"))
                                           .Sum(f => (double)f.Importance * 100);

            string mainDriver = isShortLag ? "recent sales momentum"
                              : isYoYTop   ? "year-over-year seasonal patterns"
                              : isCalendar ? "calendar effects"
                              : topName;

            var sb = new System.Text.StringBuilder();
            sb.Append($"The Revenue model is driven primarily by {mainDriver} ({topPct:N0}%), ");
            if (isShortLag) sb.Append("meaning that how well a store performed in recent months is the strongest predictor of near-term performance. ");
            else if (isYoYTop) sb.Append("confirming that annual retail cycles repeat reliably across the training data. ");
            else if (isCalendar) sb.Append("reflecting the strong seasonal structure of fashion retail demand. ");
            else sb.Append("which has the dominant influence on the model output. ");
            if (!isYoYTop && yoyTotal > 5)
                sb.Append($"Year-over-year features contribute a combined {Math.Round(yoyTotal, 0):N0}%, confirming detectable annual seasonality. ");
            if (!isShortLag && shortLagTotal > 10)
                sb.Append($"Short-term lag features account for {Math.Round(shortLagTotal, 0):N0}%, capturing sales momentum. ");
            if (seasonalTotal > 15)
                sb.Append($"Seasonal indicators collectively account for {Math.Round(seasonalTotal, 0):N0}% — consistent with fashion retail demand cycles. ");
            sb.Append(topPct > 40
                ? "The high concentration in a single feature suggests the model may be sensitive to recent performance disruptions."
                : "The balanced weight distribution suggests the model generalises well and is not overly dependent on any single signal.");

            vm.FeatureInterpretation = sb.ToString();
        }

        // ── Strategic Outlook ────────────────────────────────────────────────
        private async Task BuildStrategicOutlookAsync(
            ForecastingViewModel vm, IQueryable<ForecastResult> forecastQuery,
            string? storeName, string? category, DateTime nextForecastDate)
        {
            var items          = new List<StrategicOutlookItem>();
            var threeMonthsAgo = DateTime.Today.AddMonths(-3);

            // Critical stock-outs
            foreach (var alert in vm.InventoryAlerts.Where(a => a.CoverageLabel == "Critical").Take(3))
                items.Add(new StrategicOutlookItem
                {
                    Severity    = "Urgent",
                    Icon        = "fa-triangle-exclamation",
                    Title       = $"Stock-Out Risk — {alert.Category} at {alert.StoreName}",
                    Body        = $"Forecasted demand ({alert.ForecastedUnits:N0} units) exceeds stock ({alert.CurrentStock:N0} units) with less than one month of coverage.",
                    Impact      = alert.RevenueAtRisk > 0 ? $"€{alert.RevenueAtRisk:N0} revenue at risk" : "Immediate stock-out exposure",
                    Action      = $"Raise replenishment order for {alert.Category} at {alert.StoreName} immediately. Gap: {alert.StockGap:N0} units.",
                    ImpactValue = alert.RevenueAtRisk
                });

            // Warning shortfalls
            foreach (var alert in vm.InventoryAlerts.Where(a => a.CoverageLabel == "Warning").OrderByDescending(a => a.RevenueAtRisk).Take(2))
                items.Add(new StrategicOutlookItem
                {
                    Severity    = "Urgent",
                    Icon        = "fa-boxes-stacked",
                    Title       = $"Low Stock Warning — {alert.Category} at {alert.StoreName}",
                    Body        = $"Stock covers {alert.CoverageMonths:N1} months of forecasted demand. Shortfall of {alert.StockGap:N0} units projected over 3 months.",
                    Impact      = alert.RevenueAtRisk > 0 ? $"€{alert.RevenueAtRisk:N0} revenue at risk" : "Replenishment required",
                    Action      = $"Schedule replenishment for {alert.Category} at {alert.StoreName} within 4 weeks.",
                    ImpactValue = alert.RevenueAtRisk
                });

            // Revenue trend
            if (vm.RevenueChangePercent.HasValue)
            {
                var pct   = vm.RevenueChangePercent.Value;
                var delta = Math.Abs(vm.NextThreeMonthRevenue - vm.PreviousThreeMonthRevenue);
                if (pct >= 8)
                    items.Add(new StrategicOutlookItem
                    {
                        Severity    = "Opportunity",
                        Icon        = "fa-arrow-trend-up",
                        Title       = "Revenue Growth Forecast",
                        Body        = $"Total revenue is forecast to increase by {pct:N1}% (€{vm.PreviousThreeMonthRevenue:N0} → €{vm.NextThreeMonthRevenue:N0}) over the next 3 months.",
                        Impact      = $"+€{delta:N0} vs prior 3 months",
                        Action      = "Align purchase orders with the expected volume uplift. Review open buy budgets to avoid leaving demand unfulfilled.",
                        ImpactValue = delta
                    });
                else if (pct <= -8)
                    items.Add(new StrategicOutlookItem
                    {
                        Severity    = "Urgent",
                        Icon        = "fa-arrow-trend-down",
                        Title       = "Revenue Decline Forecast",
                        Body        = $"Total revenue is forecast to decline by {Math.Abs(pct):N1}% (€{vm.PreviousThreeMonthRevenue:N0} → €{vm.NextThreeMonthRevenue:N0}) over the next 3 months.",
                        Impact      = $"−€{delta:N0} vs prior 3 months",
                        Action      = "Review open purchase orders. Consider deferring non-critical replenishment and assessing markdown strategy for slow movers.",
                        ImpactValue = delta
                    });
                else if (pct is > 3 and < 8)
                    items.Add(new StrategicOutlookItem
                    {
                        Severity    = "Watch",
                        Icon        = "fa-chart-line",
                        Title       = "Moderate Revenue Growth",
                        Body        = $"Revenue is forecast to grow {pct:N1}% over the next 3 months — steady but below high-growth threshold.",
                        Impact      = $"+€{delta:N0} vs prior 3 months",
                        Action      = "Maintain current replenishment cadence. Monitor actuals vs forecast weekly.",
                        ImpactValue = delta
                    });
            }

            // Category signals
            var actSalesQ = _context.Sales.AsNoTracking().Include(s => s.Store).Include(s => s.Product)
                .Where(s => s.SaleDate >= threeMonthsAgo && s.Product != null);
            if (storeName != null) actSalesQ = actSalesQ.Where(s => s.Store!.StoreName == storeName);
            if (category  != null) actSalesQ = actSalesQ.Where(s => s.Product!.Category == category);

            var actualByCat   = await actSalesQ.GroupBy(s => s.Product!.Category!).Select(g => new { Category = g.Key, Revenue = g.Sum(x => x.Revenue) }).ToDictionaryAsync(x => x.Category, x => x.Revenue);
            var forecastByCat = await forecastQuery.GroupBy(f => f.Category).Select(g => new { Category = g.Key, Revenue = g.Sum(x => x.RevenueForecast) }).ToDictionaryAsync(x => x.Category, x => x.Revenue);

            var growthByCat = forecastByCat.Keys
                .Where(cat => actualByCat.ContainsKey(cat) && actualByCat[cat] > 0)
                .Select(cat => new { Category = cat, GrowthPct = (double)((forecastByCat[cat] - actualByCat[cat]) / actualByCat[cat] * 100m), ImpactVal = forecastByCat[cat] - actualByCat[cat] })
                .ToList();

            var topCat = growthByCat.OrderByDescending(x => x.GrowthPct).FirstOrDefault();
            if (topCat != null && topCat.GrowthPct >= 8)
                items.Add(new StrategicOutlookItem
                {
                    Severity    = "Opportunity",
                    Icon        = "fa-arrow-trend-up",
                    Title       = $"Demand Growth — {topCat.Category}",
                    Body        = $"{topCat.Category} demand is forecast to increase by {topCat.GrowthPct:F0}% vs the prior 3-month period.",
                    Impact      = topCat.ImpactVal > 0 ? $"+€{topCat.ImpactVal:N0} projected" : null,
                    Action      = $"Prioritise stock replenishment for {topCat.Category} ahead of the forecast period.",
                    ImpactValue = (decimal)topCat.ImpactVal
                });

            var bottomCat = growthByCat.OrderBy(x => x.GrowthPct).FirstOrDefault();
            if (bottomCat != null && bottomCat.GrowthPct <= -8)
                items.Add(new StrategicOutlookItem
                {
                    Severity    = "Watch",
                    Icon        = "fa-arrow-trend-down",
                    Title       = $"Demand Decline — {bottomCat.Category}",
                    Body        = $"{bottomCat.Category} demand is forecast to drop by {Math.Abs(bottomCat.GrowthPct):F0}% vs the prior 3-month period.",
                    Impact      = $"−€{Math.Abs(bottomCat.ImpactVal):N0} projected",
                    Action      = $"Review open purchase orders for {bottomCat.Category}. Pause non-essential replenishment and assess markdown options.",
                    ImpactValue = (decimal)Math.Abs(bottomCat.ImpactVal)
                });

            // Top-store signal
            if (storeName == null)
            {
                var storeQ   = _context.ForecastResults.AsNoTracking().AsQueryable();
                if (category != null) storeQ = storeQ.Where(f => f.Category == category);
                var topStore = await storeQ.GroupBy(f => f.StoreName).Select(g => new { Store = g.Key, Revenue = g.Sum(x => x.RevenueForecast) }).OrderByDescending(x => x.Revenue).FirstOrDefaultAsync();
                if (topStore != null)
                    items.Add(new StrategicOutlookItem
                    {
                        Severity    = "Watch",
                        Icon        = "fa-building",
                        Title       = $"Top Revenue Location — {topStore.Store}",
                        Body        = $"{topStore.Store} is projected to generate €{topStore.Revenue:N0} over the next 3 months — the highest of all locations.",
                        Impact      = $"€{topStore.Revenue:N0} forecast revenue",
                        Action      = "Ensure stock allocation for this location is proportionate to its expected revenue contribution.",
                        ImpactValue = topStore.Revenue
                    });
            }

            // Seasonal context
            var m  = nextForecastDate.Month;
            (string? Title, string? Body, string? Action) seasonal = m switch
            {
                11 or 12 => ("Peak Holiday Season",
                             "The forecast window covers the holiday trading period. Elevated demand is typical across most fashion categories.",
                             "Confirm stock is in place across all stores before end of October. Prioritise occasion-wear and gifting categories."),
                6 or 7   => ("Summer Season Peak",
                             "Summer seasonal patterns are active. Lightweight and warm-weather categories typically see peak demand.",
                             "Monitor sell-through rates weekly. Identify slow movers early to plan timely markdowns before end of season."),
                2 or 3 or 4 => ("Spring-Summer Collection Launch",
                                "SS new arrivals typically drive increased store traffic and uplift in spring and summer categories.",
                                "Ensure SS collection deliveries are on schedule. Prioritise floor space for new arrivals."),
                8 or 9 or 10 => ("Autumn-Winter Collection Period",
                                 "AW new arrivals are approaching. Outerwear and layering categories typically see significant demand uplift.",
                                 "Plan AW replenishment now to avoid early-season stockouts. Confirm delivery schedules with suppliers."),
                _ => (null, null, null)
            };

            if (seasonal.Title != null)
                items.Add(new StrategicOutlookItem
                {
                    Severity    = "Watch",
                    Icon        = "fa-calendar-days",
                    Title       = seasonal.Title,
                    Body        = seasonal.Body!,
                    Impact      = "Seasonal demand signal active",
                    Action      = seasonal.Action!,
                    ImpactValue = 0
                });

            // Urgent first, then Opportunity, then Watch; within each tier by financial impact desc
            vm.StrategicOutlook = items
                .OrderBy(i => i.Severity == "Urgent" ? 0 : i.Severity == "Opportunity" ? 1 : 2)
                .ThenByDescending(i => i.ImpactValue)
                .Take(6).ToList();
        }

        // ── Dynamic Subtitle ─────────────────────────────────────────────────
        private static void BuildDynamicSubtitle(ForecastingViewModel vm)
        {
            var parts = new List<string>();

            if (vm.YoYThreeMonthRevenuePct.HasValue)
            {
                var dir = vm.YoYThreeMonthRevenuePct.Value >= 0 ? "grow" : "decline";
                var pct = Math.Abs(vm.YoYThreeMonthRevenuePct.Value);
                parts.Add($"Forecast revenue is expected to {dir} {pct:N1}% vs the same period last year");
            }
            else if (vm.RevenueChangePercent.HasValue)
            {
                var dir = vm.RevenueChangePercent.Value >= 0 ? "grow" : "decline";
                var pct = Math.Abs(vm.RevenueChangePercent.Value);
                parts.Add($"Forecast revenue is expected to {dir} {pct:N1}% vs the prior 3 months");
            }

            if (vm.TotalRevenueAtRisk > 0)
                parts.Add($"current stock constraints place €{vm.TotalRevenueAtRisk:N0} of revenue at risk");

            vm.DynamicSubtitle = parts.Count > 0
                ? char.ToUpper(parts[0][0]) + parts[0][1..] +
                  (parts.Count > 1 ? $", but {parts[1]}" : string.Empty) + "."
                : string.Empty;
        }
    }
}
