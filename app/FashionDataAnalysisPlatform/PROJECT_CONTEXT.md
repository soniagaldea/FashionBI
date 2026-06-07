# FashionBI — Project Context

**Type:** ASP.NET Core 8 MVC web application (thesis project)
**Purpose:** Premium SaaS retail intelligence platform for multi-store fashion retailers
**Last updated:** 2026-06-07

---

## Architecture

### Stack
| Layer | Technology |
|---|---|
| Framework | ASP.NET Core 8 MVC |
| Database | SQL Server (LocalDB / full SQL Server) |
| ORM | Entity Framework Core 8 (`AsNoTracking` throughout) |
| Frontend | Razor Views, Bootstrap 5, CSS Custom Properties |
| Charts | Chart.js 4.4.0 (CDN) |
| Icons | Font Awesome 6.5.1 (CDN) |
| Typography | Inter (Google Fonts) |
| Background sync | `BackgroundService` (`StoreSyncBackgroundService`) |

### Data access pattern
All analytics controllers follow a strict three-step pattern:
1. One or more `AsNoTracking()` DB fetches projecting only required columns into anonymous types
2. All grouping, aggregation, and KPI calculation done **in-memory** after the fetch
3. Results serialised to JSON via `JsonSerializer.Serialize` and placed in `ViewBag`, then rendered with `@Html.Raw(...)` inside `<script>` blocks

This avoids EF Core translation errors for complex LINQ and keeps DB round-trips predictable.

### Analytics default
All analytics pages default to `dateRange = "all"` (All Time). Date filter options are identical across every page: Last 7 Days / Last 30 Days / Last 90 Days / Last 12 Months / All Time.

### Currency
All monetary values displayed in **Euro (€)** throughout the application.

---

## Project Structure

```
FashionDataAnalysisPlatform/
├── Controllers/
│   ├── DashboardController.cs          # Executive Dashboard + LiveMetrics endpoint
│   ├── SalesController.cs              # Sales Analytics + CRUD stubs
│   ├── InventoriesController.cs        # Inventory Intelligence + CRUD stubs
│   ├── StoreComparisonController.cs    # Store Comparison analytics
│   ├── StoreConnectionController.cs    # Store Connections (UI stub)
│   ├── ProductsController.cs           # Products CRUD
│   ├── ImportController.cs             # CSV/file import
│   └── HomeController.cs              # Home/privacy pages
├── Models/
│   ├── Product.cs
│   ├── Sale.cs
│   ├── Inventory.cs
│   ├── Store.cs
│   ├── StoreConnection.cs
│   └── Prediction.cs
├── Data/
│   └── AppDbContext.cs
├── Services/
│   └── StoreSyncBackgroundService.cs
├── Dtos/
│   ├── StoreApiDtos.cs
│   └── StoreOrderDto.cs
├── ViewModels/
│   └── DashboardViewModel.cs
├── Views/
│   ├── Shared/_Layout.cshtml           # App shell (sidebar, topbar, scope selector)
│   ├── Dashboard/Index.cshtml
│   ├── Sales/Index.cshtml
│   ├── Inventories/Index.cshtml
│   └── StoreComparison/Index.cshtml
└── wwwroot/
    └── css/site.css                    # All custom CSS (design system + component styles)
```

---

## Models

### Product
```
ProductId (PK), ProductCode [unique per StoreConnection+Store], ProductName,
Category, Color, Season, UnitPrice (decimal 18,2), Brand, Gender, Material,
BaseCost (decimal 18,2), IsSeasonal (bool), LaunchDate (DateTime?),
StoreId (FK), StoreConnectionId (FK), ExternalProductId (int?)
```
Navigation: `Store`, `StoreConnection`, `Sales[]`, `Inventories[]`, `Predictions[]`

### Sale
```
SaleId (PK), ProductId (FK), SaleDate, Quantity, UnitPrice (decimal 18,2),
Revenue (decimal 18,2), Size, Color, DiscountPercent (decimal?),
TotalCost (decimal?), Profit (decimal?), CustomerId (int?),
SalesChannel (string? — "Online" | "Mobile" | "Physical"),
StoreConnectionId (FK, SetNull on delete), ExternalOrderId (int?),
ExternalOrderItemId (int?), ExternalProductCode, StoreId (FK, NoAction)
```
Navigation: `Product`, `StoreConnection`, `Store`

