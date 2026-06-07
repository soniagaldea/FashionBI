using FashionStoreAPI.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<StoreDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<StoreDbContext>();

    if (!context.Stores.Any())
    {
        var scufferDistrict = new FashionStoreAPI.Models.Store
        {
            StoreName = "Scuffer District",
            City = "Bucharest",
            Country = "Romania",
            StoreType = "Flagship",
            Region = "South Eastern Europe"
        };

        var scufferDowntown = new FashionStoreAPI.Models.Store
        {
            StoreName = "Scuffer Downtown",
            City = "Cluj-Napoca",
            Country = "Romania",
            StoreType = "Urban Store",
            Region = "Central Europe"
        };

        var maisonToulouse = new FashionStoreAPI.Models.Store
        {
            StoreName = "Maison Toulouse",
            City = "Paris",
            Country = "France",
            StoreType = "Premium Boutique",
            Region = "Western Europe"
        };

        context.Stores.AddRange(scufferDistrict, scufferDowntown, maisonToulouse);
        context.SaveChanges();

        var products = new List<FashionStoreAPI.Models.Product>();

        string[] scufferCategories = { "Tops", "Trousers", "Outerwear", "Accessories" };
        string[] maisonCategories = { "Dresses", "Outerwear", "Blazers", "Accessories" };

        for (int i = 1; i <= 40; i++)
        {
            var category = scufferCategories[(i - 1) % scufferCategories.Length];
            var price = category switch
            {
                "Tops" => 39.99m + i,
                "Trousers" => 59.99m + i,
                "Outerwear" => 89.99m + i,
                "Accessories" => 19.99m + i,
                _ => 49.99m
            };

            var baseCost = Math.Round(price * 0.45m, 2);

            products.Add(new FashionStoreAPI.Models.Product
            {
                StoreId = scufferDistrict.StoreId,
                ProductCode = $"P{i:000}",
                ProductName = $"Scuffer {category} Style {i}",
                Category = category,
                Color = i % 2 == 0 ? "Black" : "Cream",
                Season = category == "Outerwear" ? "Autumn/Winter" : "All Season",
                UnitPrice = price,
                Brand = "Scuffer",
                Gender = "Unisex",
                Material = category == "Trousers" ? "Cotton Denim" : "Cotton Blend",
                BaseCost = baseCost,
                IsSeasonal = category == "Outerwear",
                LaunchDate = new DateTime(2025, ((i - 1) % 12) + 1, 1)
            });

            products.Add(new FashionStoreAPI.Models.Product
            {
                StoreId = scufferDowntown.StoreId,
                ProductCode = $"P{i:000}",
                ProductName = $"Scuffer {category} Style {i}",
                Category = category,
                Color = i % 2 == 0 ? "Black" : "Cream",
                Season = category == "Outerwear" ? "Autumn/Winter" : "All Season",
                UnitPrice = price,
                Brand = "Scuffer",
                Gender = "Unisex",
                Material = category == "Trousers" ? "Cotton Denim" : "Cotton Blend",
                BaseCost = baseCost,
                IsSeasonal = category == "Outerwear",
                LaunchDate = new DateTime(2025, ((i - 1) % 12) + 1, 1)
            });
        }

        for (int i = 41; i <= 60; i++)
        {
            var category = maisonCategories[(i - 41) % maisonCategories.Length];
            var price = category switch
            {
                "Dresses" => 260m + i,
                "Outerwear" => 520m + i,
                "Blazers" => 390m + i,
                "Accessories" => 180m + i,
                _ => 250m
            };

            var baseCost = Math.Round(price * 0.32m, 2);

            products.Add(new FashionStoreAPI.Models.Product
            {
                StoreId = maisonToulouse.StoreId,
                ProductCode = $"P{i:000}",
                ProductName = $"Maison Toulouse {category} Piece {i}",
                Category = category,
                Color = i % 2 == 0 ? "Navy" : "Camel",
                Season = category == "Outerwear" ? "Winter" : "All Season",
                UnitPrice = price,
                Brand = "Maison Toulouse",
                Gender = "Women",
                Material = category == "Outerwear" ? "Wool Cashmere Blend" : "Silk Wool Blend",
                BaseCost = baseCost,
                IsSeasonal = category == "Outerwear" || category == "Dresses",
                LaunchDate = new DateTime(2025, ((i - 41) % 12) + 1, 1)
            });
        }

        context.Products.AddRange(products);
        context.SaveChanges();

        var inventories = products.Select(p => new FashionStoreAPI.Models.Inventory
        {
            ProductId = p.ProductId,
            CurrentStock = p.Brand == "Maison Toulouse" ? 120 : 350,
            MinimumStockThreshold = p.Brand == "Maison Toulouse" ? 15 : 40,
            LastRestockDate = new DateTime(2025, 1, 1),
            LastUpdated = new DateTime(2025, 1, 1)
        }).ToList();

        context.Inventories.AddRange(inventories);
        context.SaveChanges();
    }
}

app.Run();
