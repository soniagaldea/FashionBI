using System.ComponentModel.DataAnnotations;

namespace FashionStoreAPI.Models
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
    }
}