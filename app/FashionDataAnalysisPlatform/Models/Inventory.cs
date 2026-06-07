using System.ComponentModel.DataAnnotations;

namespace FashionDataAnalysisPlatform.Models
{
    public class Inventory
    {
        [Key]
        public int InventoryId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [Required]
        public int CurrentStock { get; set; }

        public DateTime LastUpdated { get; set; } = DateTime.Now;

        public int MinimumStockThreshold { get; set; }

        public DateTime? LastRestockDate { get; set; }

        public Product? Product { get; set; }

        public int? StoreId { get; set; }

        public int? StoreConnectionId { get; set; }

        public int? ExternalInventoryId { get; set; }

        public Store? Store { get; set; }

        public StoreConnection? StoreConnection { get; set; }
    }
}