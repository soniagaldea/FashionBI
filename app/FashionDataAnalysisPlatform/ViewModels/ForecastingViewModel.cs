namespace FashionDataAnalysisPlatform.ViewModels
{
    public class ForecastingViewModel
    {
        // Page state
        public bool HasForecasts { get; set; }
        public DateTime? LastGeneratedAt { get; set; }
        public string DynamicSubtitle { get; set; } = string.Empty;

        // Filter selections
        public string? SelectedStore { get; set; }
        public string? SelectedCategory { get; set; }
        public List<string> AvailableStores { get; set; } = new();
        public List<string> AvailableCategories { get; set; } = new();

        // KPI cards — next forecast month
        public string NextMonthLabel { get; set; } = string.Empty;
        public decimal NextMonthRevenue { get; set; }
        public int NextMonthOrders { get; set; }
        public int NextMonthUnits { get; set; }
        public decimal ForecastAccuracyPercent { get; set; }

        // YoY for next-month KPI cards
        public string SameMonthLastYearLabel { get; set; } = string.Empty;
        public decimal SameMonthLastYearRevenue { get; set; }
        public decimal YoYNextMonthRevenueDelta { get; set; }
        public decimal? YoYNextMonthRevenuePct { get; set; }
        public int SameMonthLastYearOrders { get; set; }
        public decimal? YoYNextMonthOrdersPct { get; set; }
        public int SameMonthLastYearUnits { get; set; }
        public decimal? YoYNextMonthUnitsPct { get; set; }

        // 4th KPI card: 3-month forecast vs same 3 months last year
        public decimal SameThreeMonthsLastYearRevenue { get; set; }
        public decimal YoYThreeMonthRevenueDelta { get; set; }
        public decimal? YoYThreeMonthRevenuePct { get; set; }
        public string ThreeMonthComparisonLabel { get; set; } = string.Empty;

        // Confidence / model quality
        public string ConfidenceLabel { get; set; } = string.Empty;
        public string ConfidenceColorClass { get; set; } = string.Empty;

        // Revenue change vs prior 3 months
        public decimal NextThreeMonthRevenue { get; set; }
        public decimal PreviousThreeMonthRevenue { get; set; }
        public decimal? RevenueChangePercent { get; set; }

        // 3-month outlook table
        public List<ForecastMonthRow> ForecastMonthRows { get; set; } = new();

        // Actual vs Forecast chart (15 points: 12 actuals + 3 forecast)
        public string AllLabelsJson { get; set; } = "[]";
        public string ActualDatasetJson { get; set; } = "[]";
        public string ForecastDatasetJson { get; set; } = "[]";
        public string UpperBoundDatasetJson { get; set; } = "[]";
        public string LowerBoundDatasetJson { get; set; } = "[]";

        // Growth charts — % change vs prior 3 months
        public string CategoryGrowthLabelsJson { get; set; } = "[]";
        public string CategoryGrowthValuesJson { get; set; } = "[]";
        public string StoreGrowthLabelsJson { get; set; } = "[]";
        public string StoreGrowthValuesJson { get; set; } = "[]";

        // Feature Importance / Forecast Drivers
        public string FeatureNamesJson { get; set; } = "[]";
        public string FeatureImportancesJson { get; set; } = "[]";
        public string FeatureInterpretation { get; set; } = string.Empty;
        public List<ForecastDriverGroup> DriverGroups { get; set; } = new();

        // Accuracy metrics
        public decimal RevenueMAE { get; set; }
        public decimal RevenueRMSE { get; set; }
        public decimal RevenueMAPE { get; set; }
        public decimal OrdersAccuracyPercent { get; set; }
        public decimal UnitsAccuracyPercent { get; set; }

        // Model selection
        public string SelectedModel { get; set; } = "Random Forest";
        public List<ModelComparisonEntry> ModelComparison { get; set; } = new();

        // Forecast track record
        public List<ForecastTrackRecord> TrackRecord { get; set; } = new();

        // Strategic Outlook
        public List<StrategicOutlookItem> StrategicOutlook { get; set; } = new();

        // Inventory risk alerts
        public List<InventoryRiskAlert> InventoryAlerts { get; set; } = new();
        public decimal TotalRevenueAtRisk { get; set; }

        // Store chart visibility
        public bool ShowStoreChart { get; set; }
    }

    public class ForecastMonthRow
    {
        public string Tag { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public int Orders { get; set; }
        public int Units { get; set; }
        public decimal? RevenuePctVsPrevMonth { get; set; }
    }

    public class ForecastDriverGroup
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Percentage { get; set; }
        public string Icon { get; set; } = string.Empty;
        public string Color { get; set; } = "var(--indigo-600)";
    }

    public class ForecastTrackRecord
    {
        public DateTime GeneratedAt { get; set; }
        public string ModelName { get; set; } = string.Empty;
        public decimal AccuracyPercent { get; set; }
        public decimal MAPE { get; set; }
    }

    public class StrategicOutlookItem
    {
        public string Severity { get; set; } = "Watch";
        public string Icon { get; set; } = "fa-eye";
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? Impact { get; set; }
        public string Action { get; set; } = string.Empty;
        public decimal ImpactValue { get; set; }
    }

    public class InventoryRiskAlert
    {
        public string Category { get; set; } = string.Empty;
        public string StoreName { get; set; } = string.Empty;
        public int ForecastedUnits { get; set; }
        public int CurrentStock { get; set; }
        public int StockGap { get; set; }
        public decimal CoverageMonths { get; set; }
        public string CoverageLabel { get; set; } = string.Empty;
        public decimal RevenueAtRisk { get; set; }
    }

    public class ModelComparisonEntry
    {
        public string ModelName { get; set; } = string.Empty;
        public decimal RevenueMAPE { get; set; }
        public decimal RevenueAccuracy { get; set; }
        public bool IsSelected { get; set; }
    }
}