### Inventory
```
InventoryId (PK), ProductId (FK), CurrentStock (int),
LastUpdated (DateTime), MinimumStockThreshold (int),
LastRestockDate (DateTime?), StoreId (FK), StoreConnectionId (FK),
ExternalInventoryId (int?)
```
Navigation: `Product`, `Store`, `StoreConnection`

### Store
```
StoreId (PK), StoreConnectionId (FK → Cascade delete),
ExternalStoreId (int), StoreName, City, Country, StoreType, Region
```
Navigation: `StoreConnection`

### StoreConnection
```
StoreConnectionId (PK), StoreName, StoreApiUrl, IsActive (bool),
LastSyncAt (DateTime?)
```
Represents a connected external retail API endpoint.

### Prediction *(defined, not yet surfaced in analytics UI)*
```
PredictionId (PK), ProductId (FK), PredictionDate, PredictedSales (decimal 18,2),
RecommendedStock (decimal 18,2), ModelName (string?)
```
Navigation: `Product`

### AppDbContext
`DbSet<>` for: `Products`, `Sales`, `Inventories`, `Predictions`, `StoreConnections`, `Stores`

Key constraints:
- `Product` has unique index on `(StoreConnectionId, StoreId, ProductCode)`
- `Sale → Product`: Cascade delete
- `Inventory → Product`: Cascade delete
- `Store → StoreConnection`: Cascade delete
- `Sale → StoreConnection`: SetNull on delete
- `Product/Inventory/Sale → Store`: NoAction

---

## Background Service — StoreSyncBackgroundService

Runs every **5 seconds**, polling all active `StoreConnection` records.

For each active connection it calls four external API endpoints in order:

| Endpoint | Action |
|---|---|
| `GET /api/Stores` | Upsert `Store` entities by `ExternalStoreId` |
| `GET /api/Products` | Upsert `Product` entities by `ExternalProductId` |
| `GET /api/Inventory` | Upsert `Inventory` entities by `ExternalInventoryId` |
| `GET /api/Orders?page=N&pageSize=500&since=<ISO>` | Insert new `Sale` rows (delta: `since` = `LastSyncAt`) |

Orders are paginated (500 per page). Sale deduplication is by `(StoreConnectionId, StoreId, ExternalOrderId, ExternalOrderItemId)`. After each full sync, `LastSyncAt` is updated on the connection.

---

## Implemented Pages

---

### 1. Executive Dashboard — `/Dashboard`

**Controller:** `DashboardController.Index(storeIds, dateRange)`
**Store scope:** Yes — global Store Scope selector in topbar filters all queries
**Live refresh:** Yes — `GET /Dashboard/LiveMetrics` called every **15 seconds** via `fetch()`, updates DOM in place without page reload

#### KPIs (8 cards)
| KPI | Calculation |
|---|---|
| Total Revenue | `SUM(Sale.Revenue)` |
| Total Orders | `COUNT(DISTINCT ExternalOrderId)` |
| Units Sold | `SUM(Sale.Quantity)` |
| Avg Order Value | `Revenue / Orders` |
| Gross Profit | `SUM(Sale.Profit)` |
| Profit Margin | `Profit / Revenue × 100` |
| Top Category | Highest-revenue `Product.Category` |
| Low Stock Alerts | `COUNT(Inventory WHERE CurrentStock ≤ MinimumStockThreshold)` |

#### Charts
| Chart | Type | Data |
|---|---|---|
| Revenue Trend | Area line (dual series) | Revenue + Profit; daily/weekly/monthly granularity adapts to date range |
| Sales Channel Distribution | Doughnut | Revenue grouped by `SalesChannel`; custom legend with % |
| Profitability by Category | Grouped bar | Revenue vs Profit per category (top 8) |
| Top Performing Products | HTML table | Top 5 products by revenue (name, category, revenue, units, margin%) |
| Executive Summary | HTML key-value list | All 8 KPIs in text form, live-updated |
| Smart Alerts | HTML alert cards | Auto-generated: stock risk, margin status, category leader, sync status |

