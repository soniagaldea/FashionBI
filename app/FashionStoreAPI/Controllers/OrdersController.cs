using FashionStoreAPI.Data;
using FashionStoreAPI.Dtos;
using FashionStoreAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionStoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly StoreDbContext _context;

        public OrdersController(StoreDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders(DateTime? since, int page = 1, int pageSize = 500)
        {
            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 1000)
            {
                pageSize = 500;
            }

            var query = _context.Orders
                .AsNoTracking()
                .Where(o => !since.HasValue || o.OrderDate > since.Value)
                .OrderBy(o => o.OrderDate)
                .ThenBy(o => o.OrderId);

            var orderIds = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(o => o.OrderId)
                .ToListAsync();

            var orders = await _context.Orders
                .AsNoTracking()
                .Where(o => orderIds.Contains(o.OrderId))
                .OrderBy(o => o.OrderDate)
                .Select(o => new
                {
                    o.OrderId,
                    StoreName = o.Store!.StoreName,
                    o.OrderDate,
                    o.TotalAmount,
                    o.CustomerId,
                    o.SalesChannel,
                    Items = o.OrderItems.Select(oi => new
                    {
                        oi.OrderItemId,
                        ProductCode = oi.Product!.ProductCode,
                        ProductName = oi.Product.ProductName,
                        oi.Quantity,
                        oi.UnitPrice,
                        oi.LineTotal,
                        oi.Size,
                        oi.Color,
                        oi.DiscountPercent,
                        oi.TotalCost,
                        oi.Profit
                    }).ToList()
                })
                .ToListAsync();

            return Ok(orders);
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder(CreateOrderDto dto)
        {
            if (dto.Items == null || dto.Items.Count == 0)
            {
                return BadRequest("Order must contain at least one item.");
            }

            var storeExists = await _context.Stores.AnyAsync(s => s.StoreId == dto.StoreId);

            if (!storeExists)
            {
                return BadRequest("Invalid store.");
            }

            var order = new Order
            {
                StoreId = dto.StoreId,
                OrderDate = dto.OrderDate ?? DateTime.Now,
                TotalAmount = 0,
                CustomerId = dto.CustomerId,
                SalesChannel = dto.SalesChannel ?? "Online"
            };

            foreach (var item in dto.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ProductCode) || item.Quantity <= 0)
                {
                    return BadRequest("Invalid order item.");
                }

                var product = await _context.Products
                    .Include(p => p.Inventory)
                    .FirstOrDefaultAsync(p =>
                        p.StoreId == dto.StoreId &&
                        p.ProductCode == item.ProductCode);

                if (product == null)
                {
                    return BadRequest($"Product not found: {item.ProductCode}");
                }

                if (product.Inventory == null || product.Inventory.CurrentStock < item.Quantity)
                {
                    return BadRequest($"Insufficient stock for product: {item.ProductCode}");
                }

                var discountPercent = item.DiscountPercent ?? 0;
                var discountedUnitPrice = Math.Round(product.UnitPrice * (1 - discountPercent / 100), 2);
                var lineTotal = Math.Round(discountedUnitPrice * item.Quantity, 2);
                var totalCost = Math.Round(product.BaseCost * item.Quantity, 2);
                var profit = Math.Round(lineTotal - totalCost, 2);
                var orderItem = new OrderItem
                {
                    ProductId = product.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = discountedUnitPrice,
                    LineTotal = lineTotal,
                    Size = item.Size,
                    Color = item.Color,
                    DiscountPercent = discountPercent,
                    TotalCost = totalCost,
                    Profit = profit
                };

                product.Inventory.CurrentStock -= item.Quantity;
                product.Inventory.LastUpdated = order.OrderDate;

                if (product.Inventory.CurrentStock <= product.Inventory.MinimumStockThreshold)
                {
                    int reorderQuantity = Math.Max(product.Inventory.MinimumStockThreshold * 3, 100);

                    product.Inventory.CurrentStock += reorderQuantity;
                    product.Inventory.LastRestockDate = order.OrderDate;
                    product.Inventory.LastUpdated = order.OrderDate;
                }

                order.TotalAmount += orderItem.LineTotal;
                order.OrderItems.Add(orderItem);
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Order created successfully",
                orderId = order.OrderId,
                storeId = order.StoreId,
                orderDate = order.OrderDate,
                totalAmount = order.TotalAmount,
                itemsCount = order.OrderItems.Count
            });
        }
    }
}