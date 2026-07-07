using FashionStoreAPI.Data;
using FashionStoreAPI.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<StoreDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

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
        // Returns the canonical launch date for a Scuffer collection.
        // Core = store opening (simulation start). AW/SS = real drop dates.
        static DateTime ScLaunch(string season) => season switch
        {
            "AW24" => new DateTime(2024, 9,  1),
            "SS25" => new DateTime(2025, 3,  1),
            "AW25" => new DateTime(2025, 9,  1),
            "SS26" => new DateTime(2026, 3,  1),
            _      => new DateTime(2024, 7,  1), // Core
        };

        // Maison Toulouse previews collections 2-4 weeks before Scuffer.
        static DateTime MtLaunch(string season) => season switch
        {
            "AW24" => new DateTime(2024, 8, 15),
            "SS25" => new DateTime(2025, 2,  1),
            "AW25" => new DateTime(2025, 8, 15),
            "SS26" => new DateTime(2026, 2,  1),
            _      => new DateTime(2024, 7,  1), // Core
        };

        // Stores
        var scufferDistrict = new Store
        {
            StoreName = "Scuffer District",
            City      = "Bucharest",
            Country   = "Romania",
            StoreType = "Flagship",
            Region    = "South Eastern Europe"
        };

        var scufferDowntown = new Store
        {
            StoreName = "Scuffer Downtown",
            City      = "Cluj-Napoca",
            Country   = "Romania",
            StoreType = "Urban Store",
            Region    = "Central Europe"
        };

        var maisonToulouse = new Store
        {
            StoreName = "Maison Toulouse",
            City      = "Paris",
            Country   = "France",
            StoreType = "Premium Boutique",
            Region    = "Western Europe"
        };

        context.Stores.AddRange(scufferDistrict, scufferDowntown, maisonToulouse);
        context.SaveChanges();

        const decimal SC_COST         = 0.42m; // Scuffer base cost as % of retail
        const decimal MT_COST         = 0.30m; // Maison Toulouse base cost as % of retail
        const decimal DISTRICT_PREMIUM = 1.05m; // Flagship price premium over Downtown

        var products = new List<Product>();

        // Scuffer Shared Range (31 products × 2 stores = 62 rows)
        // Same ProductCode exists in both District and Downtown.
        // District price = base × 1.05; Downtown price = base.
        var shared = new (string Code, string Name, string Category, string Season,
                          bool IsSeasonal, string Color, string Material,
                          string Gender, decimal BasePrice)[]
        {
            // Tops (8)
            ("SC-T001", "Relaxed Fit Tee",          "Tops",      "Core", false, "White",          "100% Cotton",           "Unisex",  29.99m),
            ("SC-T002", "Oversized Graphic Tee",     "Tops",      "Core", false, "Black",          "100% Cotton",           "Unisex",  34.99m),
            ("SC-T003", "Box-Cut Striped Tee",       "Tops",      "SS25", true,  "Ecru",           "Cotton",                "Unisex",  32.99m),
            ("SC-T004", "Cropped Ribbed Tank",       "Tops",      "SS25", true,  "Sage",           "Cotton Rib",            "Women",   24.99m),
            ("SC-T005", "Long-Sleeve Essential",     "Tops",      "AW25", true,  "Charcoal",       "Cotton",                "Unisex",  39.99m),
            ("SC-T006", "Raglan Baseball Tee",       "Tops",      "AW24", true,  "Black",          "Cotton",                "Unisex",  35.99m),
            ("SC-T007", "Boxy Linen Shirt",          "Tops",      "SS25", true,  "Stone",          "Linen Blend",           "Unisex",  44.99m),
            ("SC-T008", "Washed Pocket Tee",         "Tops",      "SS26", true,  "Vintage White",  "Garment-Washed Cotton", "Unisex",  37.99m),

            // Knitwear (5)
            ("SC-K001", "Chunky Knit Sweater",       "Knitwear",  "AW24", true,  "Cream",          "Wool Blend",            "Unisex",  89.99m),
            ("SC-K002", "Crewneck Sweatshirt",       "Knitwear",  "Core", false, "Black",          "Cotton Fleece",         "Unisex",  69.99m),
            ("SC-K003", "Zip-Up Hoodie",             "Knitwear",  "Core", false, "Grey Marl",      "Cotton Fleece",         "Unisex",  79.99m),
            ("SC-K004", "Cable Knit Cardigan",       "Knitwear",  "AW25", true,  "Oat",            "Wool Blend",            "Women",   95.99m),
            ("SC-K005", "Quarter-Zip Pullover",      "Knitwear",  "AW25", true,  "Navy",           "Cotton Fleece",         "Unisex",  74.99m),

            // Trousers (7) 
            ("SC-P001", "Utility Cargo Pant",        "Trousers",  "Core", false, "Black",          "Cotton Twill",          "Unisex",  79.99m),
            ("SC-P002", "Straight-Leg Denim",        "Trousers",  "Core", false, "Indigo",         "Cotton Denim",          "Unisex",  89.99m),
            ("SC-P003", "Track Pant",                "Trousers",  "Core", false, "Charcoal",       "Jersey",                "Unisex",  69.99m),
            ("SC-P004", "Wide-Leg Linen Trouser",    "Trousers",  "SS25", true,  "Stone",          "Linen Blend",           "Unisex",  74.99m),
            ("SC-P005", "Slim Fit Chino",            "Trousers",  "Core", false, "Khaki",          "Cotton Twill",          "Unisex",  74.99m),
            ("SC-P006", "Barrel-Leg Denim",          "Trousers",  "AW25", true,  "Light Wash",     "Cotton Denim",          "Women",   89.99m),
            ("SC-P007", "Relaxed Pleated Trouser",   "Trousers",  "AW25", true,  "Charcoal",       "Poly Blend",            "Unisex",  79.99m),

            // Outerwear (5) 
            ("SC-O001", "Nylon Coach Jacket",        "Outerwear", "Core", false, "Black",          "Nylon Shell",           "Unisex", 109.99m),
            ("SC-O002", "Padded Puffer",             "Outerwear", "AW24", true,  "Cream",          "Poly Shell",            "Unisex", 139.99m),
            ("SC-O003", "Nylon Windbreaker",         "Outerwear", "SS25", true,  "Sage",           "Nylon",                 "Unisex",  89.99m),
            ("SC-O004", "Fleece Zip-Up Jacket",      "Outerwear", "AW25", true,  "Charcoal",       "Polyester Fleece",      "Unisex", 119.99m),
            ("SC-O005", "Cropped Denim Jacket",      "Outerwear", "Core", false, "Light Wash",     "Cotton Denim",          "Unisex",  99.99m),

            // Dresses (3) 
            ("SC-D001", "Slip Midi Dress",           "Dresses",   "SS25", true,  "Black",          "Satin",                 "Women",   54.99m),
            ("SC-D002", "Oversized Shirt Dress",     "Dresses",   "Core", false, "White",          "Cotton Poplin",         "Women",   59.99m),
            ("SC-D003", "Midi Wrap Dress",           "Dresses",   "SS26", true,  "Floral Print",   "Viscose",               "Women",   49.99m),

            // Accessories (3) 
            ("SC-A001", "Canvas Tote",               "Accessories","Core", false, "Natural",       "Cotton Canvas",         "Unisex",  24.99m),
            ("SC-A002", "Ribbed Beanie",             "Accessories","AW24", true,  "Black",         "Acrylic Knit",          "Unisex",  19.99m),
            ("SC-A003", "Crossbody Mini Bag",        "Accessories","Core", false, "Black",         "Synthetic Leather",     "Unisex",  39.99m),
        };

        foreach (var t in shared)
        {
            var cost   = Math.Round(t.BasePrice * SC_COST, 2);
            var launch = ScLaunch(t.Season);

            products.Add(new Product
            {
                StoreId = scufferDowntown.StoreId,
                ProductCode = t.Code, ProductName = t.Name,
                Category = t.Category, Season = t.Season, IsSeasonal = t.IsSeasonal,
                Color = t.Color, Material = t.Material, Gender = t.Gender,
                UnitPrice = t.BasePrice, BaseCost = cost,
                Brand = "Scuffer", LaunchDate = launch
            });

            products.Add(new Product
            {
                StoreId = scufferDistrict.StoreId,
                ProductCode = t.Code, ProductName = t.Name,
                Category = t.Category, Season = t.Season, IsSeasonal = t.IsSeasonal,
                Color = t.Color, Material = t.Material, Gender = t.Gender,
                UnitPrice = Math.Round(t.BasePrice * DISTRICT_PREMIUM, 2), BaseCost = cost,
                Brand = "Scuffer", LaunchDate = launch
            });
        }

        // Scuffer District Exclusives (6 rows) 
        // Fashion-forward seasonal drops available only at the flagship.
        var districtEx = new (string Code, string Name, string Category, string Season,
                              bool IsSeasonal, string Color, string Material,
                              string Gender, decimal Price)[]
        {
            ("SC-T009", "Archive Print Camp Collar Shirt", "Tops",      "SS25", true, "Ecru",     "Viscose",          "Unisex",  54.99m),
            ("SC-T010", "Linen-Cotton Band Collar Shirt",  "Tops",      "SS26", true, "Stone",    "Linen-Cotton",     "Unisex",  49.99m),
            ("SC-K006", "Wool Blend Turtleneck",           "Knitwear",  "AW25", true, "Cream",    "Wool Blend",       "Unisex",  99.99m),
            ("SC-P008", "Pinstripe Wide-Leg Trouser",      "Trousers",  "AW25", true, "Charcoal", "Wool Blend",       "Unisex", 109.99m),
            ("SC-O006", "Structured Trench Coat",          "Outerwear", "AW25", true, "Camel",    "Cotton Gabardine", "Unisex", 189.99m),
            ("SC-D004", "Cut-Out Bodycon Dress",           "Dresses",   "SS25", true, "Black",    "Viscose",          "Women",   69.99m),
        };

        foreach (var t in districtEx)
        {
            products.Add(new Product
            {
                StoreId = scufferDistrict.StoreId,
                ProductCode = t.Code, ProductName = t.Name,
                Category = t.Category, Season = t.Season, IsSeasonal = t.IsSeasonal,
                Color = t.Color, Material = t.Material, Gender = t.Gender,
                UnitPrice = t.Price, BaseCost = Math.Round(t.Price * SC_COST, 2),
                Brand = "Scuffer", LaunchDate = ScLaunch(t.Season)
            });
        }

        // Scuffer Downtown Exclusives (6 rows) 
        // Practical, value-first basics for the everyday urban customer.
        var downtownEx = new (string Code, string Name, string Category, string Season,
                              bool IsSeasonal, string Color, string Material,
                              string Gender, decimal Price)[]
        {
            ("SC-T011", "Essential Crew Tee",      "Tops",        "Core", false, "White",     "Cotton Jersey", "Unisex", 19.99m),
            ("SC-K007", "Pullover Hoodie",         "Knitwear",    "Core", false, "Black",     "Cotton Fleece", "Unisex", 64.99m),
            ("SC-P009", "Fleece Jogger",           "Trousers",    "Core", false, "Grey Marl", "Cotton Fleece", "Unisex", 59.99m),
            ("SC-O007", "Lightweight Anorak",      "Outerwear",   "SS25", true,  "Navy",      "Nylon",         "Unisex", 74.99m),
            ("SC-A004", "Six-Panel Baseball Cap",  "Accessories", "Core", false, "Black",     "Cotton Twill",  "Unisex", 24.99m),
            ("SC-A005", "Nylon Commuter Backpack", "Accessories", "Core", false, "Black",     "Nylon",         "Unisex", 49.99m),
        };

        foreach (var t in downtownEx)
        {
            products.Add(new Product
            {
                StoreId = scufferDowntown.StoreId,
                ProductCode = t.Code, ProductName = t.Name,
                Category = t.Category, Season = t.Season, IsSeasonal = t.IsSeasonal,
                Color = t.Color, Material = t.Material, Gender = t.Gender,
                UnitPrice = t.Price, BaseCost = Math.Round(t.Price * SC_COST, 2),
                Brand = "Scuffer", LaunchDate = ScLaunch(t.Season)
            });
        }

        // Maison Toulouse (26 rows) 
        // All products: Gender = Women, Brand = "Maison Toulouse", basecost 30%.
        // MT-P (Trousers) category covers all bottoms: trousers, skirts, culottes.
        var mtProducts = new (string Code, string Name, string Category, string Season,
                              bool IsSeasonal, string Color, string Material, decimal Price)[]
        {
            // Dresses (4) 
            ("MT-D001", "Silk Slip Dress",                "Dresses",    "SS25", true,  "Ivory",         "100% Silk",           380m),
            ("MT-D002", "Broderie Anglaise Midi Dress",   "Dresses",    "SS25", true,  "White",         "Cotton Broderie",     420m),
            ("MT-D003", "Velvet Column Dress",            "Dresses",    "AW24", true,  "Midnight Blue", "Silk Velvet",         680m),
            ("MT-D004", "Wool Sheath Dress",              "Dresses",    "AW25", true,  "Camel",         "Wool Crepe",          490m),

            // Tops (4) 
            ("MT-T001", "Silk Blouse",                    "Tops",       "SS25", true,  "Ivory",         "100% Silk",           280m),
            ("MT-T002", "Cashmere Round-Neck Top",        "Tops",       "AW25", true,  "Camel",         "100% Cashmere",       320m),
            ("MT-T003", "Satin Camisole",                 "Tops",       "SS25", true,  "Champagne",     "Silk Satin",          240m),
            ("MT-T004", "Crisp Cotton Shirt",             "Tops",       "Core", false, "White",         "Egyptian Cotton",     260m),

            // Trousers / Bottoms (4) 
            ("MT-P001", "Tailored Wool Trouser",          "Trousers",   "AW24", true,  "Charcoal",      "Wool Crepe",          380m),
            ("MT-P002", "Wide-Leg Silk Pant",             "Trousers",   "SS25", true,  "Champagne",     "Silk Blend",          360m),
            ("MT-P003", "Pencil Skirt",                   "Trousers",   "Core", false, "Black",         "Wool Crepe",          320m),
            ("MT-P004", "Pleated Linen Culottes",         "Trousers",   "SS25", true,  "Ecru",          "100% Linen",          295m),

            // Blazers (4) 
            ("MT-B001", "Single-Breasted Tweed Blazer",  "Blazers",    "AW24", true,  "Houndstooth",   "Tweed",               620m),
            ("MT-B002", "Longline Wool Blazer",           "Blazers",    "AW25", true,  "Camel",         "Wool",                680m),
            ("MT-B003", "Double-Breasted Crepe Blazer",  "Blazers",    "SS25", true,  "Ivory",         "Viscose Crepe",       580m),
            ("MT-B004", "Velvet Tuxedo Blazer",          "Blazers",    "AW24", true,  "Black",         "Silk Velvet",         720m),

            // Knitwear (4) 
            ("MT-K001", "Cashmere Roll-Neck Sweater",    "Knitwear",   "AW24", true,  "Oatmeal",       "100% Cashmere",       580m),
            ("MT-K002", "Merino Wool Cardigan",          "Knitwear",   "AW25", true,  "Camel",         "Merino Wool",         420m),
            ("MT-K003", "Silk-Cashmere Twinset",         "Knitwear",   "AW25", true,  "Ivory",         "Silk-Cashmere",       680m),
            ("MT-K004", "Fine-Knit Merino Turtleneck",  "Knitwear",   "AW25", true,  "Charcoal",      "Merino Wool",         380m),

            // Outerwear (4) 
            ("MT-O001", "Double-Faced Wool Coat",        "Outerwear",  "AW24", true,  "Camel",         "Wool-Cashmere Blend", 980m),
            ("MT-O002", "Belted Trench Coat",            "Outerwear",  "Core", false, "Beige",         "Cotton Gabardine",    860m),
            ("MT-O003", "Longline Cashmere Coat",        "Outerwear",  "AW25", true,  "Ivory",         "100% Cashmere",       1480m),
            ("MT-O004", "Shearling Trim Parka",          "Outerwear",  "AW25", true,  "Chocolate",     "Suede-Nappa",         1240m),

            // Accessories (2) 
            ("MT-A001", "Structured Leather Tote",       "Accessories","Core", false, "Black",         "Full-Grain Leather",  540m),
            ("MT-A002", "Silk Twill Scarf",              "Accessories","Core", false, "Floral Print",  "100% Silk",           240m),
        };

        foreach (var t in mtProducts)
        {
            products.Add(new Product
            {
                StoreId = maisonToulouse.StoreId,
                ProductCode = t.Code, ProductName = t.Name,
                Category = t.Category, Season = t.Season, IsSeasonal = t.IsSeasonal,
                Color = t.Color, Material = t.Material, Gender = "Women",
                UnitPrice = t.Price, BaseCost = Math.Round(t.Price * MT_COST, 2),
                Brand = "Maison Toulouse", LaunchDate = MtLaunch(t.Season)
            });
        }

        context.Products.AddRange(products);
        context.SaveChanges();

        // Inventories
        // Stock levels reflect brand velocity and whether the product is seasonal.
        // LastRestockDate matches LaunchDate so aging analytics start clean.
        var inventories = products.Select(p => new Inventory
        {
            ProductId             = p.ProductId,
            CurrentStock          = p.Brand == "Maison Toulouse"
                                        ? (p.IsSeasonal ? 80 : 100)
                                        : (p.IsSeasonal ? 250 : 350),
            MinimumStockThreshold = p.Brand == "Maison Toulouse"
                                        ? (p.IsSeasonal ? 10 : 12)
                                        : (p.IsSeasonal ? 30 : 40),
            LastRestockDate       = p.LaunchDate ?? new DateTime(2024, 7, 1),
            LastUpdated           = p.LaunchDate ?? new DateTime(2024, 7, 1)
        }).ToList();

        context.Inventories.AddRange(inventories);
        context.SaveChanges();
    }
}

app.Run();