---

### 2. Sales Analytics — `/Sales`

**Controller:** `SalesController.Index(storeIds, dateRange)`
**Store scope:** Yes — global Store Scope selector in topbar
**Pattern:** Single `ToListAsync()` of raw sales → all analytics in-memory; separate `ToDictionaryAsync` for product name lookup

#### KPIs (6 cards)
Total Revenue, Total Orders, Units Sold, Avg Order Value, Gross Profit, Profit Margin

#### Charts
| Chart | Type | Data |
|---|---|---|
| Revenue + AOV Trend | Dual line (combo) | Revenue (left axis) + AOV (right axis); daily/weekly/monthly |
| Discount Efficiency | Grouped bar | Revenue + Margin% per discount band: 0%, 1–10%, 11–20%, 21–30%, 30%+ |
| Color Performance | Vertical bar | Revenue by colour (top 6); separate units bar |
| Size Distribution | Vertical bar | Units sold per standard size: XS S M L XL XXL |
| Channel Split | HTML table | Online/Mobile/Physical — revenue, share%, AOV |
| Top Products | HTML table | Top 5 by revenue (name, category, revenue, units, margin%) |
| Smart Insights | HTML alert grid | 6 rule-based insights: trend direction, top category, SKU concentration, discount efficiency, AOV trend, strongest channel |

---

### 3. Inventory Intelligence — `/Inventories`

**Controller:** `InventoriesController.Index(storeIds, dateRange)`
**Store scope:** Yes — global Store Scope selector in topbar
**Pattern:** Raw inventory fetch → batch product lookup → sales fetch for velocity KPIs → last-sale dates for dead stock detection — all in-memory

#### KPIs (6 cards)
| KPI | Calculation |
|---|---|
| Total SKUs | `COUNT(Inventory rows)` |
| Units on Hand | `SUM(CurrentStock)` |
| Inventory Value | `SUM(UnitPrice × CurrentStock)` at retail price |
| Low Stock | `COUNT(CurrentStock > 0 AND CurrentStock ≤ MinThreshold)` |
| Out of Stock | `COUNT(CurrentStock == 0)` |
| Weeks of Cover | `UnitsOnHand / AvgWeeklySales`; "all" range uses 365-day annualised rate |

#### Stock Health panel (4 metrics)
| Metric | Calculation |
|---|---|
| Sell-through Rate | `UnitsSold / (UnitsSold + UnitsOnHand) × 100` |
| Stock-to-Sales Ratio | `UnitsOnHand / PeriodUnitsSold` |
| Inventory Turnover | `UnitsSold / (refDays/365) / UnitsOnHand` (annualised) |
| Overstocked SKUs | `CurrentStock > AvgWeeklySales × 8` |

#### Charts & Tables
| Component | Type | Data |
|---|---|---|
| Inventory Aging | Vertical bar | SKU count per aging bucket: <30d, 30–60d, 61–90d, 91–180d, >180d (based on `LastRestockDate`) |
| Reorder Priority | Doughnut | Critical (OOS) / High (below threshold) / Medium (≤2× threshold) / Low (>2× threshold) |
| Inventory Value by Category | Horizontal bar | Retail value of on-hand stock per category (top 8) |
| Low Stock Alerts | HTML table | Up to 10 SKUs at/below threshold: stock, min, gap bar |
| Dead Stock | HTML table | Up to 10 SKUs with no sales ≥90 days: idle days badge, retail value |
| Smart Insights | HTML alert grid | 6 rule-based insights: OOS alert, dead stock capital, weeks of cover, category concentration, sell-through, aging risk |

**Dead stock detection:** `MAX(SaleDate)` per product fetched from DB; fallback to `LastRestockDate ?? LastUpdated` if never sold. Item flagged dead if idle ≥ 90 days and `CurrentStock > 0`.

---

### 4. Store Comparison — `/StoreComparison`

