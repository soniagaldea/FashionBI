using System.ComponentModel.DataAnnotations;

namespace FashionDataAnalysisPlatform.Models
{
    public class StoreConnection
    {
        [Key]
        public int StoreConnectionId { get; set; }

        [Required]
        [StringLength(150)]
        public string StoreName { get; set; } = string.Empty;

        [Required]
        [StringLength(300)]
        public string StoreApiUrl { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime? LastSyncAt { get; set; }
    }
}