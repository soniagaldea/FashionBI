# FashionBI — Data Generator Specification

**Purpose:** Complete specification for a Python data generator that exposes the four REST API endpoints consumed by `StoreSyncBackgroundService`. All data must make every dashboard chart, KPI, and insight render meaningfully.

**Last updated:** 2026-06-07

---

## 1. API Contract (what the sync service calls)

The background service polls every active `StoreConnection` at `StoreApiUrl`. It calls exactly four endpoints per connection per cycle (every 5 seconds):

```
GET {baseUrl}/api/Stores
GET {baseUrl}/api/Products
GET {baseUrl}/api/Inventory
GET {baseUrl}/api/Orders?page=1&pageSize=500&since=<ISO8601>
```

The generator must be a running HTTP server that responds to these four routes.

### 1.1 `GET /api/Stores` → `List<StoreApiDto>`

```json
[
  {
    "storeId": 1,
    "storeName": "Maison Milano",
    "city": "Milan",
    "country": "Italy",
    "storeType": "Flagship",
    "region": "Southern Europe"
  }
]
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `storeId` | int | Yes | Unique per generator instance |
| `storeName` | string | Yes | Used for order→store matching (must be **identical** to the `StoreName` in orders) |
| `city` | string | No | Shown in Store Comparison card meta |
| `country` | string | No | Not currently displayed but stored |
| `storeType` | string | No | Shown in Store Comparison card meta (e.g. "Flagship", "Outlet", "Concession") |
| `region` | string | No | Stored, not currently displayed |

### 1.2 `GET /api/Products` → `List<ProductApiDto>`

```json
[
  {
    "productId": 101,
    "storeId": 1,
    "storeName": "Maison Milano",
    "productCode": "TOP-001-BLK-M",
    "productName": "Oversized Linen Shirt",
    "category": "Tops",
    "color": "Black",
    "season": "SS25",
    "unitPrice": 89.99,
    "currentStock": 0,
    "brand": "Maison",
    "gender": "Women",
    "material": "Linen",
    "baseCost": 28.50,
    "isSeasonal": true,
    "launchDate": "2025-03-01T00:00:00"
  }
]
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `productId` | int | Yes | ExternalProductId — unique per store |
| `storeId` | int | Yes | Must match a storeId from `/api/Stores` |
| `storeName` | string | Yes | Must match `storeId`'s store name |
| `productCode` | string | Yes | Used for order item→product matching; must be unique per store |
| `productName` | string | Yes | Shown in top products table, dead stock table |
| `category` | string | Yes | Drives Profitability by Category, Inventory Value by Category |
| `color` | string | No | Drives Colour Performance chart |
| `season` | string | No | Stored; used for seasonality logic in generator |
| `unitPrice` | decimal | Yes | Used to compute `Inventory Value = UnitPrice × CurrentStock` |
| `currentStock` | int | No | Ignored by sync — inventory comes from `/api/Inventory` separately |
| `brand` | string | No | Stored |
| `gender` | string | No | Stored |
| `material` | string | No | Stored |
| `baseCost` | decimal | Yes | `BaseCost` used to derive `TotalCost` and `Profit` in orders |
| `isSeasonal` | bool | Yes | Generator uses this to suppress off-season sales |
| `launchDate` | datetime? | No | Generator uses this as earliest valid sale date |

### 1.3 `GET /api/Inventory` → `List<InventoryApiDto>`

