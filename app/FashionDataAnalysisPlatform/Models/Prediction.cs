using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionDataAnalysisPlatform.Models
{
    public class Prediction
    {
        [Key]
        public int PredictionId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [Required]
        public DateTime PredictionDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PredictedSales { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal RecommendedStock { get; set; }

        [StringLength(100)]
        public string? ModelName { get; set; }

        public Product? Product { get; set; }
    }
}