**Controller:** `StoreComparisonController.Index(dateRange)`
**Store scope:** No — global Store Scope selector is **hidden** on this page
**Primary filter:** "Compare Stores" pill selector (client-side JavaScript, no page reload)
**Pattern:** 5 DB fetches (stores, sales, inventory, product prices, last-sale dates) → all analytics computed per-store in-memory via LINQ lambdas

#### Per-store metrics computed
Revenue, Profit, Orders, Units, AOV, Profit Margin%, Online/Mobile/Physical channel split %, Inventory Value, Inventory Turnover, Low Stock count, Out-of-Stock count, Dead Stock count

#### Layout (top to bottom)
1. **Page header** — date range dropdown + Export button
2. **Compare Stores selector bar** — pill toggle buttons, one per store, colour-coded; minimum 2 stores enforced
3. **Store Summary Cards** — hero section, one card per selected store:
   - Colour-coded top border + rank badge
   - Revenue (26px/800 weight) with relative bar
   - 2×2 KPI grid: Orders, AOV, Margin (colour-coded), Turnover
   - Stock health footer: Healthy / Low / OOS tags
4. **Revenue vs Profit Margin** — full-width combo chart:
   - Bar series: Revenue per store (per-store colours, left axis)
   - Line series: Profit Margin % (green, right axis; point colour = margin level)
5. **Sales Channel Mix** — stacked bar (col-7): Online/Mobile/Physical % per store
6. **Performance Profile** — radar chart (col-5): normalised 0–100 across Revenue, Orders, AOV, Margin, Turnover
7. **Inventory Value** — horizontal bar chart (col-7): retail value per store
8. **Inventory Risk scorecard** — HTML panel (col-5): sorted by risk score (`OOS×3 + LowStock×2 + DeadStock`); per store: turnover badge (ok/warn/bad) + count badges for Low/Dead/OOS
9. **Store Ranking** — HTML table sorted by revenue: rank badge (gold/silver/bronze), store colour dot, all per-store KPIs
10. **Smart Store Insights** — grid of up to 6 rule-based insight cards generated server-side from cross-store data

#### Server-side insights generated
- Revenue leader + gap % to lowest
- Most profitable store (margin comparison)
- Highest AOV store (upsell opportunity)
- Best inventory turnover (benchmark)
- Highest inventory risk (OOS + low + dead stock total)
- Channel dominance difference (online-led vs physical-led stores)

---

## Shared Layout (`Views/Shared/_Layout.cshtml`)

### Sidebar navigation
| Link | Controller | Status |
|---|---|---|
| Executive Dashboard | Dashboard | ✅ Implemented |
| Sales Analytics | Sales | ✅ Implemented |
| Inventory Intelligence | Inventories | ✅ Implemented |
| Store Comparison | StoreComparison | ✅ Implemented |
| Forecasting | — | ⚠️ Stub (`href="#"`) |
| Smart Insights | — | ⚠️ Stub (`href="#"`) |
| Sustainability | — | ⚠️ Stub (`href="#"`) |

### Global Store Scope selector (topbar)
- Checkbox list of all stores; submits as `?storeIds=1&storeIds=2&dateRange=all`
- Routes to the current page's controller (Dashboard / Sales / Inventories / StoreComparison)
- **Hidden on StoreComparison** (`display:none`) — that page uses its own client-side store filter
- `scopeFormController` switch in `_Layout.cshtml` controls routing per-controller

### Topbar placeholders (not implemented)
- Global search box (`⌘K`)
- Notification bell
- User account settings

### Layout JavaScript
- Sidebar collapse/expand with `localStorage` persistence (`bi-sidebar-collapsed`)
- Mobile slide-in sidebar with backdrop
- Store scope dropdown toggle
- User workspace popover (profile menu)
- storeIds propagation to sidebar nav links via `URLSearchParams`

---

## CSS Design System (`wwwroot/css/site.css`)

### Custom Properties (`:root`)
```css
--primary        /* indigo #5b5fc7 */
--success        /* emerald #10b981 */
--warning        /* amber #f59e0b */
--danger         /* red #ef4444 */
--border         /* #e8ecf2 */
--muted          /* #94a3b8 */
--text           /* #0f172a */
--bg             /* #f1f5f9 */
--card           /* #ffffff */
--shadow-sm/md/lg
--radius-sm/md/lg/xl
--success-soft / --warning-soft / --danger-soft   /* tinted backgrounds */
```