```json
[
  {
    "inventoryId": 5001,
    "productId": 101,
    "storeId": 1,
    "storeName": "Maison Milano",
    "productCode": "TOP-001-BLK-M",
    "productName": "Oversized Linen Shirt",
    "category": "Tops",
    "currentStock": 0,
    "lastUpdated": "2026-06-05T08:00:00",
    "minimumStockThreshold": 10,
    "lastRestockDate": "2025-11-12T00:00:00"
  }
]
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `inventoryId` | int | Yes | ExternalInventoryId — unique per generator |
| `productId` | int | Yes | Must match a productId from `/api/Products` for same store |
| `storeId` | int | Yes | Must match store |
| `storeName` | string | Yes | Must match store |
| `productCode` | string | Yes | For reference |
| `productName` | string | Yes | Shown in low stock / dead stock tables |
| `category` | string | Yes | Used in Inventory Value by Category chart |
| `currentStock` | int | Yes | Core inventory KPI; drives OOS/low-stock/overstock detection |
| `lastUpdated` | datetime | Yes | Fallback date for aging and dead-stock when `lastRestockDate` is null |
| `minimumStockThreshold` | int | Yes | OOS: stock==0; Low: 0 < stock ≤ threshold; Medium: ≤ 2× threshold; Low-risk: > 2× |
| `lastRestockDate` | datetime? | No | **Primary** date for inventory aging buckets; also dead-stock fallback |

### 1.4 `GET /api/Orders` → `List<StoreOrderDto>` (paginated)

Query parameters: `page` (1-based), `pageSize` (500), `since` (ISO8601, optional).
When `since` is present, return only orders with `OrderDate >= since`. When absent, return all.
Return an empty array `[]` to signal end of pagination.

```json
[
  {
    "orderId": 90001,
    "storeName": "Maison Milano",
    "orderDate": "2026-05-14T11:23:00",
    "totalAmount": 179.98,
    "customerId": 4412,
    "salesChannel": "Online",
    "items": [
      {
        "orderItemId": 180001,
        "productCode": "TOP-001-BLK-M",
        "productName": "Oversized Linen Shirt",
        "quantity": 2,
        "unitPrice": 89.99,
        "lineTotal": 179.98,
        "size": "M",
        "color": "Black",
        "discountPercent": null,
        "totalCost": 57.00,
        "profit": 122.98
      }
    ]
  }
]
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `orderId` | int | Yes | `ExternalOrderId` — used for deduplication and DISTINCT count for KPIs |
| `storeName` | string | Yes | **Must match exactly** the store's `storeName` (sync does string match, not ID) |
| `orderDate` | datetime | Yes | `SaleDate` after sync; drives all trend charts |
| `totalAmount` | decimal | Yes | Order-level total (informational; not stored on Sale) |
| `customerId` | int? | No | Stored as `CustomerId` on Sale |
| `salesChannel` | string | Yes | **Must be exactly** `"Online"`, `"Mobile"`, or `"Physical"` (case-sensitive) |
| **items[].orderItemId** | int | Yes | `ExternalOrderItemId` — used with `orderId` for exact deduplication |
| **items[].productCode** | string | Yes | Matched to `Product.ProductCode` (same store + connection) |
| **items[].productName** | string | No | Informational |
| **items[].quantity** | int | Yes | `Sale.Quantity` |
| **items[].unitPrice** | decimal | Yes | `Sale.UnitPrice` — the actual selling price after discount |
| **items[].lineTotal** | decimal | Yes | `Sale.Revenue` — `UnitPrice * Quantity` (after any discount) |
| **items[].size** | string? | No | `Sale.Size` — drives Size Performance chart; must match: `XS`, `S`, `M`, `L`, `XL`, `XXL` |
| **items[].color** | string? | No | `Sale.Color` — drives Colour Performance chart |
| **items[].discountPercent** | decimal? | No | `Sale.DiscountPercent` — drives Discount Efficiency bands |
| **items[].totalCost** | decimal? | No | `Sale.TotalCost` = `BaseCost * Quantity` |
| **items[].profit** | decimal? | Yes | `Sale.Profit` = `lineTotal - totalCost`; **critical** — drives all margin KPIs |

---

## 2. Stores

### 2.1 Recommended configuration

Generate **4–6 stores** with meaningfully different profiles so Store Comparison charts are non-trivial.

| Store | City | Type | Character |
|---|---|---|---|
| Store A | Milan | Flagship | High revenue, premium margins, physical-dominant |
| Store B | London | Online Hub | High order volume, online-dominant, moderate margin |
| Store C | Paris | Boutique | Lower volume, highest AOV, best margin |
| Store D | Berlin | Outlet | High units, deep discounts, lowest margin |
| Store E | Amsterdam | Concession | Balanced channels, medium everything |

### 2.2 Per-store variation requirements

For Store Comparison charts to be useful each store must differ on:

| Dimension | Variation needed |
|---|---|
| Revenue | Factor of 2–4× between highest and lowest store |
| Profit margin | At least 10 percentage-point spread across stores |
| AOV | At least €20 spread (needed for AOV insight card) |
| Inventory turnover | At least one store ≥ 4× (good) and one ≤ 1.5× (poor) |
| Channel dominance | At least 1 store where Online% ≥ 60%, at least 1 where Physical% ≥ 50% |
| Inventory risk | At least 1 store with high risk (OOS + low stock > 10 items), 1 with clean stock |

