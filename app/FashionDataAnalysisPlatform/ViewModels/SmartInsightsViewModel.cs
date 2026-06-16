namespace FashionDataAnalysisPlatform.ViewModels
{
    public class SmartInsightsViewModel
    {
        public DateTime LastRefreshedAt { get; set; } = DateTime.Now;
        public bool     HasSalesData    { get; set; }
        public bool     HasForecastData { get; set; }

        // ── Section 1: Business Health Score ────────────────────────────
        public int    HealthScore       { get; set; }
        public string HealthStatus      { get; set; } = string.Empty;
        public string HealthColorClass  { get; set; } = string.Empty;
        public string HealthExplanation { get; set; } = string.Empty;

        public int RevenueScore       { get; set; }
        public int ProfitabilityScore { get; set; }
        public int InventoryScore     { get; set; }
        public int ForecastScore      { get; set; }

        public string RevenueScoreLabel       { get; set; } = string.Empty;
        public string ProfitabilityScoreLabel { get; set; } = string.Empty;
        public string InventoryScoreLabel     { get; set; } = string.Empty;
        public string ForecastScoreLabel      { get; set; } = string.Empty;

        public string RevenueScoreDetail       { get; set; } = string.Empty;
        public string ProfitabilityScoreDetail { get; set; } = string.Empty;
        public string InventoryScoreDetail     { get; set; } = string.Empty;
        public string ForecastScoreDetail      { get; set; } = string.Empty;

        // ── Section 2: Strategic Action Board ───────────────────────────
        public List<StrategicAction> UrgentActions      { get; set; } = new();
        public List<StrategicAction> OpportunityActions { get; set; } = new();
        public List<StrategicAction> OptimizeActions    { get; set; } = new();
        public List<StrategicAction> MonitorActions     { get; set; } = new();

        public int TotalActionCount =>
            UrgentActions.Count + OpportunityActions.Count +
            OptimizeActions.Count + MonitorActions.Count;

        // ── Financial impact aggregates (from Action Board) ──────────────
        public decimal TotalRevenueAtRisk     { get; set; }
        public decimal TotalRevenueUpside     { get; set; }
        public decimal TotalOverstockRecovery { get; set; }

        // ── Section 3: KPI Exception Detection ──────────────────────────
        public List<KpiException> Exceptions { get; set; } = new();

        // ── Section 4: ABC/XYZ Portfolio Matrix ──────────────────────────
        public Dictionary<string, AbcXyzCell> Matrix          { get; set; } = new();
        public int     TotalProductCount     { get; set; }
        public decimal TotalPortfolioRevenue { get; set; }
    }

    public class StrategicAction
    {
        public string  ActionCategory     { get; set; } = string.Empty;
        public string  Icon               { get; set; } = string.Empty;
        public string  Title              { get; set; } = string.Empty;
        public string  Explanation        { get; set; } = string.Empty;
        public string  SuggestedAction    { get; set; } = string.Empty;
        public decimal EstimatedImpact    { get; set; }
        public string  ImpactLabel        { get; set; } = string.Empty;
        public string? Store              { get; set; }
        public string? ProductCategory    { get; set; }
        public string  DataBadge          { get; set; } = string.Empty;
    }

    public class KpiException
    {
        public string  MetricName      { get; set; } = string.Empty;
        public string  Dimension       { get; set; } = string.Empty;
        public string  DimensionType   { get; set; } = string.Empty;
        public decimal ExpectedValue   { get; set; }
        public decimal ActualValue     { get; set; }
        public decimal DeviationPct    { get; set; }
        public double  DeviationSigma  { get; set; }
        public string  Severity        { get; set; } = string.Empty;
        public string  SeverityColor   { get; set; } = string.Empty;
        public string  Icon            { get; set; } = string.Empty;
        public string  Explanation     { get; set; } = string.Empty;
        public string  Unit            { get; set; } = string.Empty;
    }

    public class AbcXyzCell
    {
        public string  AbcClass       { get; set; } = string.Empty;
        public string  XyzClass       { get; set; } = string.Empty;
        public int     ProductCount   { get; set; }
        public decimal Revenue        { get; set; }
        public decimal RevenuePercent { get; set; }
        public string  StrategicLabel { get; set; } = string.Empty;
        public string  Recommendation { get; set; } = string.Empty;
        public string  CellBg         { get; set; } = string.Empty;
        public string  CellText       { get; set; } = string.Empty;
        public List<AbcXyzProductEntry> Products { get; set; } = new();
    }

    public class AbcXyzProductEntry
    {
        public int     ProductId    { get; set; }
        public string  ProductName  { get; set; } = string.Empty;
        public string  ProductCode  { get; set; } = string.Empty;
        public string  Category     { get; set; } = string.Empty;
        public decimal Revenue      { get; set; }
        public double  Cv           { get; set; }
        public int     CurrentStock { get; set; }
        public string  AbcClass     { get; set; } = string.Empty;
        public string  XyzClass     { get; set; } = string.Empty;
    }
}
