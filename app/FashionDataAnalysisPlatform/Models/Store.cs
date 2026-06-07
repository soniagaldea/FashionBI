using System.ComponentModel.DataAnnotations;

namespace FashionDataAnalysisPlatform.Models
{
    public class Store
    {
        [Key]
        public int StoreId { get; set; }

        public int StoreConnectionId { get; set; }

        public int ExternalStoreId { get; set; }

        [Required]
        [StringLength(150)]
        public string StoreName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(100)]
        public string? Country { get; set; }

        [StringLength(50)]
        public string? StoreType { get; set; }

        [StringLength(100)]
        public string? Region { get; set; }

        public StoreConnection? StoreConnection { get; set; }
    }
}