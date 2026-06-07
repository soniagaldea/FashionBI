using System.ComponentModel.DataAnnotations;

namespace FashionDataAnalysisPlatform.Models
{
    public class ProductImportModel
    {
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        public string Category { get; set; }
        public string Color { get; set; }
        public string Season { get; set; }
        public decimal UnitPrice { get; set; }
    }
}