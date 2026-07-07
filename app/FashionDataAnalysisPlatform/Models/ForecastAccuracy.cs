using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionDataAnalysisPlatform.Models
{
    // Stores evaluation metrics for each forecasting model and target.
    [Table("ForecastAccuracies")]
    public class ForecastAccuracy
    {
        [Key]
        public int ForecastAccuracyId { get; set; }

        [StringLength(50)]
        public string? ModelName { get; set; }

        [Required]
        [StringLength(20)]
        public string Target { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,4)")]
        public decimal MAE { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal RMSE { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal MAPE { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal AccuracyPercent { get; set; }

        public DateTime GeneratedAt { get; set; }
    }
}
