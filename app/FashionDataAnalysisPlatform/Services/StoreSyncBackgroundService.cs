using FashionDataAnalysisPlatform.Data;
using FashionDataAnalysisPlatform.Dtos;
using FashionDataAnalysisPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FashionDataAnalysisPlatform.Services
{
    public class StoreSyncBackgroundService : BackgroundService
    {
        private const string HttpClientName = "StoreSync";
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<StoreSyncBackgroundService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public StoreSyncBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<StoreSyncBackgroundService> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        // Main background loop: runs sync every 5 seconds until the application stops.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue<bool>("StoreSync:Enabled", defaultValue: true))
            {
                _logger.LogInformation("StoreSync disabled via StoreSync:Enabled = false => background sync will not run.");
                return;
            }

            _logger.LogInformation("StoreSyncBackgroundService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncAllConnectionsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in StoreSyncBackgroundService loop.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("StoreSyncBackgroundService stopped.");
        }

        // Synchronises all active store connections using a fresh scoped DbContext.
        private async Task SyncAllConnectionsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var http = _httpClientFactory.CreateClient(HttpClientName);

            var connections = await context.StoreConnections
                .Where(c => c.IsActive)
                .ToListAsync(stoppingToken);

            foreach (var connection in connections)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var apiUrl = connection.StoreApiUrl.TrimEnd('/');

                try
                {
                    await SyncConnectionAsync(context, connection, apiUrl, http, stoppingToken);

                    _logger.LogInformation(
                        "[{Connection}] Sync completed — last successful sync: {Time}.",
                        connection.StoreName,
                        connection.LastSyncAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "never");
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(
                        "[{Connection}] Sync skipped — API unavailable at {Url}. Reason: {Reason}",
                        connection.StoreName, apiUrl, ex.Message);
                }
                catch (TaskCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Request timed out, but the application is still running.
                    _logger.LogWarning(
                        "[{Connection}] Sync skipped — request timed out after 10 s. API may be unreachable at {Url}.",
                        connection.StoreName, apiUrl);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw; // Application is shutting down; exit the loop cleanly.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[{Connection}] Unexpected error syncing {Url}. Connection skipped.",
                        connection.StoreName, apiUrl);
                }
            }
        }

        // Synchronises one store connection in the required order: stores, products, inventory, then orders.
        private async Task SyncConnectionAsync(
            AppDbContext context,
            StoreConnection connection,
            string apiUrl,
            HttpClient http,
            CancellationToken stoppingToken)
        {
            await SyncStoreEntitiesAsync(context, connection, apiUrl, http, stoppingToken);
            await SyncProductsAsync(context, connection, apiUrl, http, stoppingToken);
            await SyncInventoryAsync(context, connection, apiUrl, http, stoppingToken);

            var (ordersDownloaded, salesAdded, missingProducts) =
                await SyncOrdersAsync(context, connection, apiUrl, http, stoppingToken);

            // Advance LastSyncAt only when the cycle completed safely.
            // If products are missing, keep the cursor unchanged so the orders can be retried.
            if (salesAdded > 0 || ordersDownloaded == 0 || missingProducts == 0)
            {
                connection.LastSyncAt = DateTime.Now;
            }
            else
            {
                _logger.LogWarning(
                    "[{Connection}] {Orders} orders downloaded, 0 sales inserted, " +
                    "{Missing} items have no matching local product. " +
                    "LastSyncAt not advanced — will retry next cycle.",
                    connection.StoreName, ordersDownloaded, missingProducts);
            }

            await context.SaveChangesAsync(stoppingToken);
        }

        private async Task SyncStoreEntitiesAsync(
            AppDbContext context,
            StoreConnection connection,
            string apiUrl,
            HttpClient http,
            CancellationToken stoppingToken)
        {
            var response = await http.GetAsync($"{apiUrl}/api/Stores", stoppingToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Could not sync stores from {ApiUrl}. Status: {StatusCode}", apiUrl, response.StatusCode);
                return;
            }

            var json   = await response.Content.ReadAsStringAsync(stoppingToken);
            var stores = JsonSerializer.Deserialize<List<StoreApiDto>>(json, JsonOpts);

            if (stores == null) return;

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
                        ExternalStoreId   = apiStore.StoreId,
                        StoreName         = apiStore.StoreName,
                        City              = apiStore.City,
                        Country           = apiStore.Country,
                        StoreType         = apiStore.StoreType,
                        Region            = apiStore.Region
                    };
                    context.Stores.Add(store);
                }
                else
                {
                    store.StoreName = apiStore.StoreName;
                    store.City      = apiStore.City;
                    store.Country   = apiStore.Country;
                    store.StoreType = apiStore.StoreType;
                    store.Region    = apiStore.Region;
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }

        private async Task SyncProductsAsync(
            AppDbContext context,
            StoreConnection connection,
            string apiUrl,
            HttpClient http,
            CancellationToken stoppingToken)
        {
            var response = await http.GetAsync($"{apiUrl}/api/Products", stoppingToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Could not sync products from {ApiUrl}. Status: {StatusCode}", apiUrl, response.StatusCode);
                return;
            }

            var json     = await response.Content.ReadAsStringAsync(stoppingToken);
            var products = JsonSerializer.Deserialize<List<ProductApiDto>>(json, JsonOpts);

            if (products == null) return;

            foreach (var apiProduct in products)
            {
                var localStore = await context.Stores.FirstOrDefaultAsync(s =>
                    s.StoreConnectionId == connection.StoreConnectionId &&
                    s.ExternalStoreId == apiProduct.StoreId,
                    stoppingToken);

                if (localStore == null) continue;

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
                        StoreId           = localStore.StoreId,
                        ExternalProductId = apiProduct.ProductId,
                        ProductCode       = apiProduct.ProductCode,
                        ProductName       = apiProduct.ProductName,
                        Category          = apiProduct.Category,
                        Color             = apiProduct.Color,
                        Season            = apiProduct.Season,
                        UnitPrice         = apiProduct.UnitPrice,
                        Brand             = apiProduct.Brand,
                        Gender            = apiProduct.Gender,
                        Material          = apiProduct.Material,
                        BaseCost          = apiProduct.BaseCost,
                        IsSeasonal        = apiProduct.IsSeasonal,
                        LaunchDate        = apiProduct.LaunchDate
                    };
                    context.Products.Add(product);
                }
                else
                {
                    product.ProductCode = apiProduct.ProductCode;
                    product.ProductName = apiProduct.ProductName;
                    product.Category    = apiProduct.Category;
                    product.Color       = apiProduct.Color;
                    product.Season      = apiProduct.Season;
                    product.UnitPrice   = apiProduct.UnitPrice;
                    product.Brand       = apiProduct.Brand;
                    product.Gender      = apiProduct.Gender;
                    product.Material    = apiProduct.Material;
                    product.BaseCost    = apiProduct.BaseCost;
                    product.IsSeasonal  = apiProduct.IsSeasonal;
                    product.LaunchDate  = apiProduct.LaunchDate;
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }

        private async Task SyncInventoryAsync(
            AppDbContext context,
            StoreConnection connection,
            string apiUrl,
            HttpClient http,
            CancellationToken stoppingToken)
        {
            var response = await http.GetAsync($"{apiUrl}/api/Inventory", stoppingToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Could not sync inventory from {ApiUrl}. Status: {StatusCode}", apiUrl, response.StatusCode);
                return;
            }

            var json           = await response.Content.ReadAsStringAsync(stoppingToken);
            var inventoryItems = JsonSerializer.Deserialize<List<InventoryApiDto>>(json, JsonOpts);

            if (inventoryItems == null) return;

            foreach (var apiInventory in inventoryItems)
            {
                var localStore = await context.Stores.FirstOrDefaultAsync(s =>
                    s.StoreConnectionId == connection.StoreConnectionId &&
                    s.ExternalStoreId == apiInventory.StoreId,
                    stoppingToken);

                if (localStore == null) continue;

                var product = await context.Products.FirstOrDefaultAsync(p =>
                    p.StoreConnectionId == connection.StoreConnectionId &&
                    p.StoreId == localStore.StoreId &&
                    p.ExternalProductId == apiInventory.ProductId,
                    stoppingToken);

                if (product == null) continue;

                var inventory = await context.Inventories.FirstOrDefaultAsync(i =>
                    i.StoreConnectionId == connection.StoreConnectionId &&
                    i.StoreId == localStore.StoreId &&
                    i.ExternalInventoryId == apiInventory.InventoryId,
                    stoppingToken);

                if (inventory == null)
                {
                    inventory = new Inventory
                    {
                        StoreConnectionId   = connection.StoreConnectionId,
                        StoreId             = localStore.StoreId,
                        ExternalInventoryId = apiInventory.InventoryId,
                        ProductId           = product.ProductId,
                        CurrentStock        = apiInventory.CurrentStock,
                        MinimumStockThreshold = apiInventory.MinimumStockThreshold,
                        LastRestockDate     = apiInventory.LastRestockDate,
                        LastUpdated         = apiInventory.LastUpdated
                    };
                    context.Inventories.Add(inventory);
                }
                else
                {
                    inventory.ProductId             = product.ProductId;
                    inventory.CurrentStock          = apiInventory.CurrentStock;
                    inventory.MinimumStockThreshold = apiInventory.MinimumStockThreshold;
                    inventory.LastRestockDate        = apiInventory.LastRestockDate;
                    inventory.LastUpdated            = apiInventory.LastUpdated;
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }

        // Fetches orders incrementally and transforms each order item into a Sales fact row.
        // Returns sync counters used to decide whether LastSyncAt can be advanced.
        private async Task<(int ordersDownloaded, int salesAdded, int missingProducts)> SyncOrdersAsync(
            AppDbContext context,
            StoreConnection connection,
            string apiUrl,
            HttpClient http,
            CancellationToken stoppingToken)
        {
            // Fetch all order pages from the API.
            var allOrders  = new List<StoreOrderDto>();
            var page       = 1;
            const int pageSize = 500;

            while (true)
            {
                var endpoint = $"{apiUrl}/api/Orders?page={page}&pageSize={pageSize}";

                if (connection.LastSyncAt.HasValue)
                {
                    var since = Uri.EscapeDataString(connection.LastSyncAt.Value.ToString("O"));
                    endpoint += $"&since={since}";
                }

                var response = await http.GetAsync(endpoint, stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "[{Connection}] Could not fetch orders. Status: {Status}",
                        connection.StoreName, response.StatusCode);
                    return (0, 0, 0);
                }

                var json       = await response.Content.ReadAsStringAsync(stoppingToken);
                var pageOrders = JsonSerializer.Deserialize<List<StoreOrderDto>>(json, JsonOpts);

                if (pageOrders == null || pageOrders.Count == 0) break;

                allOrders.AddRange(pageOrders);
                if (pageOrders.Count < pageSize) break;
                page++;
            }

            _logger.LogInformation(
                "[{Connection}] Orders downloaded: {Count}",
                connection.StoreName, allOrders.Count);

            if (allOrders.Count == 0) return (0, 0, 0);

            // Preload stores and products to avoid the N+1 query problem.

            var storeMap = await context.Stores
                .Where(s => s.StoreConnectionId == connection.StoreConnectionId)
                .ToDictionaryAsync(s => s.StoreName, stoppingToken);

            var productMap = (await context.Products
                .Where(p => p.StoreConnectionId == connection.StoreConnectionId)
                .ToListAsync(stoppingToken))
                .ToDictionary(p => (p.StoreId, p.ProductCode));

            if (storeMap.Count == 0)
            {
                _logger.LogWarning(
                    "[{Connection}] No local stores found. Stores must sync before orders.",
                    connection.StoreName);
                return (allOrders.Count, 0, 1);
            }

            if (productMap.Count == 0)
            {
                _logger.LogWarning(
                    "[{Connection}] No local products found. Products must sync before orders.",
                    connection.StoreName);
                return (allOrders.Count, 0, 1);
            }

            _logger.LogInformation(
                "[{Connection}] Lookup tables ready — {Stores} stores, {Products} products",
                connection.StoreName, storeMap.Count, productMap.Count);

            // Build the deduplication set for the incoming orders.
            var incomingOrderIds = allOrders.Select(o => (int?)o.OrderId).ToHashSet();

            var existingKeys = (await context.Sales
                .Where(s => s.StoreConnectionId == connection.StoreConnectionId
                            && s.ExternalOrderId != null
                            && incomingOrderIds.Contains(s.ExternalOrderId))
                .Select(s => new { s.ExternalOrderId, s.ExternalOrderItemId })
                .ToListAsync(stoppingToken))
                .Select(x => (orderId: x.ExternalOrderId ?? 0, itemId: x.ExternalOrderItemId ?? 0))
                .ToHashSet();

            // Process orders and create Sale rows.
            int addedSales       = 0;
            int skippedNoStore   = 0;
            int skippedNoProd    = 0;
            int skippedDuplicate = 0;

            foreach (var order in allOrders)
            {
                if (!storeMap.TryGetValue(order.StoreName, out var localStore))
                {
                    _logger.LogWarning(
                        "[{Connection}] Local store not found for OrderId={OrderId}. " +
                        "API StoreName='{StoreName}'. Available: [{Available}]",
                        connection.StoreName, order.OrderId, order.StoreName,
                        string.Join(", ", storeMap.Keys));
                    skippedNoStore++;
                    continue;
                }

                foreach (var item in order.Items)
                {
                    var dedupKey = (orderId: order.OrderId, itemId: item.OrderItemId);

                    if (existingKeys.Contains(dedupKey))
                    {
                        skippedDuplicate++;
                        continue;
                    }

                    if (!productMap.TryGetValue((localStore.StoreId, item.ProductCode), out var product))
                    {
                        _logger.LogWarning(
                            "[{Connection}] Product not found — " +
                            "StoreName='{StoreName}', LocalStoreId={LocalStoreId}, " +
                            "ProductCode='{ProductCode}', OrderId={OrderId}",
                            connection.StoreName, order.StoreName,
                            localStore.StoreId, item.ProductCode, order.OrderId);
                        skippedNoProd++;
                        continue;
                    }

                    context.Sales.Add(new Sale
                    {
                        StoreConnectionId   = connection.StoreConnectionId,
                        StoreId             = localStore.StoreId,
                        ProductId           = product.ProductId,
                        ExternalOrderId     = order.OrderId,
                        ExternalOrderItemId = item.OrderItemId,
                        ExternalProductCode = item.ProductCode,
                        SaleDate            = order.OrderDate,
                        Quantity            = item.Quantity,
                        UnitPrice           = item.UnitPrice,
                        Revenue             = item.LineTotal,
                        Size                = item.Size,
                        Color               = item.Color,
                        DiscountPercent     = item.DiscountPercent,
                        TotalCost           = item.TotalCost,
                        Profit              = item.Profit,
                        CustomerId          = order.CustomerId,
                        SalesChannel        = order.SalesChannel
                    });

                    existingKeys.Add(dedupKey);
                    addedSales++;

                    // Save every 500 records to keep transactions small.
                    if (addedSales % 500 == 0)
                    {
                        await context.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation(
                            "[{Connection}] Batch saved — {Count} sales so far.",
                            connection.StoreName, addedSales);
                    }
                }
            }

            if (addedSales % 500 != 0)
            {
                await context.SaveChangesAsync(stoppingToken);
            }

            _logger.LogInformation(
                "[{Connection}] Order sync complete — Added={Added}, Duplicates={Dup}, " +
                "MissingStore={NoStore}, MissingProduct={NoProd}",
                connection.StoreName, addedSales, skippedDuplicate,
                skippedNoStore, skippedNoProd);

            return (allOrders.Count, addedSales, skippedNoProd);
        }
    }
}
