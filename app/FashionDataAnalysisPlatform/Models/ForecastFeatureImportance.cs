using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionDataAnalysisPlatform.Models
{
    [Table("ForecastFeatureImportances")]
    public class ForecastFeatureImportance
    {
        [Key]
        public int ForecastFeatureImportanceId { get; set; }

        [Required]
        [StringLength(100)]
        public string FeatureName { get; set; } = string.Empty;

        [Column(TypeName = "decimal(10,6)")]
        public decimal Importance { get; set; }

        [Required]
        [StringLength(20)]
        public string Target { get; set; } = string.Empty;

        public DateTime GeneratedAt { get; set; }
    }
}
