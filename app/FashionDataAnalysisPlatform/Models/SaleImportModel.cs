namespace FashionDataAnalysisPlatform.Models
{
    public class SaleImportModel
    {
        public string ProductCode { get; set; } = string.Empty;
        public DateTime SaleDate { get; set; }
        public int Quantity { get; set; }
    }
}