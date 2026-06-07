using CsvHelper;
using CsvHelper.Configuration;
using FashionDataAnalysisPlatform.Data;
using FashionDataAnalysisPlatform.Models;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace FashionDataAnalysisPlatform.Controllers
{
    public class ImportController : Controller
    {
        private readonly AppDbContext _context;

        public ImportController(AppDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ImportProducts(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                ViewBag.Message = "No file selected";
                return View("Index");
            }

            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));

            var records = csv.GetRecords<ProductImportModel>().ToList();

            foreach (var r in records)
            {
                var product = new Product
                {
                    ProductCode = r.ProductCode,
                    ProductName = r.ProductName,
                    Category = r.Category,
                    Color = r.Color,
                    Season = r.Season,
                    UnitPrice = r.UnitPrice
                };

                _context.Products.Add(product);
            }

            await _context.SaveChangesAsync();

            ViewBag.Message = "Import successful!";
            return View("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportInventory(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                ViewBag.Message = "No file selected for inventory import.";
                return View("Index");
            }

            int importedCount = 0;
            int skippedCount = 0;

            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            });

            var records = csv.GetRecords<InventoryImportModel>().ToList();

            foreach (var r in records)
            {
                if (string.IsNullOrWhiteSpace(r.ProductCode))
                {
                    skippedCount++;
                    continue;
                }

                var product = _context.Products.FirstOrDefault(p => p.ProductCode == r.ProductCode);

                if (product == null)
                {
                    skippedCount++;
                    continue;
                }

                var existingInventory = _context.Inventories.FirstOrDefault(i => i.ProductId == product.ProductId);

                if (existingInventory != null)
                {
                    existingInventory.CurrentStock = r.CurrentStock;
                    existingInventory.LastUpdated = DateTime.Now;
                }
                else
                {
                    var inventory = new Inventory
                    {
                        ProductId = product.ProductId,
                        CurrentStock = r.CurrentStock,
                        LastUpdated = DateTime.Now
                    };

                    _context.Inventories.Add(inventory);
                }

                importedCount++;
            }

            await _context.SaveChangesAsync();

            ViewBag.Message = $"Inventory import completed. Imported/Updated: {importedCount}, Skipped: {skippedCount}";
            return View("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportSales(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                ViewBag.Message = "No file selected for sales import.";
                return View("Index");
            }

            int importedCount = 0;
            int skippedCount = 0;

            using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            });

            var records = csv.GetRecords<SaleImportModel>().ToList();

            foreach (var r in records)
            {
                if (string.IsNullOrWhiteSpace(r.ProductCode) || r.Quantity <= 0)
                {
                    skippedCount++;
                    continue;
                }

                var product = _context.Products.FirstOrDefault(p => p.ProductCode == r.ProductCode);

                if (product == null)
                {
                    skippedCount++;
                    continue;
                }

                var sale = new Sale
                {
                    ProductId = product.ProductId,
                    SaleDate = r.SaleDate,
                    Quantity = r.Quantity,
                    UnitPrice = product.UnitPrice,
                    Revenue = product.UnitPrice * r.Quantity
                };

                _context.Sales.Add(sale);
                importedCount++;
            }

            await _context.SaveChangesAsync();

            ViewBag.Message = $"Sales import completed. Imported: {importedCount}, Skipped: {skippedCount}";
            return View("Index");
        }
    }
}