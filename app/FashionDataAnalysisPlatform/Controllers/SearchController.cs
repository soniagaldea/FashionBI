using FashionDataAnalysisPlatform.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FashionDataAnalysisPlatform.Controllers
{
    [Authorize]
    public class SearchController : Controller
    {
        private readonly AppDbContext _context;

        public SearchController(AppDbContext context) => _context = context;

        private static readonly (string Name, string Secondary, string Url)[] Pages =
        [
            ("Executive Dashboard",    "KPI overview & top-line metrics",             "/Dashboard"),
            ("Sales Analytics",        "Revenue, trends & category performance",       "/Sales"),
            ("Inventory Intelligence", "Stock health, reorder alerts & aging",         "/Inventories"),
            ("Store Comparison",       "Side-by-side multi-store benchmarking",        "/StoreComparison"),
            ("Forecasting",            "ML demand forecasting & accuracy metrics",     "/Forecasting"),
            ("Smart Insights",         "Health score, ABC/XYZ matrix & KPI alerts",   "/SmartInsights"),
            ("Sustainability",         "Sell-through rate, waste risk & dead stock",   "/Sustainability"),
            ("Store Connections",      "Manage connected retail store data sources",   "/StoreConnection"),
        ];

        [HttpGet]
        public async Task<IActionResult> Autocomplete(string? q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Json(Array.Empty<object>());

            var term = q.Trim();
            var results = new List<object>();

            // ── Pages (hardcoded, always first) ──────────────────────────────
            foreach (var (name, secondary, url) in Pages)
            {
                if (name.Contains(term, StringComparison.OrdinalIgnoreCase))
                    results.Add(new { name, type = "Page", secondary, url });
            }

            // ── Stores ───────────────────────────────────────────────────────
            var stores = await _context.Stores
                .AsNoTracking()
                .Where(s => s.StoreName.Contains(term))
                .Take(3)
                .Select(s => new { s.StoreName, s.City, s.StoreType, s.StoreId })
                .ToListAsync();

            foreach (var s in stores)
            {
                var secondary = string.Join(" · ",
                    new[] { s.City, s.StoreType }.Where(x => !string.IsNullOrEmpty(x)));
                results.Add(new {
                    name      = s.StoreName,
                    type      = "Store",
                    secondary,
                    url       = "/StoreComparison"
                });
            }

            // ── Categories ───────────────────────────────────────────────────
            var categories = await _context.Products
                .AsNoTracking()
                .Where(p => p.Category.Contains(term))
                .Select(p => p.Category)
                .Distinct()
                .Take(3)
                .ToListAsync();

            foreach (var cat in categories)
                results.Add(new { name = cat, type = "Category", secondary = "Browse in Sales Analytics", url = "/Sales" });

            // ── Products (by name) ───────────────────────────────────────────
            var products = await _context.Products
                .AsNoTracking()
                .Where(p => p.ProductName.Contains(term))
                .Take(4)
                .Select(p => new { p.ProductName, p.ProductCode, p.Category })
                .ToListAsync();

            foreach (var p in products)
                results.Add(new {
                    name      = p.ProductName,
                    type      = "Product",
                    secondary = $"{p.ProductCode} · {p.Category}",
                    url       = "/Inventories"
                });

            // ── SKUs (ProductCode match, not already surfaced as a product name match) ──
            var skus = await _context.Products
                .AsNoTracking()
                .Where(p => p.ProductCode.Contains(term) && !p.ProductName.Contains(term))
                .Take(3)
                .Select(p => new { p.ProductCode, p.ProductName })
                .ToListAsync();

            foreach (var s in skus)
                results.Add(new {
                    name      = s.ProductCode,
                    type      = "SKU",
                    secondary = s.ProductName,
                    url       = "/Inventories"
                });

            return Json(results.Take(8));
        }
    }
}
