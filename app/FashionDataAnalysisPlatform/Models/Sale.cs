using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FashionDataAnalysisPlatform.Models
{
    public class Sale
    {
        [Key]
        public int SaleId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [Required]
        public DateTime SaleDate { get; set; }

        [Required]
        public int Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Revenue { get; set; }

        [StringLength(50)]
        public string? Size { get; set; }

        [StringLength(50)]
        public string? Color { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? DiscountPercent { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? TotalCost { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Profit { get; set; }

        public int? CustomerId { get; set; }

        [StringLength(50)]
        public string? SalesChannel { get; set; }

        public Product? Product { get; set; }

        public int? StoreConnectionId { get; set; }

        public int? ExternalOrderId { get; set; }

        public string? ExternalProductCode { get; set; }

        public StoreConnection? StoreConnection { get; set; }

        public int? StoreId { get; set; }

        public int? ExternalOrderItemId { get; set; }

        public Store? Store { get; set; }
    }
}