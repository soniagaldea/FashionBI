# Fashion Retail Data Analysis Platform — Project Context

**Last updated:** 2026-06-07  
**Author:** Sonia  
**Purpose:** Thesis reference document covering architecture, data flow, schema, and business logic

---

## 1. System Overview

This is a **multi-store fashion retail analytics platform** built as a bachelor's thesis project. It demonstrates a realistic end-to-end retail data pipeline: from order simulation, through a transactional API, to a synchronized analytics dashboard.

Three components work together:

| Component | Technology | Role |
|---|---|---|
| `FashionStoreAPI` | .NET 8 REST API | Transactional backend — stores, products, inventory, orders |
| `FashionDataAnalysisPlatform` | .NET 8 MVC Web App | Analytics dashboard — syncs from API, visualizes insights |
| `fashion_order_generator.py` | Python 3 | Data generator — simulates realistic historical and live orders |

---

## 2. Projects and Responsibilities

### 2.1 FashionStoreAPI

**Location:** `app/FashionStoreAPI/`  
**Database:** `FashionStoreApiDb` (SQL Server LocalDB)  
**Default URL:** `https://localhost:7151`

Manages the operational retail layer. Exposes REST endpoints consumed both by the data generator (write) and the analytics platform (read).

**Responsibilities:**
- Store catalog management (3 stores: 2 Scuffer brand, 1 Maison Toulouse)
- Product catalog with brand, pricing, seasonality metadata
- Real-time inventory tracking with automatic restocking
- Order intake with discount application and profit calculation
- Paginated, delta-capable order retrieval for downstream sync

### 2.2 FashionDataAnalysisPlatform

**Location:** `app/FashionDataAnalysisPlatform/`  
**Database:** `FashionRetailDb` (SQL Server LocalDB)  
**Default URL:** `https://localhost:7000`

The analytics and reporting layer. Maintains a local copy of all retail data, synchronized every 5 seconds from the API, and exposes rich dashboards.

**Responsibilities:**
- Background synchronization of stores, products, inventory, and orders
- Flattening order items into a `Sale` fact table for analytics
- Multi-store KPI dashboards (revenue, profit, AOV, margin)
- Inventory health analysis (aging, turnover, dead stock, reorder priority)
- Sales analysis (channel split, size/color performance, discount efficiency)
- Cross-store comparison and benchmarking
- CSV import for bulk data bootstrap
- Insight generation (up to 6 smart narrative insights per view)

### 2.3 fashion_order_generator.py

**Location:** `data_generator/fashion_order_generator.py`

A Python 3 simulation engine that generates statistically realistic order data representing 18 months of retail activity.

**Responsibilities:**
- Historical mode: generates 18 months of backdated orders (12–35/day with weekend uplift ×1.45)
- Live mode: continuously posts one order batch every 10 seconds
- Models store-specific traffic volume, channel mix, and discount behavior
- Applies seasonal demand curves per product category
- Simulates basket affinity (cross-sell attachment rate 35%)

---

## 3. Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Data Generator                           │
│                  fashion_order_generator.py                     │
│                                                                 │
│  Historical Mode: 18 months backdated orders                    │
│  Live Mode:       new order every 10 seconds                    │
└────────────────────────┬────────────────────────────────────────┘
                         │  POST /api/Orders
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                      FashionStoreAPI                            │
│                    (.NET 8 REST API)                            │
│                                                                 │
│  ┌────────────┐  ┌──────────┐  ┌───────────┐  ┌───────────┐   │
│  │  Stores    │  │ Products │  │ Inventory │  │  Orders   │   │
│  │ Controller │  │Controller│  │Controller │  │Controller │   │
│  └────────────┘  └──────────┘  └───────────┘  └───────────┘   │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              FashionStoreApiDb (SQL Server)              │   │
│  │  Stores │ Products │ Inventories │ Orders │ OrderItems   │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
          │                                      ▲
          │  GET /api/Stores                     │
          │  GET /api/Products                   │
          │  GET /api/Inventory                  │  Auto-restock
          │  GET /api/Orders?since=...&page=...  │  when threshold
          │                                      │  crossed
          ▼
