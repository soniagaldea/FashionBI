using FashionStoreAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionStoreAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StoresController : ControllerBase
    {
        private readonly StoreDbContext _context;

        public StoresController(StoreDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetStores()
        {
            var stores = await _context.Stores
                .Select(s => new
                {
                    s.StoreId,
                    s.StoreName,
                    s.City,
                    s.Country,
                    s.StoreType,
                    s.Region
                })
                .ToListAsync();
            return Ok(stores);
        }
    }
}