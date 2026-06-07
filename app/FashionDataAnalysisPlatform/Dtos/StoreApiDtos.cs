namespace FashionDataAnalysisPlatform.Dtos
{
    public class StoreApiDto
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? StoreType { get; set; }
        public string? Region { get; set; }
    }

    public class ProductApiDto
    {
        public int ProductId { get; set; }
        public int StoreId { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Color { get; set; }
        public string? Season { get; set; }
        public decimal UnitPrice { get; set; }
        public int CurrentStock { get; set; }
        public string? Brand { get; set; }
        public string? Gender { get; set; }
        public string? Material { get; set; }
        public decimal BaseCost { get; set; }
        public bool IsSeasonal { get; set; }
        public DateTime? LaunchDate { get; set; }
    }

    public class InventoryApiDto
    {
        public int InventoryId { get; set; }
        public int ProductId { get; set; }
        public int StoreId { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int CurrentStock { get; set; }
        public DateTime LastUpdated { get; set; }
        public int MinimumStockThreshold { get; set; }
        public DateTime? LastRestockDate { get; set; }
    }
}