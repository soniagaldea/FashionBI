using FashionStoreAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionStoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InventoryController : ControllerBase
    {
        private readonly StoreDbContext _context;

        public InventoryController(StoreDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetInventory()
        {
            var inventory = await _context.Inventories
                .Include(i => i.Product)
                    .ThenInclude(p => p!.Store)
                .Select(i => new
                {
                    i.InventoryId,
                    i.ProductId,
                    i.Product!.StoreId,
                    StoreName = i.Product.Store!.StoreName,
                    i.Product.ProductCode,
                    i.Product.ProductName,
                    i.Product.Category,
                    i.CurrentStock,
                    i.MinimumStockThreshold,
                    i.LastRestockDate,
                    i.LastUpdated
                })
                .ToListAsync();

            return Ok(inventory);
        }

        [HttpGet("store/{storeId}")]
        public async Task<IActionResult> GetInventoryByStore(int storeId)
        {
            var inventory = await _context.Inventories
                .Include(i => i.Product)
                    .ThenInclude(p => p!.Store)
                .Where(i => i.Product!.StoreId == storeId)
                .Select(i => new
                {
                    i.InventoryId,
                    i.ProductId,
                    i.Product!.StoreId,
                    StoreName = i.Product.Store!.StoreName,
                    i.Product.ProductCode,
                    i.Product.ProductName,
                    i.Product.Category,
                    i.CurrentStock,
                    i.MinimumStockThreshold,
                    i.LastRestockDate,
                    i.LastUpdated
                })
                .ToListAsync();

            return Ok(inventory);
        }
    }
}