### Component families
| Prefix | Components |
|---|---|
| `kpi-*` | `kpi-grid`, `kpi-card`, `kpi-value`, `kpi-label`, `kpi-icon`, `kpi-pill`, `kpi-top`, `kpi-footer` |
| `dash-*` | `dash-header`, `dash-title`, `dash-subtitle`, `dash-actions` |
| `card` | Bootstrap card extended: `card-header`, `chart-card-title`, `chart-card-subtitle` |
| `sidebar-*` | Full sidebar layout, collapse, mobile open state |
| `topbar` | Topbar layout, search, scope selector |
| `alert-item` | Alert/insight cards: `success`, `warning`, `danger`, `info` |
| `top-products-*` | Shared product table styles |
| `sc-*` | All Store Comparison specific: cards, pills, risk scorecard, ranking, insights grid |
| `donut-stat-*` | Donut chart legend rows (used in Inventory Intelligence) |
| `date-range-btn` | Date range dropdown button |
| `export-btn` | Export action button |
| `live-badge` | Dashboard live refresh indicator |

### KPI value sizes
- Default: 28px / 700 weight
- `.smaller` class: 18px (used when value is text, e.g. "High cover", Top Category)
- Store Comparison revenue: 26px / 800 weight (`.sc-card-rev-value`)

---

## Routing

Default MVC route: `{controller=Home}/{action=Index}/{id?}`

Startup default page is `HomeController` (redirects to Dashboard in practice via nav). All primary analytics pages are GET-only with query string parameters.

```
GET /Dashboard?storeIds=1&storeIds=2&dateRange=30d
GET /Dashboard/LiveMetrics?storeIds=1&dateRange=30d   (JSON endpoint)
GET /Sales?storeIds=1&dateRange=90d
GET /Inventories?storeIds=1&dateRange=all
GET /StoreComparison?dateRange=12m
GET /StoreConnection
```

---

## Pending / Not Yet Implemented

### Pages (sidebar stubs)
| Page | Notes |
|---|---|
| Forecasting | Sidebar link exists. `Prediction` model and `Predictions` DbSet are in place. No controller or view. Intended to surface `PredictedSales` / `RecommendedStock` data. |
| Smart Insights | Sidebar link exists. No controller or view. Could aggregate cross-page insights. |
| Sustainability | Sidebar link exists. No controller or view. |

### Features on existing pages
| Feature | Location | Notes |
|---|---|---|
| Export button | All analytics pages | Placeholder `<a href="#">` — no download logic |
| Global search | Topbar | Placeholder `<input>` — no search logic |
| Notification bell | Topbar | Placeholder button — no notification system |
| Account Settings | User popover | `href="#"` stub |
| Logout | User popover | `href="#"` stub |
| Store Connections page | `/StoreConnection` | View exists (`Views/StoreConnections/Index.cshtml`) but controller returns an empty view with no data |

### Import system
`ImportController` and `Views/Import/Index.cshtml` exist with associated import models (`ProductImportModel`, `InventoryImportModel`, `SaleImportModel`). Import flow is implemented but not linked from the main navigation.

### Prediction model
The `Prediction` entity (PredictionId, ProductId, PredictionDate, PredictedSales, RecommendedStock, ModelName) is fully migrated and in the DbContext but nothing reads or writes to it through the analytics UI. The Forecasting page would consume it.

---

## Hard Constraints (thesis rules — do not violate)

- **Do not modify:** Models, DbContext, migrations, services, background sync logic
- **Do not use:** React, Tailwind, any frontend framework beyond Bootstrap 5 + vanilla JS
- **No fake data:** All metrics and charts must come from real database queries
- **No mock analytics:** No hardcoded values, simulated forecasts, or placeholder chart data
- **No navigation properties in analytics queries:** Use `.Select(s => new { ... })` projections and in-memory joins, not `.Include()` (except Dashboard which uses Include for EF Core DB-side grouping)
