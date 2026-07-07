namespace FashionDataAnalysisPlatform.ViewModels
{
    public class SustainabilityViewModel
    {
        public DateTime LastRefreshedAt  { get; set; } = DateTime.Now;
        public bool     HasData          { get; set; }
        public string   CollectionSummary { get; set; } = string.Empty;

        // Top KPI Row
        public decimal AvgSellThroughRate       { get; set; }   // %
        public decimal AtRiskInventoryValue     { get; set; }   // €  seasonal, low-STR
        public int     DeadStockCount           { get; set; }   // SKUs with zero velocity ≥ 45 days
        public decimal MarkdownDependencyRate   { get; set; }   // % units sold at ≥20% discount
        public decimal SeasonalOverstockValue   { get; set; }   // € remaining in expired seasons

        // Section 2: Season Timeline
        public List<SeasonSummary> Seasons { get; set; } = new();

        // Section 3: Waste Risk Matrix (bubble data)
        // Each entry = one Category×Season combination
        public List<WasteRiskPoint> WasteRiskPoints { get; set; } = new();

        // Section 4: Sell-Through by Category
        public List<CategorySellThrough> CategorySellThroughs { get; set; } = new();

        // Section 5: Markdown Dependency by Category
        public List<CategoryMarkdown> CategoryMarkdowns { get; set; } = new();

        // Section 6: At-Risk SKU Table
        public List<SkuRiskEntry> AtRiskSkus { get; set; } = new();

        // Section 7: Recommendations
        public List<SustainabilityRecommendation> ActNowRecs     { get; set; } = new();
        public List<SustainabilityRecommendation> MonitorRecs    { get; set; } = new();
        public List<SustainabilityRecommendation> PlanBetterRecs { get; set; } = new();

        public int TotalRecommendationCount =>
            ActNowRecs.Count + MonitorRecs.Count + PlanBetterRecs.Count;
    }

    // Season summary (timeline section)
    public class SeasonSummary
    {
        public string  SeasonName        { get; set; } = string.Empty;
        public bool    IsCore            { get; set; }
        public DateTime? SeasonEnd       { get; set; }   // null for Core
        public int     DaysToEnd         { get; set; }   // negative = expired
        public decimal SellThroughRate   { get; set; }   // %
        public int     SoldUnits         { get; set; }
        public int     RemainingUnits    { get; set; }
        public decimal AtRiskValue       { get; set; }   // € remaining × BaseCost
        public string  Status            { get; set; } = string.Empty;  // Healthy / Monitor / Critical / Closed
        public string  StatusColor       { get; set; } = string.Empty;  // emerald / amber / red / slate
    }

    // Waste risk matrix point
    public class WasteRiskPoint
    {
        public string  Label             { get; set; } = string.Empty;  // "Category — Season"
        public string  Category          { get; set; } = string.Empty;
        public string  SeasonName        { get; set; } = string.Empty;
        public int     DaysToEnd         { get; set; }   // negative = expired; 9999 = Core
        public decimal SellThroughRate   { get; set; }   // Y-axis (0–100)
        public decimal AtRiskValue       { get; set; }   // bubble size
        public int     WasteRiskScore    { get; set; }   // 0–100; drives colour
        public string  RiskColor         { get; set; } = string.Empty;  // green / amber / red
    }

    // Category sell-through
    public class CategorySellThrough
    {
        public string  Category        { get; set; } = string.Empty;
        public int     SoldUnits       { get; set; }
        public int     RemainingUnits  { get; set; }
        public decimal SellThroughRate { get; set; }   // %
        public string  StatusColor     { get; set; } = string.Empty;
    }

    // Markdown dependency by category
    public class CategoryMarkdown
    {
        public string  Category             { get; set; } = string.Empty;
        public decimal MarkdownDependency   { get; set; }  // % units at ≥20% disc
        public decimal AvgDiscountPercent   { get; set; }  // avg across all units
        public int     TotalUnitsSold       { get; set; }
        public int     MarkdownUnitsSold    { get; set; }
        public string  Interpretation       { get; set; } = string.Empty;
        public string  StatusColor          { get; set; } = string.Empty;
    }

    // At-risk SKU row
    public class SkuRiskEntry
    {
        public string  ProductName         { get; set; } = string.Empty;
        public string  StoreName           { get; set; } = string.Empty;
        public string  Category            { get; set; } = string.Empty;
        public string  SeasonName          { get; set; } = string.Empty;
        public decimal SellThroughRate     { get; set; }
        public int     CurrentStock        { get; set; }
        public decimal AtRiskValue         { get; set; }  // CurrentStock × BaseCost
        public int     DaysToSeasonEnd     { get; set; }  // negative = expired; 9999 = Core
        public decimal MarkdownDependency  { get; set; }  // % of this product's sales at ≥20%
        public int     WasteRiskScore      { get; set; }  // 0–100
        public string  RecommendedAction   { get; set; } = string.Empty;
        public string  RiskColor           { get; set; } = string.Empty;
        public bool    IsDeadStock         { get; set; }
    }

    // Sustainability recommendation
    public class SustainabilityRecommendation
    {
        public string  Group            { get; set; } = string.Empty;  // ActNow / Monitor / PlanBetter
        public string  Icon             { get; set; } = string.Empty;
        public string  Title            { get; set; } = string.Empty;
        public string  Explanation      { get; set; } = string.Empty;
        public string  SuggestedAction  { get; set; } = string.Empty;
        public decimal FinancialImpact  { get; set; }  // € at risk or recoverable
        public string  ImpactLabel      { get; set; } = string.Empty;
        public string  DataBadge        { get; set; } = string.Empty;
    }
}
