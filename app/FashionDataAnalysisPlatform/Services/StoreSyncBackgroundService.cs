using FashionDataAnalysisPlatform.Data;
using FashionDataAnalysisPlatform.Dtos;
using FashionDataAnalysisPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FashionDataAnalysisPlatform.Services
{
    public class StoreSyncBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<StoreSyncBackgroundService> _logger;
        private readonly HttpClient _httpClient;

        public StoreSyncBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<StoreSyncBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _httpClient = new HttpClient();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncStoresAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during store synchronization.");
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        private async Task SyncStoresAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var connections = await context.StoreConnections
                .Where(c => c.IsActive)
                .ToListAsync(stoppingToken);

            foreach (var connection in connections)
            {
                var apiUrl = connection.StoreApiUrl.TrimEnd('/');

                await SyncStoreEntitiesAsync(context, connection, apiUrl, stoppingToken);
                await SyncProductsAsync(context, connection, apiUrl, stoppingToken);
                await SyncInventoryAsync(context, connection, apiUrl, stoppingToken);
                await SyncOrdersAsync(context, connection, apiUrl, stoppingToken);

                connection.LastSyncAt = DateTime.Now;
                await context.SaveChangesAsync(stoppingToken);
            }
        }

        private async Task SyncStoreEntitiesAsync(
            AppDbContext context,
            StoreConnection connection,
            string apiUrl,
            CancellationToken stoppingToken)
        {
            var response = await _httpClient.GetAsync($"{apiUrl}/api/Stores", stoppingToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Could not sync stores from {ApiUrl}. Status: {StatusCode}", apiUrl, response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(stoppingToken);

            var stores = JsonSerializer.Deserialize<List<StoreApiDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (stores == null)
            {
                return;
            }

            foreach (var apiStore in stores)
            {
                var store = await context.Stores.FirstOrDefaultAsync(s =>
                    s.StoreConnectionId == connection.StoreConnectionId &&
                    s.ExternalStoreId == apiStore.StoreId,
                    stoppingToken);

                if (store == null)
                {
                    store = new Store
                    {
                        StoreConnectionId = connection.StoreConnectionId,
                        ExternalStoreId = apiStore.StoreId,
                        StoreName = apiStore.StoreName,
                        City = apiStore.City,
                        Country = apiStore.Country,
                        StoreType = apiStore.StoreType,
                        Region = apiStore.Region
                    };

                    context.Stores.Add(store);
                }
                else
                {
                    store.StoreName = apiStore.StoreName;
                    store.City = apiStore.City;
                    store.Country = apiStore.Country;
                    store.StoreType = apiStore.StoreType;
                    store.Region = apiStore.Region;
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }

        private async Task SyncProductsAsync(
            AppDbContext context,
            StoreConnection connection,
            string apiUrl,
            CancellationToken stoppingToken)
        {
            var response = await _httpClient.GetAsync($"{apiUrl}/api/Products", stoppingToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Could not sync products from {ApiUrl}. Status: {StatusCode}", apiUrl, response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(stoppingToken);

            var products = JsonSerializer.Deserialize<List<ProductApiDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (products == null)
            {
                return;
            }

            foreach (var apiProduct in products)
            {
                var localStore = await context.Stores.FirstOrDefaultAsync(s =>
                    s.StoreConnectionId == connection.StoreConnectionId &&
                    s.ExternalStoreId == apiProduct.StoreId,
                    stoppingToken);

                if (localStore == null)
                {
                    continue;
                }

                var product = await context.Products.FirstOrDefaultAsync(p =>
                    p.StoreConnectionId == connection.StoreConnectionId &&
                    p.StoreId == localStore.StoreId &&
                    p.ExternalProductId == apiProduct.ProductId,
                    stoppingToken);

                if (product == null)
                {
                    product = new Product
                    {
                        StoreConnectionId = connection.StoreConnectionId,
                        StoreId = localStore.StoreId,
                        ExternalProductId = apiProduct.ProductId,
                        ProductCode = apiProduct.ProductCode,
                        ProductName = apiProduct.ProductName,
                        Category = apiProduct.Category,
                        Color = apiProduct.Color,
                        Season = apiProduct.Season,
                        UnitPrice = apiProduct.UnitPrice,
                        Brand = apiProduct.Brand,
                        Gender = apiProduct.Gender,
                        Material = apiProduct.Material,
                        BaseCost = apiProduct.BaseCost,
                        IsSeasonal = apiProduct.IsSeasonal,
                        LaunchDate = apiProduct.LaunchDate
                    };

                    context.Products.Add(product);
                }
                else
                {
                    product.ProductCode = apiProduct.ProductCode;
                    product.ProductName = apiProduct.ProductName;
                    product.Category = apiProduct.Category;
                    product.Color = apiProduct.Color;
                    product.Season = apiProduct.Season;
                    product.UnitPrice = apiProduct.UnitPrice;
                    product.Brand = apiProduct.Brand;
                    product.Gender = apiProduct.Gender;
                    product.Material = apiProduct.Material;
                    product.BaseCost = apiProduct.BaseCost;
                    product.IsSeasonal = apiProduct.IsSeasonal;
                    product.LaunchDate = apiProduct.LaunchDate;
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }

        private async Task SyncInventoryAsync(
            AppDbContext context,
            StoreConnection connection,
            string apiUrl,
            CancellationToken stoppingToken)
        {
            var response = await _httpClient.GetAsync($"{apiUrl}/api/Inventory", stoppingToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Could not sync inventory from {ApiUrl}. Status: {StatusCode}", apiUrl, response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(stoppingToken);

            var inventoryItems = JsonSerializer.Deserialize<List<InventoryApiDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (inventoryItems == null)
            {
                return;
            }

            foreach (var apiInventory in inventoryItems)
            {
                var localStore = await context.Stores.FirstOrDefaultAsync(s =>
                    s.StoreConnectionId == connection.StoreConnectionId &&
                    s.ExternalStoreId == apiInventory.StoreId,
                    stoppingToken);

                if (localStore == null)
                {
                    continue;
                }

                var product = await context.Products.FirstOrDefaultAsync(p =>
                    p.StoreConnectionId == connection.StoreConnectionId &&
                    p.StoreId == localStore.StoreId &&
                    p.ExternalProductId == apiInventory.ProductId,
                    stoppingToken);

                if (product == null)
                {
                    continue;
                }

                var inventory = await context.Inventories.FirstOrDefaultAsync(i =>
                    i.StoreConnectionId == connection.StoreConnectionId &&
                    i.StoreId == localStore.StoreId &&
                    i.ExternalInventoryId == apiInventory.InventoryId,
                    stoppingToken);

                if (inventory == null)
                {
                    inventory = new Inventory
                    {
                        StoreConnectionId = connection.StoreConnectionId,
                        StoreId = localStore.StoreId,
                        ExternalInventoryId = apiInventory.InventoryId,
                        ProductId = product.ProductId,
                        CurrentStock = apiInventory.CurrentStock,
                        MinimumStockThreshold = apiInventory.MinimumStockThreshold,
                        LastRestockDate = apiInventory.LastRestockDate,
                        LastUpdated = apiInventory.LastUpdated
                    };

                    context.Inventories.Add(inventory);
                }
                else
                {
                    inventory.ProductId = product.ProductId;
                    inventory.CurrentStock = apiInventory.CurrentStock;
                    inventory.MinimumStockThreshold = apiInventory.MinimumStockThreshold;
                    inventory.LastRestockDate = apiInventory.LastRestockDate;
                    inventory.LastUpdated = apiInventory.LastUpdated;
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }

        private async Task SyncOrdersAsync(
            AppDbContext context,
            StoreConnection connection,
            string apiUrl,
            CancellationToken stoppingToken)
        {
            var allOrders = new List<StoreOrderDto>();
            var page = 1;
            var pageSize = 500;

            while (true)
            {
                var ordersEndpoint = $"{apiUrl}/api/Orders?page={page}&pageSize={pageSize}";

                if (connection.LastSyncAt.HasValue)
                {
                    var since = Uri.EscapeDataString(connection.LastSyncAt.Value.ToString("O"));
                    ordersEndpoint += $"&since={since}";
                }

                var response = await _httpClient.GetAsync(ordersEndpoint, stoppingToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Could not sync orders from {ApiUrl}. Status: {StatusCode}", apiUrl, response.StatusCode);
                    return;
                }

                var json = await response.Content.ReadAsStringAsync(stoppingToken);

                var pageOrders = JsonSerializer.Deserialize<List<StoreOrderDto>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (pageOrders == null || pageOrders.Count == 0)
                {
                    break;
                }

                allOrders.AddRange(pageOrders);

                if (pageOrders.Count < pageSize)
                {
                    break;
                }

                page++;
            }

            var orders = allOrders;
            _logger.LogInformation("Orders downloaded from API: {Count}", orders.Count);

            if (orders == null)
            {
                return;
            }

            foreach (var order in orders)
            {
                var localStore = await context.Stores.FirstOrDefaultAsync(s =>
                    s.StoreConnectionId == connection.StoreConnectionId &&
                    s.StoreName == order.StoreName,
                    stoppingToken);

                if (localStore == null)
                {
                    continue;
                }

                foreach (var item in order.Items)
                {
                    var alreadyExists = await context.Sales.AnyAsync(s =>
                        s.StoreConnectionId == connection.StoreConnectionId &&
                        s.StoreId == localStore.StoreId &&
                        s.ExternalOrderId == order.OrderId &&
                        s.ExternalOrderItemId == item.OrderItemId,
                        stoppingToken);

                    if (alreadyExists)
                    {
                        continue;
                    }

                    var product = await context.Products.FirstOrDefaultAsync(p =>
                        p.StoreConnectionId == connection.StoreConnectionId &&
                        p.StoreId == localStore.StoreId &&
                        p.ProductCode == item.ProductCode,
                        stoppingToken);

                    if (product == null)
                    {
                        continue;
                    }

                    var sale = new Sale
                    {
                        StoreConnectionId = connection.StoreConnectionId,
                        StoreId = localStore.StoreId,
                        ProductId = product.ProductId,
                        ExternalOrderId = order.OrderId,
                        ExternalOrderItemId = item.OrderItemId,
                        ExternalProductCode = item.ProductCode,
                        SaleDate = order.OrderDate,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        Revenue = item.LineTotal,
                        Size = item.Size,
                        Color = item.Color,
                        DiscountPercent = item.DiscountPercent,
                        TotalCost = item.TotalCost,
                        Profit = item.Profit,
                        CustomerId = order.CustomerId,
                        SalesChannel = order.SalesChannel
                    };

                    context.Sales.Add(sale);
                    _logger.LogInformation("Sale added: Order {OrderId}, Item {ItemId}", order.OrderId, item.OrderItemId);
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }
    }
}