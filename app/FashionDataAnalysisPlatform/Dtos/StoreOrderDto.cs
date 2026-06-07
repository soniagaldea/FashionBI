namespace FashionDataAnalysisPlatform.Dtos
{
    public class StoreOrderDto
    {
        public int OrderId { get; set; }
        public string StoreName { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public int? CustomerId { get; set; }
        public string? SalesChannel { get; set; }
        public List<StoreOrderItemDto> Items { get; set; } = new();
    }

    public class StoreOrderItemDto
    {
        public int OrderItemId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public decimal? DiscountPercent { get; set; }
        public decimal? TotalCost { get; set; }
        public decimal? Profit { get; set; }
    }
}