┌─────────────────────────────────────────────────────────────────┐
│               FashionDataAnalysisPlatform                       │
│                  (.NET 8 MVC Web App)                           │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │       StoreSyncBackgroundService (every 5 seconds)       │  │
│  │  Syncs: Stores → Products → Inventory → Orders→Sales     │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │                    FashionRetailDb                       │  │
│  │  StoreConnections │ Stores │ Products                    │  │
│  │  Sales (fact) │ Inventories │ Predictions                │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌─────────┐ ┌───────┐ ┌───────────┐ ┌──────────────────────┐ │
│  │Dashboard│ │ Sales │ │Inventories│ │  StoreComparison     │ │
│  └─────────┘ └───────┘ └───────────┘ └──────────────────────┘ │
│                                                                 │
│                    Razor Views + Bootstrap 5                    │
└─────────────────────────────────────────────────────────────────┘
```

### Architecture Pattern

- **FashionStoreAPI:** Repository pattern via EF Core, thin controllers, seed data at startup
- **FashionDataAnalysisPlatform:** Service-layer background sync, CQRS-light (writes via sync service, reads via controllers), fat ViewModels assembled in controllers
- **Communication:** HTTP polling (not webhooks/events) — pull-based sync every 5 seconds

---

## 4. Database Schemas

### 4.1 FashionStoreApiDb

#### Store
| Column | Type | Notes |
|---|---|---|
| StoreId | int PK | Auto-increment |
| StoreName | string | |
| City | string | |
| Country | string | |
| StoreType | string | "Flagship", "Urban", "Boutique" |
| Region | string | "Eastern Europe", "Western Europe" |

**Seed data:** Scuffer District (Bucharest), Scuffer Downtown (Cluj-Napoca), Maison Toulouse (Paris)

#### Product
| Column | Type | Notes |
|---|---|---|
| ProductId | int PK | |
| StoreId | int FK → Store | |
| ProductCode | string | e.g., "P001" |
| ProductName | string | |
| Category | string | Tops, Trousers, Outerwear, Accessories, Dresses, Blazers |
| Color | string | Base color |
| Season | string | |
| UnitPrice | decimal | Retail price |
| Brand | string | "Scuffer" or "Maison Toulouse" |
| Gender | string | |
| Material | string | |
| BaseCost | decimal | Unit cost (used for profit calc) |
| IsSeasonal | bool | |
| LaunchDate | DateTime | |

**Unique index:** (StoreId, ProductCode)  
**Seed data:** 60 products total — 40 Scuffer (stores 1–2), 20 Maison Toulouse (store 3)

#### Inventory
| Column | Type | Notes |
|---|---|---|
| InventoryId | int PK | |
| ProductId | int FK → Product | 1:1 |
| CurrentStock | int | |
| MinimumStockThreshold | int | 40 (Scuffer) / 15 (Maison Toulouse) |
| LastRestockDate | DateTime? | |
| LastUpdated | DateTime | |

**Initial stock:** 350 units (Scuffer) / 120 units (Maison Toulouse)  
**Reorder rule:** When stock ≤ threshold → add MAX(threshold × 3, 100) units

#### Order
| Column | Type | Notes |
|---|---|---|
| OrderId | int PK | |
| StoreId | int FK → Store | |
| OrderDate | DateTime | |
| TotalAmount | decimal | Sum of all line totals |
| CustomerId | int? | 10,000–99,999 range |
| SalesChannel | string | "Online", "MobileApp", "Physical" |

#### OrderItem
| Column | Type | Notes |
|---|---|---|
| OrderItemId | int PK | |
| OrderId | int FK → Order | |
| ProductId | int FK → Product | Restricted delete |
| Quantity | int | |
| UnitPrice | decimal | After discount applied |
| LineTotal | decimal | UnitPrice × Quantity |
| Size | string | XS/S/M/L/XL/XXL |
| Color | string | Actual sale color |
| DiscountPercent | decimal | 0–50% |
| TotalCost | decimal | BaseCost × Quantity |
| Profit | decimal | LineTotal − TotalCost |

---

### 4.2 FashionRetailDb

#### StoreConnection
| Column | Type | Notes |
|---|---|---|
| StoreConnectionId | int PK | |
| StoreName | string | Label for the connection |
| StoreApiUrl | string | e.g., "https://localhost:7151" |
| IsActive | bool | Controls sync participation |
| LastSyncAt | DateTime? | Used for delta order sync |

#### Store (local mirror)
| Column | Type | Notes |
|---|---|---|
| StoreId | int PK | Local PK (not external) |
| StoreConnectionId | int FK → StoreConnection | |
| ExternalStoreId | int | ID from FashionStoreAPI |
| StoreName, City, Country, StoreType, Region | string | Mirrored fields |

#### Product (local mirror)
| Column | Type | Notes |
|---|---|---|
| ProductId | int PK | Local PK |
| StoreConnectionId | int FK | |
| StoreId | int FK → Store | |
| ExternalProductId | int | ID from FashionStoreAPI |
| ProductCode, ProductName, Category, Color, Season | string | |
| UnitPrice, BaseCost | decimal | |
| Brand, Gender, Material | string | |
| IsSeasonal | bool | |
| LaunchDate | DateTime | |

**Unique index:** (StoreConnectionId, StoreId, ProductCode)

#### Sale (fact table)
| Column | Type | Notes |
|---|---|---|
| SaleId | int PK | |
| ProductId | int FK → Product | |
| StoreConnectionId | int FK | |
| StoreId | int FK → Store | |
| ExternalOrderId | int | For deduplication |
| ExternalOrderItemId | int | For deduplication |
| ExternalProductCode | string | |
| SaleDate | DateTime | |
| Quantity | int | |
| UnitPrice | decimal | |
| Revenue | decimal | = LineTotal from API |
| Size, Color | string | |
| DiscountPercent | decimal | |
| TotalCost | decimal | |
| Profit | decimal | |
| CustomerId | int? | |
| SalesChannel | string | |

**Deduplication key:** (StoreConnectionId, StoreId, ExternalOrderId, ExternalOrderItemId)

#### Inventory (snapshot)
| Column | Type | Notes |
|---|---|---|
| InventoryId | int PK | |
| ProductId | int FK | |
| StoreConnectionId | int FK | |
| StoreId | int FK | |
| ExternalInventoryId | int | |
| CurrentStock | int | |
| MinimumStockThreshold | int | |
| LastRestockDate | DateTime? | |
| LastUpdated | DateTime | |

#### Prediction (future use)
| Column | Type | Notes |
|---|---|---|
| PredictionId | int PK | |
| ProductId | int FK | |
| PredictionDate | DateTime | |
| PredictedSales | decimal | |
| RecommendedStock | int | |
| ModelName | string | |

---

## 5. Data Flow

### 5.1 Order Creation Flow (Write Path)

```
data_generator.py
  │
  ├─ GET /api/Stores        → fetch storeId list
  ├─ GET /api/Products      → fetch productCode, price, brand metadata
  │
  └─ POST /api/Orders  ──► FashionStoreAPI
                              │
                              ├─ Validate store + products + stock
                              ├─ Calculate: discountedPrice, lineTotal, totalCost, profit
                              ├─ Decrement Inventory.CurrentStock
                              ├─ If stock ≤ threshold → trigger restock
                              └─ Persist Order + OrderItems → FashionStoreApiDb