---

## 3. Products

### 3.1 Categories

The app groups by `Product.Category` for multiple charts. Need **6–8 distinct categories** with spread in inventory value and revenue.

Recommended categories and product counts:

| Category | Products per store | Notes |
|---|---|---|
| Tops | 20–30 | Highest unit volume |
| Dresses | 15–20 | High AOV |
| Outerwear | 10–15 | Highest unit price, seasonal |
| Trousers | 15–20 | Steady year-round |
| Accessories | 20–30 | Low price, high volume |
| Footwear | 10–15 | High price |
| Knitwear | 10–15 | Seasonal (AW) |
| Swimwear | 8–12 | Seasonal (SS) |

### 3.2 Product code format

`{CATEGORY_CODE}-{SEQ}-{COLOR_CODE}` e.g. `TOP-001-BLK`, `DRS-012-NVY`

Code must be **unique per store**. Different stores selling the same SKU should have the same `productCode` — this is intentional for cross-store comparison.

### 3.3 Pricing

`UnitPrice` and `BaseCost` drive all margin calculations. Set `BaseCost` to produce the intended gross margin before discounts.

| Category | UnitPrice range | Target gross margin (pre-discount) |
|---|---|---|
| Accessories | €15 – €80 | 60–70% |
| Tops | €40 – €120 | 55–65% |
| Trousers | €60 – €150 | 55–65% |
| Dresses | €80 – €250 | 50–60% |
| Footwear | €80 – €250 | 45–55% |
| Knitwear | €70 – €180 | 50–60% |
| Outerwear | €150 – €500 | 45–55% |
| Swimwear | €40 – €120 | 60–70% |

Formula: `BaseCost = UnitPrice × (1 - targetMargin)`

### 3.4 Seasons

| Season code | Months active | `IsSeasonal` |
|---|---|---|
| `SS25` | March – August 2025 | true |
| `AW25` | September 2025 – February 2026 | true |
| `SS26` | March – June 2026 | true |
| `CORE` | All year | false |

Seasonal products must only appear in sales during their active window. Generator should suppress order items for seasonal products outside their season.

### 3.5 Gender split

Distribute products across: `Women` (50%), `Men` (35%), `Unisex` (15%).

---

## 4. Inventory

### 4.1 Target stock states

The Inventory Intelligence page requires all five reorder priority buckets and all five aging buckets to be populated. Plan deliberately.

**Reorder priority buckets** (derived from `CurrentStock` vs `MinimumStockThreshold`):

| Bucket | Rule | Target % of SKUs |
|---|---|---|
| Critical (OOS) | `CurrentStock == 0` | 5–8% |
| High | `0 < CurrentStock < MinThreshold` | 10–15% |
| Medium | `MinThreshold ≤ CurrentStock ≤ MinThreshold × 2` | 20–25% |
| Low (healthy) | `CurrentStock > MinThreshold × 2` | 55–65% |

**Inventory aging buckets** (based on `LastRestockDate`, fallback `LastUpdated`):

| Bucket | Rule | Target % of SKUs |
|---|---|---|
| `<30d` | Restocked within 30 days of today | 25% |
| `30–60d` | 30–60 days since restock | 30% |
| `61–90d` | 61–90 days since restock | 20% |
| `91–180d` | 91–180 days since restock | 15% |
| `>180d` | More than 180 days since restock | 10% |

### 4.2 Dead stock rules

Dead stock requires: `CurrentStock > 0` AND most recent `SaleDate` for that (product, store) pair is ≥ 90 days ago (or the product was never sold and `LastRestockDate ?? LastUpdated` is ≥ 90 days ago).

Target: **8–15% of SKUs** that still have stock should be dead stock. Concentrate in the `>180d` aging bucket. Typically: slow-moving end-of-season items that weren't cleared.

### 4.3 Low stock and OOS rules

- **OOS items** (`CurrentStock == 0`): Use these for fast sellers that ran out (high-demand items). They should have recent `LastRestockDate` (< 60 days ago) to show they were active but sold through.
- **Low stock items** (`0 < CurrentStock ≤ MinThreshold`): Items approaching the threshold. `MinThreshold` should be set relative to expected weekly sales velocity (e.g. 2–3 weeks of cover).

