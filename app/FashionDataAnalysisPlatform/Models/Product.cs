using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionDataAnalysisPlatform.Models
{
    public class Product
    {
        [Key]
        public int ProductId { get; set; }

        [Required]
        [StringLength(50)]
        public string ProductCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string ProductName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Category { get; set; } = string.Empty;

        [StringLength(50)]
        public string? Color { get; set; }

        [StringLength(50)]
        public string? Season { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }
        [StringLength(100)]
        public string? Brand { get; set; }

        [StringLength(50)]
        public string? Gender { get; set; }

        [StringLength(100)]
        public string? Material { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BaseCost { get; set; }

        public bool IsSeasonal { get; set; }

        public DateTime? LaunchDate { get; set; }

        public int? StoreId { get; set; }

        public int? StoreConnectionId { get; set; }

        public int? ExternalProductId { get; set; }

        public Store? Store { get; set; }

        public StoreConnection? StoreConnection { get; set; }

        public ICollection<Sale> Sales { get; set; } = new List<Sale>();
        public ICollection<Inventory> Inventories { get; set; } = new List<Inventory>();
    }
}