```

### 5.2 Sync Flow (Read / Analytics Path)

```
StoreSyncBackgroundService (every 5 seconds)
  │
  ├─ For each active StoreConnection:
  │
  ├─ GET /api/Stores
  │     └─ Upsert local Store records (match on ExternalStoreId)
  │
  ├─ GET /api/Products
  │     └─ Upsert local Product records (match on ExternalProductId)
  │
  ├─ GET /api/Inventory
  │     └─ Upsert local Inventory records (match on ExternalInventoryId)
  │
  └─ GET /api/Orders?since={LastSyncAt}&page={N}&pageSize=500
        └─ Paginate until exhausted
              └─ Per OrderItem:
                    ├─ Check deduplication key → skip if exists
                    └─ Insert Sale record
```

### 5.3 Cross-System ID Mapping

| FashionStoreAPI field | Mapped to (FashionRetailDb) |
|---|---|
| `Store.StoreId` | `Store.ExternalStoreId` |
| `Product.ProductId` | `Product.ExternalProductId` |
| `Inventory.InventoryId` | `Inventory.ExternalInventoryId` |
| `Order.OrderId` | `Sale.ExternalOrderId` |
| `OrderItem.OrderItemId` | `Sale.ExternalOrderItemId` |

---

## 6. API Endpoints Reference

### FashionStoreAPI (`https://localhost:7151`)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/Stores` | All stores with metadata |
| GET | `/api/Products` | All products with current stock |
| GET | `/api/Products/store/{storeId}` | Products for a specific store |
| GET | `/api/Inventory` | All inventory with product+store details |
| GET | `/api/Inventory/store/{storeId}` | Inventory for a specific store |
| GET | `/api/Orders?since=&page=&pageSize=` | Paginated orders (delta sync capable) |
| POST | `/api/Orders` | Create order, decrement stock, maybe restock |