### 4.4 MinimumStockThreshold guidance

| Category | Typical MinThreshold |
|---|---|
| Accessories | 15–25 |
| Tops / Trousers | 8–15 |
| Dresses | 5–10 |
| Outerwear | 3–8 |
| Footwear | 5–10 |
| Knitwear | 5–10 |
| Swimwear | 5–8 |

### 4.5 Inventory value targets

The `Inventory Value by Category` chart (top 8) needs spread across categories. Target total inventory value per store: **€200,000 – €800,000** (sum of `UnitPrice × CurrentStock`).

### 4.6 Sell-through and overstock

The Stock Health panel also shows:
- **Overstock SKUs**: `CurrentStock > AvgWeeklySales × 8` — target 5–10% of SKUs
- **Sell-through rate**: target **40–65%** for the data period to produce an "info/success" insight
- **Weeks of cover**: target **4–16 weeks** normal range; a few stores may go above 24 (triggers "overstock risk") or below 2 (triggers "reorder urgently")

---

## 5. Orders (Sales)

### 5.1 Date range

Generate orders spanning **at least 24 months** ending at today (2026-06-07). This ensures:
- "All Time" shows monthly trend with 24+ data points
- "Last 12 Months" shows 12 monthly points
- "Last 90 Days" produces weekly grouping with 12–13 points
- "Last 30 Days" produces daily grouping with 30 points
- "Last 7 Days" produces daily grouping with 7 points

Recommended start date: **2024-01-01**. Total span: ~30 months.

### 5.2 Order volume

| Store type | Orders / month | Items / order |
|---|---|---|
| Flagship | 400–600 | 1.8–2.5 avg |
| Online Hub | 800–1200 | 1.5–2.0 avg |
| Boutique | 150–250 | 2.0–3.0 avg |
| Outlet | 500–800 | 2.5–3.5 avg |
| Concession | 250–400 | 1.5–2.0 avg |

This yields approximately **5,000–15,000 orders per store over 2 years** — sufficient for trend charts to be non-trivial.

### 5.3 Temporal distribution

Sales must not be uniform. Apply these patterns:

**Weekly pattern** (within each month):
- Monday–Thursday: 55% of weekly orders
- Friday–Saturday: 35%
- Sunday: 10%

**Seasonal uplift multipliers** (relative to baseline month):
| Month | Multiplier |
|---|---|
| January | 0.7 (post-Christmas slump) |
| February | 0.8 |
| March | 1.1 (spring launch) |
| April | 1.0 |
| May | 1.1 |
| June | 1.2 (summer) |
| July | 0.8 (holiday slowdown) |
| August | 0.9 |
| September | 1.1 (autumn launch) |
| October | 1.1 |
| November | 1.5 (Black Friday) |
| December | 1.4 (Christmas) |

**Year-on-year growth**: Apply +10–15% YoY lift to give the Revenue Trend chart a visible upward slope that triggers the "Revenue growing" insight.

### 5.4 Revenue field calculations (per order item)

```python
unit_price_before_discount = product.unit_price
discount_pct = chosen_discount  # see §8
effective_unit_price = round(unit_price_before_discount * (1 - discount_pct / 100), 2)

# Fields in StoreOrderItemDto:
item.unitPrice       = effective_unit_price          # Sale.UnitPrice
item.lineTotal       = round(effective_unit_price * quantity, 2)  # Sale.Revenue
item.totalCost       = round(product.base_cost * quantity, 2)     # Sale.TotalCost
item.profit          = round(item.lineTotal - item.totalCost, 2)  # Sale.Profit
item.discountPercent = discount_pct if discount_pct > 0 else None
```

**Critical**: `Sale.Revenue = item.lineTotal`. This is what all revenue KPIs sum. `Sale.Profit = item.profit`. Never leave profit null — the dashboard relies on it for every margin calculation.

### 5.5 OrderId and OrderItemId

`orderId` is the `ExternalOrderId`. AOV = `TotalRevenue / COUNT(DISTINCT ExternalOrderId)`. A single orderId can have multiple items. To produce realistic AOV (€80–€200), make 60% of orders single-item and 40% multi-item.

