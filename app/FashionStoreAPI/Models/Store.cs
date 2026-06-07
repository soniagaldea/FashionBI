using System.ComponentModel.DataAnnotations;

namespace FashionStoreAPI.Models
{
    public class Store
    {
        [Key]
        public int StoreId { get; set; }

        [Required]
        [StringLength(100)]
        public string StoreName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(100)]
        public string? Country { get; set; }

        [StringLength(50)]
        public string? StoreType { get; set; }

        [StringLength(100)]
        public string? Region { get; set; }

        public ICollection<Product> Products { get; set; } = new List<Product>();
        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}