### FashionDataAnalysisPlatform

| Route | Description |
|---|---|
| `/Dashboard/Index?storeIds=&dateRange=` | Main KPI dashboard |
| `/Dashboard/LiveMetrics` | AJAX JSON endpoint for live refresh |
| `/Sales/Index?storeIds=&dateRange=` | Sales analytics (channel, color, size) |
| `/Inventories/Index?storeIds=&dateRange=` | Inventory health dashboard |
| `/StoreComparison/Index?dateRange=` | Cross-store benchmarking |
| `/Products/*` | Product CRUD |
| `/Sales/*` | Sales CRUD |
| `/Inventories/*` | Inventory CRUD |
| `/Import/ImportProducts` | Bulk CSV product import |
| `/Import/ImportInventory` | Bulk CSV inventory import |
| `/Import/ImportSales` | Bulk CSV sales import |
| `/StoreConnections/Index` | Connection configuration (stub) |

---

## 7. Business Logic

### 7.1 Profit Calculation

```
discountedPrice = UnitPrice × (1 − DiscountPercent / 100)
LineTotal       = discountedPrice × Quantity
TotalCost       = BaseCost × Quantity
Profit          = LineTotal − TotalCost
ProfitMargin %  = (Profit / Revenue) × 100
```

**Base cost ratios (by brand):**
- Scuffer: 45% of retail price
- Maison Toulouse: 32% of retail price

### 7.2 Inventory Auto-Restock

Triggered inside `POST /api/Orders` after each item is written:
```
if (CurrentStock ≤ MinimumStockThreshold):
    reorderQty = MAX(threshold × 3, 100)
    CurrentStock += reorderQty
    LastRestockDate = now
```

### 7.3 Dashboard Chart Granularity

| Date Range | Chart Granularity | Label Format |
|---|---|---|
| 7 days | Daily | "Mon" |
| 30 days | Daily | "15 Jan" |
| 90 days | Weekly (Monday) | "W of 15 Jan" |
| 12m / All | Monthly | "Jan 2025" |

### 7.4 Inventory Health Metrics

| Metric | Formula |
|---|---|
| Weeks of Cover | CurrentStock / (Units sold in period / weeks) |
| Sell-Through Rate | Units sold / (Units sold + CurrentStock) |
| Inventory Turnover | Cost of Goods Sold / Average Inventory Value |
| Dead Stock | Products with no sales in last 90 days |
| Aging buckets | <30d, 30–60d, 61–90d, 91–180d, >180d since last sale |

### 7.5 Reorder Priority

| Level | Condition |
|---|---|
| Critical | CurrentStock = 0 |
| High | 0 < CurrentStock ≤ MinimumStockThreshold |
| Medium | Stock ≤ 2× threshold |
| Low | Stock > 2× threshold |

---

## 8. Data Generator — Key Behaviors

### Store Profiles

| Store | Volume Weight | Discount Modifier | Online | MobileApp | Physical |
|---|---|---|---|---|---|
| Scuffer District (1) | 1.60 | 1.00 | 55% | 35% | 10% |
| Scuffer Downtown (2) | 1.10 | 0.90 | 35% | 25% | 40% |
| Maison Toulouse (3) | 0.40 | 0.25 | 40% | 20% | 40% |

### Seasonal Demand (monthly weights, Jan–Dec)

| Category | J | F | M | A | M | J | J | A | S | O | N | D |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Tops | 3 | 4 | 6 | 8 | 9 | 10 | 10 | 9 | 7 | 5 | 4 | 3 |
| Trousers | 7 | 7 | 8 | 8 | 8 | 7 | 6 | 7 | 8 | 9 | 8 | 7 |
| Outerwear | 10 | 9 | 5 | 1.5 | 0.5 | 0.1 | 0.1 | 0.5 | 3 | 7 | 9 | 10 |
| Accessories | 4 | 4 | 5 | 6 | 7 | 8 | 9 | 9 | 7 | 6 | 8 | 10 |
| Dresses | 1 | 1 | 3 | 6 | 8.5 | 10 | 10 | 9 | 4.5 | 2 | 1.5 | 1 |
| Blazers | 6 | 6 | 8 | 9 | 7 | 4 | 3 | 4 | 8 | 9 | 7 | 6 |