`orderItemId` is `ExternalOrderItemId`. The deduplication check in `SyncOrdersAsync` is:
```csharp
s.ExternalOrderId == order.OrderId AND s.ExternalOrderItemId == item.OrderItemId
```
Both must be globally unique (or at minimum unique per store + connection).

---

## 6. Sales Channels

Exactly three allowed values: `"Online"`, `"Mobile"`, `"Physical"` (case-sensitive).

### 6.1 Per-store channel distribution

| Store type | Online% | Mobile% | Physical% |
|---|---|---|---|
| Flagship | 30% | 15% | 55% |
| Online Hub | 65% | 25% | 10% |
| Boutique | 20% | 10% | 70% |
| Outlet | 35% | 20% | 45% |
| Concession | 45% | 30% | 25% |

These distributions ensure the Store Comparison "Stores differ in channel strength" insight fires (requires at least 1 online-dominant store and at least 1 physical-dominant store).

### 6.2 AOV variation by channel

Online and Mobile channels typically have higher AOV than Physical (larger basket, no fitting constraints). Apply a channel multiplier to effective unit price:
- Physical: baseline
- Online: ×1.15
- Mobile: ×1.10

---

## 7. Sizes

The Size Performance chart uses **exactly** these labels with `ToUpperInvariant()` normalisation:

```
XS, S, M, L, XL, XXL
```

Only assign sizes to apparel categories (Tops, Dresses, Trousers, Knitwear, Outerwear). Not Accessories or Footwear.

### 7.1 Size distribution (bell curve)

| Size | Share of apparel units |
|---|---|
| XS | 8% |
| S | 22% |
| M | 30% |
| L | 25% |
| XL | 11% |
| XXL | 4% |

This should be reflected in both product stock allocation and order item distribution.

---

## 8. Colours

The Colour Performance chart shows top 6 colours by units sold. Need **at least 8 distinct colours** in the data so the "top 6" ranking is meaningful.

### 8.1 Recommended colour palette

| Colour | Share of units |
|---|---|
| Black | 22% |
| White | 14% |
| Navy | 12% |
| Beige | 10% |
| Camel | 8% |
| Red | 8% |
| Grey | 7% |
| Olive | 6% |
| Pink | 5% |
| Blue | 4% |
| Other | 4% |

Colours must match between products (registered `Product.Color`) and order items (`item.color`). The product `Color` field and the sale `Color` field are stored separately — they will match naturally if orders are generated from the product catalogue.

---

## 9. Discounts

The Discount Efficiency chart groups into 5 bands based on `Sale.DiscountPercent`:

| Band label | Range | `discountPercent` value |
|---|---|---|
| `0%` | No discount | `null` or `0` |
| `1–10%` | Light | 5–10 |
| `11–20%` | Moderate | 11–20 |
| `21–30%` | Heavy | 21–30 |
| `30%+` | Deep | 31–50 |

### 9.1 Distribution targets

| Band | Share of order items | Notes |
|---|---|---|
| 0% (full price) | 50% | Largest group |
| 1–10% | 20% | Loyalty / small promotions |
| 11–20% | 15% | End-of-season |
| 21–30% | 10% | Outlet / clearance |
| 30%+ | 5% | Deep clearance only |

Outlet-type stores should skew toward 21–30% and 30%+ bands. Flagship/Boutique should skew toward full-price.

### 9.2 Margin impact by band

The Discount Efficiency chart overlays profit margin% per band. For the "discounts cost X pp" insight to trigger (requires 12+ pp gap), the margin must visibly decrease with deeper discounts:

