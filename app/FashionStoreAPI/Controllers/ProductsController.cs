using FashionStoreAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionStoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly StoreDbContext _context;

        public ProductsController(StoreDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {
            var products = await _context.Products
                .Include(p => p.Store)
                .Include(p => p.Inventory)
                .Select(p => new
                {
                    p.ProductId,
                    p.StoreId,
                    StoreName = p.Store!.StoreName,
                    p.ProductCode,
                    p.ProductName,
                    p.Category,
                    p.Color,
                    p.Season,
                    p.UnitPrice,
                    p.Brand,
                    p.Gender,
                    p.Material,
                    p.BaseCost,
                    p.IsSeasonal,
                    p.LaunchDate,

                    CurrentStock = p.Inventory!.CurrentStock
                })
                .ToListAsync();

            return Ok(products);
        }

        [HttpGet("store/{storeId}")]
        public async Task<IActionResult> GetProductsByStore(int storeId)
        {
            var products = await _context.Products
                .Include(p => p.Store)
                .Include(p => p.Inventory)
                .Where(p => p.StoreId == storeId)
                .Select(p => new
                {
                    p.ProductId,
                    p.StoreId,
                    StoreName = p.Store!.StoreName,
                    p.ProductCode,
                    p.ProductName,
                    p.Category,
                    p.Color,
                    p.Season,
                    p.UnitPrice,
                    p.Brand,
                    p.Gender,
                    p.Material,
                    p.BaseCost,
                    p.IsSeasonal,
                    p.LaunchDate,

                    CurrentStock = p.Inventory != null ? p.Inventory.CurrentStock : 0
                })
                .ToListAsync();

            return Ok(products);
        }
    }
}