namespace FashionStoreAPI.Dtos
{
    public class CreateOrderDto
    {
        public int StoreId { get; set; }
        public List<CreateOrderItemDto> Items { get; set; } = new();
        public int? CustomerId { get; set; }
        public string? SalesChannel { get; set; }
        public DateTime? OrderDate { get; set; }
    }

    public class CreateOrderItemDto
    {
        public string ProductCode { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public decimal? DiscountPercent { get; set; }
    }
}