### Discount Campaigns (Scuffer)

| Period | Category Targets | Discount Range |
|---|---|---|
| January | Outerwear, Blazers | 20–50% (winter clearance) |
| July | Tops, Dresses | 20–40% (summer clearance) |
| Nov 22–30 | All | 20–40% (Black Friday) |
| Slow movers (any) | — | 10–20% (18% chance) |
| General (any) | — | 10–15% (5% chance) |

**Maison Toulouse:** Maximum 10–15% discount, only on slow movers or Black Friday (luxury positioning).

### Basket Affinity

35% chance to attach a companion product per order:

| Anchor Category | → | Companion |
|---|---|---|
| Trousers | | Accessories |
| Tops | | Trousers |
| Outerwear | | Accessories |
| Dresses | | Accessories |
| Blazers | | Trousers |

---

## 9. Technology Stack

| Layer | Technology | Version |
|---|---|---|
| API Framework | ASP.NET Core | 8.0 |
| Web Framework | ASP.NET Core MVC + Razor | 8.0 |
| ORM | Entity Framework Core | 8.0.4 |
| Database | SQL Server LocalDB | — |
| CSV parsing | CsvHelper | 33.1.0 |
| API docs | Swagger (Swashbuckle) | 6.4.0 |
| Frontend | Bootstrap 5 + jQuery | — |
| Data generation | Python 3 | — |
| HTTP client (Python) | requests + urllib3 | — |

---

## 10. Migration History

### FashionStoreApiDb

| Migration | Date | Changes |
|---|---|---|
| InitialCreate | 2026-04-29 | Stores, Products, Inventories, Orders, OrderItems |
| AddFashionAnalyticsFields | 2026-05-19 | Region/StoreType, Brand/Gender/Material/BaseCost/IsSeasonal/LaunchDate, CustomerId/SalesChannel, Size/Color/DiscountPercent/TotalCost/Profit, MinimumStockThreshold/LastRestockDate |

### FashionRetailDb

| Migration | Date | Changes |
|---|---|---|
| InitialCreate | 2026-04-21 | Products, Inventories, Predictions, Sales |
| AddStoreConnections | 2026-04-29 | StoreConnections table, StoreConnectionId on Sales |
| AddStoresAndExternalIds | 2026-04-29 | Stores table, ExternalXxxId tracking columns, composite unique index |
| AddFashionAnalyticsFields | 2026-05-19 | Mirrors FashionStoreAPI migration — analytics fields added to Sales/Products/Inventories |

---

## 11. Setup and Startup Order

```bash
# 1. Start FashionStoreAPI (seeds 3 stores + 60 products on first run)
cd app/FashionStoreAPI
dotnet ef database update
dotnet run
# → https://localhost:7151 | Swagger at /swagger/index.html

# 2. Generate 18 months of historical data (run once)
cd data_generator
python fashion_order_generator.py --historical
# → ~50k–80k orders posted depending on date range

# 3. Start FashionDataAnalysisPlatform
cd app/FashionDataAnalysisPlatform
dotnet ef database update
dotnet run
# → https://localhost:7000 | BackgroundService begins syncing immediately

# 4. (Optional) Run live order generator
python fashion_order_generator.py
# → New orders every 10 seconds, picked up by sync within 5 seconds
```

---

## 12. Known Limitations / Future Work

- **Authentication:** No auth on either API or web app (thesis scope, not production-ready)
- **StoreConnection UI:** `/StoreConnections` view is a stub — connections must be seeded manually in the DB
- **Prediction table:** Schema exists, no ML model implemented yet
- **Single-instance sync:** BackgroundService runs in the same process as the web app; no fault tolerance
- **Hard-coded API URL:** `https://localhost:7151` — must be updated in StoreConnection records for any other environment
- **SSL certs:** Python generator uses `verify=False` for localhost; not suitable for production