| Band | Expected profit margin |
|---|---|
| 0% | ~55–65% (product's natural gross margin) |
| 1–10% | ~48–58% |
| 11–20% | ~40–52% |
| 21–30% | ~30–42% |
| 30%+ | ~18–30% |

Since `Profit = lineTotal - TotalCost` and `lineTotal` is already discounted, this happens automatically if `BaseCost` is set correctly. No adjustment needed beyond correct cost assignment.

---

## 10. Margins

### 10.1 KPI margin thresholds

The dashboard uses three visual states for profit margin:

| Margin | Class | Text |
|---|---|---|
| ≥ 25% | `emerald` (green) | "Strong margin" |
| ≥ 10% | `amber` (yellow) | "Healthy" |
| < 10% | `red` | "Watch margin" |

Target the overall blended margin (across all discounts and products) at **28–35%** for most stores, with the Outlet store at **18–22%**.

### 10.2 Margin insight in Sales Analytics

The "Discounts are well-managed / cost X pp" insight fires when:
- `marginGap > 12` → warning (heavy discounting hurts margin)
- `marginGap ≤ 12` → success (discounts within acceptable range)

`marginGap = fullPriceMargin - deepDiscountMargin`. For realistic data, this gap will naturally be 15–25 pp, triggering the warning and making the insight non-trivial.

---

## 11. Inventory Aging

Ages are computed from `LastRestockDate` (if present) or `LastUpdated` relative to today (2026-06-07).

### 11.1 Aging bucket distribution

| Bucket | Days since restock | `LastRestockDate` range (relative to 2026-06-07) |
|---|---|---|
| `<30d` | 0–29 | 2026-05-09 → 2026-06-06 |
| `30–60d` | 30–59 | 2026-04-09 → 2026-05-08 |
| `61–90d` | 60–89 | 2026-03-10 → 2026-04-08 |
| `91–180d` | 90–179 | 2025-12-10 → 2026-03-09 |
| `>180d` | ≥ 180 | ≤ 2025-12-10 |

The inventory aging bar chart will only be interesting if all five buckets are non-zero.

### 11.2 Coupling aging to dead stock

Dead stock = `CurrentStock > 0` AND no sale in 90+ days. This naturally concentrates in the `91–180d` and `>180d` aging buckets. Make the `>180d` bucket contain mostly dead stock items (items that were restocked long ago and never sold through).

---

## 12. Dead Stock

### 12.1 Detection rule (from code)

```csharp
// In InventoriesController:
var lastSale = lastSaleDates.TryGetValue(i.ProductId, out var d) ? (DateTime?)d : null;
var refDate  = lastSale ?? (i.LastRestockDate ?? i.LastUpdated);
var idleDays = (today - refDate).Days;
// Dead if idleDays >= 90 AND CurrentStock > 0
```

```csharp
// In StoreComparisonController (per store, uses store+product pair):
var key  = (sid, i.ProductId);
DateTime ref_ = lastSaleMap.TryGetValue(key, out var ls) ? ls
              : i.LastRestockDate ?? i.LastUpdated;
return (today - ref_).Days >= 90;
```

To create dead stock: generate inventory items with `CurrentStock > 0` for which the most recent sale in the database is older than 90 days (or no sale exists at all).

### 12.2 Dead stock idle badge thresholds (Dead Stock table)

| `idleDays` | Badge class | Visual |
|---|---|---|
| 90–119 | `aging` | Yellow |
| 120–179 | `stale` | Orange |
| ≥ 180 | `dead` | Red |

Target spread: 40% aging, 35% stale, 25% dead across dead-stock items.

### 12.3 Dead stock capital insight trigger

The "Capital tied in dead stock" insight fires when `SUM(UnitPrice × CurrentStock)` across dead items > 0. For this insight to be impactful, target **€5,000–€25,000** in dead stock value per store.

---

## 13. Low Stock

### 13.1 Detection rules (from code)

- **OOS** (`CurrentStock == 0`): 5–8% of inventory items
- **Low stock** (`CurrentStock > 0 AND CurrentStock ≤ MinThreshold`): 10–15% of inventory items

### 13.2 Dashboard smart alert thresholds

In `DashboardController.LiveMetrics`:
```csharp
if (lowStockCount == 0)     → "Inventory Healthy" (success)
else if (lowStockCount <= 5) → "Stock Monitor" (warning)
else                         → "Inventory Risk" (danger)
```

`lowStockCount` = `COUNT(CurrentStock ≤ MinimumStockThreshold)` (includes OOS).

Target: `lowStockCount > 5` across the full dataset so the red "Inventory Risk" alert fires and the dashboard is visually complete.

---

## 14. Seasonality

### 14.1 `IsSeasonal` flag

Products with `IsSeasonal = true` should only appear in order items during their active season. In the generator, before assigning a product to an order item, check: `IsInSeason(product, order.orderDate)`.

### 14.2 Season windows

| `Season` value | Active months | Apply to |
|---|---|---|
| `SS24` | March 2024 – August 2024 | Historical |
| `AW24` | September 2024 – February 2025 | Historical |
| `SS25` | March 2025 – August 2025 | Historical |
| `AW25` | September 2025 – February 2026 | Recent |
| `SS26` | March 2026 – August 2026 | Current |
| `CORE` | All dates | Non-seasonal basics |

### 14.3 Seasonal uplift

Apply category-level seasonal multipliers to order generation:

| Category | Peak season | Off-season multiplier |
|---|---|---|
| Swimwear | June–August | 0.05 (nearly no sales) |
| Outerwear | October–February | 0.1 |
| Knitwear | September–February | 0.15 |
| Tops | March–August peak | 0.6 off-season |
| Dresses | April–August peak | 0.5 off-season |
| Accessories | All year | 1.0 (minor holiday lift) |
| Footwear | All year | 1.0 |
| Trousers | All year | 1.0 |

### 14.4 Inventory implications of seasonality

End-of-season dead stock should be present:
- SS25 Swimwear that wasn't cleared before September 2025 → dead stock candidate
- AW25 Outerwear that wasn't cleared before March 2026 → dead stock candidate

Generate these explicitly with `CurrentStock > 0` and last sale date in the previous season (90+ days ago).

---

## 15. Forecasting (Prediction table)

The `Prediction` model is stored in the database but not yet surfaced in any analytics page. The Forecasting page is a planned feature.

### 15.1 Schema

```
PredictionId (PK)
ProductId (FK → Products)
PredictionDate (datetime)
PredictedSales (decimal 18,2)
RecommendedStock (decimal 18,2)
ModelName (string?)
```

### 15.2 Generator requirements

The generator does **not** need to serve predictions via an API endpoint — there is no `/api/Predictions` endpoint in the sync service. Predictions could be pre-populated via the Import system or a separate seed endpoint.

For future compatibility, the generator should be capable of producing a `predictions` dataset with:
- One row per (ProductId, ForecastDate) combination
- `PredictedSales`: simple moving average of last 8 weeks' unit sales for the product
- `RecommendedStock`: `PredictedSales × (leadTimeDays / 7) + safetyStock`
- `ModelName`: e.g. `"SMA-8w"`

This data is not consumed by the current dashboard but should be generated to avoid a completely empty `Predictions` table.

---

## 16. Smart Insights — Trigger Conditions

Each page has server-side rules that generate insight cards. The data must be rich enough to make non-trivial insights fire.

### 16.1 Sales Analytics insights (6 cards)

| Insight | Trigger condition | Data requirement |
|---|---|---|
| Revenue growing/declining/stable | Second-half vs first-half revenue comparison (>5% = trend) | Year-on-year growth (+10–15%) will reliably trigger "growing" |
| Category drives X% of revenue | Top category revenue share | One category at 25–40% (not too dominant) |
| Top-5 SKU concentration | Top 5 products / total revenue | Keep below 60% (target 40–55%) |
| Discount efficiency | Full-price vs 21%+ discount margin gap | Gap of >12pp triggers warning |
| AOV trending up/down | First-third vs last-third AOV comparison | Slight YoY AOV growth (+5–8%) triggers "trending up" |
| Strongest channel | Always fires if any channel data exists | Any channel split works |

### 16.2 Inventory Intelligence insights (6 cards)

| Insight | Trigger condition | Data requirement |
|---|---|---|
| Out-of-stock alert | OOS count > 0 | Generate 5–8% OOS items |
| Capital in dead stock | Dead stock value > 0 | Generate 8–15% dead stock items |
| Low weeks of cover | WOC < 2 | One store in very low WOC state |
| Excess stock / Healthy coverage | WOC > 24 or 2–24 | Most stores: 4–16 weeks |
| Category concentration | One category > 40% of inventory value | Outerwear or Tops typically hits this |
| Low sell-through / Strong sell-through | < 20% or > 65% | Target 40–65% overall |
| Aging stock risk | `>90d` aging items > 0 | Generate 25% of items in 91–180d + >180d buckets |

### 16.3 Store Comparison insights (up to 6 cards)

| Insight | Trigger condition | Data requirement |
|---|---|---|
| Revenue leader | `active.Count >= 2` | At least 2 stores with sales data |
| Most profitable store | Margin comparison | 10 pp margin spread across stores |
| Highest basket value | Store with orders > 0 | AOV variation across stores |
| Best inventory turnover | Store with turnover > 0 | At least one store at 4× turnover |
| Highest inventory risk | `riskTotal > 0` | At least one store with OOS/low/dead stock |
| Stores differ in channel | `onlineDom > 0 AND physicalDom > 0` | Mix of online-dominant and physical-dominant stores |

---

## 17. Dashboard Live Refresh

The Dashboard calls `GET /Dashboard/LiveMetrics` every 15 seconds. This is an internal C# endpoint (not the generator API), but the data it returns depends on the database. The data generator does not need to push to this endpoint — it only needs to ensure the database is populated via the sync service.

---

## 18. Data Volume Summary

| Entity | Target total (all stores) |
|---|---|
| Stores | 4–6 |
| Products | 80–150 per store |
| Inventory rows | 1 per product per store (80–150 per store) |
| Orders | 5,000–15,000 per store over 2 years |
| Order line items | 8,000–30,000 per store (1.5–2.5 items/order avg) |
| Sale rows (after sync) | Same as order line items |
| Predictions | Optional; 1 per (product, week) for 12 future weeks |

---

## 19. Generator Architecture Recommendations

The Python generator should be a **Flask or FastAPI server** that:

1. Generates all data **once at startup** and holds it in memory (or a SQLite file)
2. Responds to the four API endpoints deterministically
3. Supports the `since` query parameter for delta order sync (return only orders with `OrderDate >= since`)
4. Supports the `page`/`pageSize` pagination for `/api/Orders`
5. Exposes a `/reset` or `/seed?seed=42` endpoint to regenerate with a new random seed

### 19.1 Generation order

Dependencies must be respected:

```
1. Generate stores
2. Generate products (require store IDs)
3. Generate inventory (require product + store IDs; set CurrentStock and LastRestockDate deliberately)
4. Generate orders (require product codes per store; apply seasonality, channel, discount rules)
5. (Optional) Generate predictions (require product IDs and order history)
```

### 19.2 Seed determinism

Use a fixed random seed (e.g. `42`) for reproducibility. Pass `?seed=N` to regenerate with a different seed without restarting the server.

### 19.3 Consistency requirements

- `StoreName` in orders **must exactly match** the `storeName` returned by `/api/Stores` (the sync does a string comparison)
- `ProductCode` in order items **must exactly match** `productCode` in `/api/Products` for the same store
- `productId` and `inventoryId` must be stable across API calls (same IDs every time for the same seed)

---

## 20. Validation Checklist

Before using the generator with the application, verify these conditions in the database after the first full sync:

**Dashboard**
- [ ] All 8 KPI cards show non-zero values
- [ ] Revenue Trend has at least 12 data points on "All Time"
- [ ] Sales Channel Distribution donut shows 3 slices
- [ ] Profitability by Category shows at least 5 categories
- [ ] Top Performing Products table shows 5 products
- [ ] Smart Alerts show a mix of success/warning/danger

**Sales Analytics**
- [ ] Revenue Over Time chart has a visible upward trend
- [ ] AOV chart shows variation (not a flat line)
- [ ] Discount Efficiency chart shows all 5 bands non-zero
- [ ] Colour Performance shows at least 6 colours
- [ ] Size Performance shows all 6 sizes non-zero
- [ ] Channel Split table shows 3 rows (Online, Mobile, Physical)

**Inventory Intelligence**
- [ ] Inventory Value KPI > €0
- [ ] Low Stock > 0, OOS > 0
- [ ] Weeks of Cover is between 2 and 24 (healthy)
- [ ] All 5 inventory aging bars non-zero
- [ ] All 4 reorder priority segments non-zero
- [ ] Category Value chart shows at least 5 bars
- [ ] Low Stock Alerts table has ≥ 1 row
- [ ] Dead Stock table has ≥ 1 row
- [ ] Smart Insights show at least one warning

**Store Comparison**
- [ ] All store cards render with non-zero revenue
- [ ] Revenue vs Profit Margin chart shows visible bar height and margin line variation
- [ ] Channel Mix stacked bars sum to ~100% per store
- [ ] Radar chart shows meaningfully different shapes per store
- [ ] Inventory Risk scorecard shows at least one store with non-zero counts
- [ ] Store Ranking table shows all stores with different revenues
- [ ] All 6 insight cards render (not just the "no data" fallback)
