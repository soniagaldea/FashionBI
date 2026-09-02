# PROJECT_CONTEXT.md
## FashionBI — Fashion Retail Data Analysis Platform
### Primary Knowledge Source for Bachelor Thesis Documentation

> **Purpose of this document:** This file is the single authoritative reference for writing the bachelor thesis. It is derived directly from the source code, not from notes or memory. Every claim made here corresponds to implemented, running code in the repository as of the final submission on 2026-06-17.

---

## 1. Project Identity

### Project Name
**FashionBI — Fashion Retail Data Analysis Platform**

### Thesis Title
*Design and Implementation of a Fashion Retail Business Intelligence Platform with Machine Learning Demand Forecasting*

### Project Purpose
FashionBI is an end-to-end business intelligence platform purpose-built for multi-store fashion retail. It ingests transactional data from a connected retail API, stores and normalises that data in a local analytics database, and surfaces it through a suite of analytical dashboards covering executive KPIs, sales analytics, inventory management, store benchmarking, machine learning demand forecasting, prescriptive smart insights, and sustainability intelligence. The system demonstrates a complete analytics pipeline from raw sales transactions to actionable prescriptive recommendations.

### Business Problem
Fashion retail is characterised by short seasonal cycles, high product variety (SKU proliferation), unpredictable consumer demand, and significant inventory risk. Retailers operating multiple store formats simultaneously face the challenge of making coherent purchasing, pricing, and markdown decisions without a unified view of performance across stores, channels, and product categories. Excess inventory results in costly markdowns or dead stock; insufficient inventory results in stockouts and lost revenue. Neither problem is easily visible without purpose-built analytics tooling. Traditional spreadsheet-based approaches are too slow and do not scale to multi-store, multi-brand operations. FashionBI addresses this by providing a real-time, continuously synchronised analytics platform that integrates descriptive, diagnostic, predictive, and prescriptive analytics into a single interface.

### Target Users
The platform is designed for:
- **Retail operations managers** who need a high-level view of network performance.
- **Merchandising and buying teams** who make stock purchasing and allocation decisions.
- **Store managers** who need to understand their store's position relative to the network.
- **Finance managers** who track revenue, gross profit, and margin health.

In the thesis context, the system is operated by a single demo user (the researcher) who represents a retail analyst role with full platform access.

### Main Objectives
1. Design and implement a pull-based data synchronisation pipeline between a retail transactional API and an analytics database.
2. Build a layered analytics dashboard covering all four levels of the analytics maturity model: Descriptive → Diagnostic → Predictive → Prescriptive.
3. Implement a machine learning demand forecasting module using Random Forest regression with multi-model comparison (Naive Baseline, Holt-Winters, Random Forest) and quantified accuracy metrics.
4. Implement an ABC/XYZ portfolio classification, Statistical Process Control anomaly detection, and a composite Business Health Score within a Smart Insights prescriptive module.
5. Implement a sustainability intelligence module that quantifies inventory waste risk using sell-through rate, markdown dependency, and dead stock detection.
6. Demonstrate industry-standard software patterns: MVC architecture, dependency injection, background services, cookie-based authentication, and CSRF protection.
7. Simulate 24 months of realistic historical fashion retail data (July 2024 → June 2026) using a Python order generator with seasonal weighting, store volume profiles, and basket affinity logic.

---

## 2. Business Context

### Fashion Retail Challenges
Fashion retail is one of the most analytically complex retail sectors due to:
- **Seasonal collections:** Products have defined lifecycle windows (Autumn/Winter, Spring/Summer) after which their commercial value collapses. Unsold stock cannot be sold at full price after the season ends.
- **SKU proliferation:** Each product exists across multiple sizes, colours, and in multiple stores, creating hundreds of inventory positions that must be monitored simultaneously.
- **Trend sensitivity:** Consumer demand for fashion items can shift rapidly in response to social media trends, weather events, and competitor activity.
- **Multi-channel distribution:** Revenue flows from physical stores, online platforms, and mobile channels, each with different average order values and margin profiles.
- **Lead times:** Purchasing decisions must be made months in advance of the selling season, making accurate forecasting critical.

### Inventory Management Problems
- **Stockouts:** When a product sells out before the season ends, every subsequent day represents lost revenue at full margin. Stockouts are especially damaging for high-velocity SKUs.
- **Overstock:** When more units are purchased than can be sold at full price, the excess must be cleared via markdowns, reducing margin or triggering a loss.
- **Dead stock:** Products that stop selling while still holding inventory tie up working capital and floor space. After a threshold period (defined as 45–90 days in this system), they require aggressive intervention.
- **Inventory aging:** As stock ages on the floor, its perceived value decreases, particularly for seasonal items as the season deadline approaches.
- **Safety stock:** Each SKU has a minimum threshold below which replenishment should be triggered. Managing these thresholds across hundreds of SKUs is operationally complex.

### Forecasting Challenges
- **Short history:** New seasonal products have limited sales history, making statistical forecasting difficult.
- **Non-stationarity:** Fashion demand is non-stationary; patterns change across seasons and years.
- **Multi-dimensional forecasting:** Demand must be forecasted not just at the total level but at the Store × Category × Month granularity to be actionable for purchasing decisions.
- **Model selection:** Different forecasting methods (naive persistence, exponential smoothing, machine learning) perform differently depending on the data characteristics.

### Sustainability Concerns
Fashion retail is under increasing pressure to reduce inventory waste. Unsold seasonal inventory represents both a financial cost (cost of goods) and an environmental cost (production without consumption). Key sustainability metrics in fashion retail include:
- **Sell-Through Rate (STR):** What percentage of ordered stock was actually sold?
- **Markdown dependency:** To what degree must the retailer rely on discounts to clear inventory?
- **Dead stock:** Which products represent permanent inventory waste?
- **Carbon cost proxy:** While FashionBI does not directly compute carbon emissions, it uses inventory waste (unsold goods at cost price) as a financial proxy for environmental waste.

### Business Intelligence Value
Business intelligence in retail creates value by converting raw transactional data into decision-relevant information at three levels:
- **Descriptive (what happened):** Revenue, profit, units sold, channel split — aggregated and visualised.
- **Diagnostic (why it happened):** Category performance, store comparison, inventory health diagnostics.
- **Predictive (what will happen):** Machine learning demand forecasting with 3-month horizon.
- **Prescriptive (what should be done):** Specific, quantified action recommendations with estimated financial impact.

FashionBI is explicitly designed to cover all four levels, advancing from traditional descriptive dashboards to full prescriptive analytics. This progression maps directly to the Gartner Analytics Maturity Model (Descriptive → Diagnostic → Predictive → Prescriptive).

---

## 3. Functional Requirements

The following functional requirements were implemented in the final submission:

**FR-01:** The system shall synchronise data from the FashionStoreAPI every 5 seconds via a background service, using incremental delta sync based on `LastSyncAt` timestamps.

**FR-02:** The system shall support multiple store connections, each independently configurable with a base API URL, active/inactive status, and last-sync timestamp.

**FR-03:** The system shall authenticate users via cookie-based session authentication. Only authenticated users shall access analytics pages. A demo login button shall allow thesis evaluation without credential entry.

**FR-04:** The Executive Dashboard shall display total revenue, gross profit, order count, average order value, profit margin, top category, and low-stock count. All KPIs shall include prior-period trend indicators. The revenue trend chart shall adapt granularity (daily/weekly/monthly) based on the selected date range. The dashboard shall refresh via a polling LiveMetrics endpoint every 15 seconds without full page reload.

**FR-05:** The Sales Analytics module shall display revenue trend, AOV trend, category performance, channel split, top 5 products by revenue, colour performance, size distribution, and discount band efficiency. All charts shall be filterable by store and date range.

**FR-06:** The Inventory Intelligence module shall display total SKU count, units on hand, inventory value, low-stock count, out-of-stock count, weeks of cover, sell-through rate, stock-to-sales ratio, inventory turnover, and overstock count. The module shall include a low-stock alert table, dead stock table (90+ days no sales), inventory aging histogram, and reorder priority classification.

**FR-07:** The Store Comparison module shall provide side-by-side metrics for all active stores including revenue, profit, orders, units, AOV, margin, channel split (Online/MobileApp/Physical), inventory value, inventory turnover, low-stock count, OOS count, and dead stock count. Automatically generated cross-store insights shall be displayed.

**FR-08:** The Forecasting module shall generate 3-month demand forecasts at the Store × Category level. The system shall compare three models (Naive Baseline, Holt-Winters, Random Forest) on a held-out test set and select the best by Revenue WMAPE. Forecasts shall be triggered manually by the user. The module shall display actual vs forecast chart with confidence bands, forecast accuracy metrics (MAE, RMSE, WMAPE, Accuracy%), feature importance chart, inventory risk alerts, year-over-year comparison, and a Strategic Outlook section.

**FR-09:** The Smart Insights module shall compute a Business Health Score (0–100) composed of four equally-weighted components. It shall generate a Strategic Action Board with actions classified as Urgent, Opportunity, Optimize, or Monitor, each with title, explanation, suggested action, and estimated financial impact. It shall detect KPI anomalies using Statistical Process Control (|σ| ≥ 1.5 threshold). It shall compute an ABC/XYZ product portfolio matrix with drill-down capability.

**FR-10:** The Sustainability module shall compute sell-through rate, at-risk inventory value, dead stock count, markdown dependency rate, and seasonal overstock value. It shall display a Collection Efficiency Timeline, Waste Risk Bubble Chart, Sell-Through by Category, Markdown Dependency Analysis, an At-Risk SKU table, and three tabs of sustainability recommendations (Act Now, Monitor, Plan Better).

**FR-11:** The Notification system shall generate live platform notifications from inventory, smart insights, forecasting, and sustainability data without a dedicated notifications table. Notifications shall be classified as Critical, Warning, Info, or Success and sorted by severity.

**FR-12:** The Global Search shall support autocomplete search across pages, stores, categories, products, and SKU codes, returning up to 8 results, each with a navigation URL.

**FR-13:** The Store Connections management page shall allow users to view all connections, test connectivity, trigger an immediate sync, and toggle active/inactive status.

**FR-14:** The system shall use CSRF protection (anti-forgery tokens) on all state-mutating POST actions.

**FR-15:** The data generator (`fashion_order_generator.py`) shall simulate 24 months of historical orders (July 2024 → present) in historical mode and continuous live orders (one order every 10 seconds) in live mode, using seasonal weights, store-specific volume profiles, and basket affinity logic.

---

## 4. Non-Functional Requirements

### Performance
- All dashboard pages shall load in under 3 seconds for datasets of up to 24 months of simulated order history.
- The background sync service shall complete each sync cycle within the 5-second polling interval for datasets of up to 10,000 sales records.
- The ML forecasting script shall complete within the 5-minute timeout configured in `ForecastingService`.
- Sales data fetch in analytics controllers uses `AsNoTracking()` throughout to avoid EF Core change-tracking overhead on read-only queries.
- The `StoreSyncBackgroundService` performs batch saves every 500 records to keep transactions small and avoid memory pressure.
- In-memory aggregation is preferred over complex SQL aggregations for analytics calculations, reducing the surface area of N+1 query risks.

### Security
- All analytics controllers are decorated with `[Authorize]`; unauthenticated requests are redirected to `/Account/Login`.
- Authentication uses ASP.NET Core cookie authentication with an 8-hour sliding session expiry, `HttpOnly` flag, and `SameAsRequest` secure policy.
- Login credentials are stored in `appsettings.json` under the `DemoUser` key, which is appropriate for a single-user thesis demonstration context.
- All POST endpoints use `[ValidateAntiForgeryToken]` (or `[IgnoreAntiforgeryToken]` explicitly for AJAX endpoints that use JSON responses without form submission).
- The application uses HTTPS redirection in all environments.
- SQL queries are parameterised throughout EF Core — no raw string concatenation in queries.

### Scalability
- The analytics platform uses a pull-based sync pattern; the FashionStoreAPI is not modified to push events. Adding more analytics consumers would not affect the transactional API.
- The `StoreConnections` table supports multiple independent API connections, allowing the platform to be extended to sync from multiple independent store APIs.
- The `StoreSyncBackgroundService` iterates over all active connections, so new connections are picked up automatically at the next sync cycle without service restart.
- The ML script operates over a 24-month window by default. Extending it to a longer window requires only a SQL query parameter change.

### Usability
- All analytics pages include a global store filter and date range selector (7d, 30d, 90d, 12m, all-time) that persist across navigation via form submission.
- The global search provides an autocomplete dropdown accessible from every page via the top navigation bar.
- All charts use Chart.js with consistent colour coding across the platform.
- The platform uses Bootstrap 5 for responsive layout. The sidebar collapses on mobile devices.
- Business Health Score and KPI exception panels use colour-coded severity indicators (emerald/amber/red) for immediate visual interpretation.
- A demo login button on the login page allows thesis evaluators to access the platform without credentials.
- The notification bell in the top navigation shows an unread count badge and displays a dropdown panel of live alerts sorted by severity.

### Maintainability
- Services are registered via ASP.NET Core dependency injection (`builder.Services.AddScoped<T>()`).
- Business logic for complex analytics modules (SmartInsights, Sustainability, Forecasting, Notifications) is encapsulated in dedicated service classes, isolating it from controller and view code.
- Simple controllers (Dashboard, Sales, Inventory, StoreComparison) contain their own query and aggregation logic directly, because the query complexity does not justify a separate service class.
- The ML forecasting module is entirely in Python (`ml/fashion_forecaster.py`) and is invoked as a subprocess by `ForecastingService`. This separation means the ML code can be updated or replaced independently of the .NET application.
- Forecast database tables are created via both EF Core `EnsureCreated()` (for fresh databases) and idempotent `IF NOT EXISTS CREATE TABLE` DDL statements in `Program.cs` (for databases created before the forecasting module was added). This allows backwards-compatible schema evolution without EF migrations.

---

## 5. Final System Architecture

### Overview

The system consists of three independent components that communicate via HTTP:

```
[fashion_order_generator.py]
          │  POST /api/Orders
          ▼
[FashionStoreAPI]  (.NET 8 Web API)
   • StoresController
   • ProductsController
   • InventoryController
   • OrdersController
   • FashionStoreApiDb (SQL Server LocalDB)
          │  GET /api/Stores, /api/Products, /api/Inventory, /api/Orders?since=&page=
          ▼
[StoreSyncBackgroundService]  (IHostedService, 5-second poll)
          │
          ▼
[FashionDataAnalysisPlatform]  (.NET 8 MVC Web App)
   ┌─────────────────────────────────────────┐
   │  Controllers                            │
   │  ├── AccountController (auth)           │
   │  ├── DashboardController                │
   │  ├── SalesController                    │
   │  ├── InventoriesController              │
   │  ├── StoreComparisonController          │
   │  ├── ForecastingController              │
   │  ├── SmartInsightsController            │
   │  ├── SustainabilityController           │
   │  ├── StoreConnectionController          │
   │  ├── NotificationController             │
   │  └── SearchController                   │
   ├─────────────────────────────────────────┤
   │  Services (complex business logic only) │
   │  ├── StoreSyncBackgroundService         │
   │  ├── ForecastingService                 │
   │  ├── SmartInsightsService               │
   │  ├── SustainabilityService              │
   │  └── NotificationService               │
   ├─────────────────────────────────────────┤
   │  Data Access (EF Core)                  │
   │  └── AppDbContext                       │
   ├─────────────────────────────────────────┤
   │  FashionRetailDb (SQL Server LocalDB)   │
   │  ├── StoreConnections                   │
   │  ├── Stores                             │
   │  ├── Products                           │
   │  ├── Sales                              │
   │  ├── Inventories                        │
   │  ├── ForecastResults                    │
   │  ├── ForecastAccuracies                 │
   │  └── ForecastFeatureImportances         │
   └─────────────────────────────────────────┘
          │  pyodbc / ODBC Driver 17
          ▼
[ml/fashion_forecaster.py]  (Python 3, manual trigger via subprocess)
```

### Layer-by-Layer Explanation

#### FashionStoreAPI (Transactional Backend)
The FashionStoreAPI is a minimal ASP.NET Core Web API that owns the transactional OLTP database (`FashionStoreApiDb`). Its responsibilities are:
- Providing REST endpoints for Stores, Products, Inventory, and Orders.
- Validating and persisting orders submitted by the data generator.
- Calculating profit per order item (`LineTotal - TotalCost = Profit`) and decrementing inventory on order creation.
- Triggering automatic inventory restocking when stock falls to the minimum threshold.
- Seeding the database with 3 stores and 100 product records on first startup.
- Exposing a Swagger UI (`/swagger`) for API exploration.

The API does not implement authentication because it operates on localhost and is considered an internal service. The API schema is managed through EF Core migrations applied via `dotnet ef database update` — two migration files are present (`20260429124414_InitialCreate`, `20260519172005_AddFashionAnalyticsFields`). `Program.cs` does not call `EnsureCreated()` or `Migrate()` explicitly; the database must have the migrations applied before first run.

#### fashion_order_generator.py (Data Generator)
The Python generator simulates realistic fashion retail demand. It operates in two modes:
- **Historical mode:** Generates 24 months of backdated orders starting from July 2024 through the current date (`HISTORICAL_START = datetime(2024, 7, 1)`, `end_date = datetime.now()`), creating the dataset required for ML forecasting. Orders are distributed across seasons, stores, and categories with realistic weighting.
- **Live mode:** Continuously generates one order every 10 seconds, simulating real-time retail activity for demo purposes.

The generator respects seasonal product lifecycles: it only orders products whose `LaunchDate` has been reached and whose `SEASON_END_DATE` has not passed. It uses store-specific volume profiles (Maison Toulouse has lower volume but higher AOV than Scuffer) and simulates basket affinity (e.g., a customer buying outerwear is more likely to also buy knitwear).

#### StoreSyncBackgroundService (Data Integration)
This is an ASP.NET Core `BackgroundService` (`IHostedService`) that runs in the background for the entire lifetime of the analytics application. It polls every 5 seconds and, for each active `StoreConnection` record, executes four synchronisation operations in sequence:

1. **SyncStoreEntitiesAsync:** Fetches `GET /api/Stores` and upserts local `Store` records, matching on `ExternalStoreId`.
2. **SyncProductsAsync:** Fetches `GET /api/Products` and upserts local `Product` records, matching on `(StoreConnectionId, StoreId, ExternalProductId)`.
3. **SyncInventoryAsync:** Fetches `GET /api/Inventory` and upserts local `Inventory` records, matching on `ExternalInventoryId`.
4. **SyncOrdersAsync:** Fetches `GET /api/Orders?since={LastSyncAt}&page={N}&pageSize=500` with pagination, converts each `OrderItem` to a `Sale` row, and performs deduplication on `(StoreConnectionId, ExternalOrderId, ExternalOrderItemId)`.

The `LastSyncAt` cursor is only advanced when orders were successfully processed or when no new orders were received. If products are missing (i.e., products have not synced yet), the cursor is held back to retry on the next cycle. The service gracefully handles API unavailability (returns warning and continues) and honours application shutdown cancellation tokens.

#### Business Services Layer
Four service classes encapsulate complex business logic that would be too long to embed in controllers:

- **ForecastingService:** Invokes the Python ML script as a subprocess with a 5-minute timeout, and builds the `ForecastingViewModel` by querying `ForecastResults`, `ForecastAccuracies`, and `ForecastFeatureImportances` tables.
- **SmartInsightsService:** Performs ABC/XYZ classification, SPC anomaly detection, Business Health Score computation, and Strategic Action Board generation entirely in-memory after loading raw data from the database.
- **SustainabilityService:** Computes per-SKU waste risk scores and all sustainability KPIs from `Sales`, `Products`, `Inventories`, and `Stores` tables.
- **NotificationService:** Generates live platform notifications from inventory, trend, forecasting, and sustainability signals without a dedicated notification storage table.

Simple controllers (Dashboard, Sales, Inventory, StoreComparison) contain their own EF Core queries and aggregations directly.

#### EF Core Data Access Layer
Entity Framework Core 8 is used as the ORM. The `AppDbContext` class defines `DbSet<T>` properties for every table in `FashionRetailDb`. All read-heavy analytics queries use `.AsNoTracking()` to avoid the overhead of EF Core's change-tracking. The schema is created via `EnsureCreated()` on startup; the three forecast tables are additionally created via idempotent raw DDL statements to support backwards-compatible deployment on existing databases.

#### FashionRetailDb (Analytics Database)
The analytics database is a SQL Server LocalDB instance that acts as a materialised view of the transactional API data. It is strictly read-only from the API's perspective — all writes come from the sync service or from the ML script. The database persists the sync cursor (`LastSyncAt` in `StoreConnections`), all dimensional data (stores, products), and all fact data (sales, inventory snapshots). The three forecast tables (`ForecastResults`, `ForecastAccuracies`, `ForecastFeatureImportances`) are written exclusively by the Python ML script via pyodbc.

#### Views (Razor/Bootstrap/Chart.js)
Each module has a dedicated Razor `.cshtml` view that renders data passed via `ViewBag` or strongly-typed `ViewModel` objects. Charts are rendered by Chart.js, with data serialised as JSON in the controller and injected into JavaScript via Razor. Bootstrap 5 provides the grid system, card components, badge styling, and tab controls. Font Awesome 6 icons are used throughout the sidebar, KPI cards, and action items. All multi-step forms use Bootstrap Tabs for progressive disclosure.

---

## 6. Technology Stack

### ASP.NET Core MVC (.NET 8)
**Selection rationale:** ASP.NET Core MVC is the primary web framework for both the analytics platform and the store API. It was selected because it is the industry-standard framework for building .NET web applications, offering built-in dependency injection, cookie authentication middleware, routing, model binding, Razor view engine, and background service hosting. .NET 8 is the current LTS release, providing long-term support and the latest performance improvements. The MVC pattern cleanly separates the data access, business logic, and presentation concerns, which is architecturally appropriate for an analytics application with complex view state.

### C# (Language)
**Selection rationale:** C# is the primary implementation language. It provides strong static typing, LINQ for expressive in-memory data transformation, async/await for non-blocking I/O operations (essential for the background sync service and all controller actions), pattern matching, and records (used extensively in service classes as projection types). These language features make it well-suited to complex analytics logic that combines database queries, in-memory aggregation, and business rule application.

### Entity Framework Core 8
**Selection rationale:** EF Core is the ORM used to interact with both databases. It was selected for its tight integration with ASP.NET Core (dependency injection, `DbContext` lifetime management), its ability to generate and execute parameterised SQL queries from LINQ expressions, and its change-tracking capability (disabled via `AsNoTracking()` for read operations). Schema management differs by component: `FashionStoreAPI` uses EF Core migrations (applied via `dotnet ef database update`); `FashionDataAnalysisPlatform` uses `EnsureCreated()` for the main schema supplemented by raw `IF NOT EXISTS` DDL statements in `Program.cs` for the three forecast tables (which were added after the initial schema was created). Migration files exist in the repository for both projects, but only the API migrations are actively applied; the analytics app's migration files are development artifacts not executed at runtime.

### SQL Server (LocalDB)
**Selection rationale:** SQL Server LocalDB was selected as the database engine because it runs in-process without requiring a separate server installation, making it suitable for a thesis demonstration environment. Two separate LocalDB instances are used: `FashionStoreApiDb` for transactional data and `FashionRetailDb` for analytics data. SQL Server's T-SQL dialect supports the `IF NOT EXISTS` DDL pattern used for backwards-compatible table creation and the `OBJECT_ID` function used to check for column existence. The `ODBC Driver 17 for SQL Server` enables the Python ML script to write directly to `FashionRetailDb` via pyodbc.

### Python 3 (Data Generator and ML Script)
**Selection rationale:** Python was selected for both the data generator and the ML forecasting script because it is the de-facto standard language for data science and machine learning, providing the `scikit-learn`, `pandas`, `numpy`, `statsmodels`, and `pyodbc` libraries that would require significant effort to replicate in C#. The generator and forecaster are standalone scripts that interact with the system via HTTP (generator → API) and ODBC (forecaster → database), maintaining a clear separation from the .NET application. The `Process.Start()` subprocess pattern in `ForecastingService` allows the .NET application to invoke the Python script without requiring Python as a first-class dependency of the .NET process.

### Scikit-Learn
**Selection rationale:** Scikit-learn is the Python machine learning library used for the Random Forest forecasting model. Specifically, `RandomForestRegressor` with 200 estimators is wrapped in `MultiOutputRegressor` to produce simultaneous predictions for Revenue, Orders, and Units. Scikit-learn was selected because it is production-grade, well-documented, and provides consistent APIs for model training, prediction, and feature importance extraction. The `ExponentialSmoothing` model from `statsmodels` is also used for Holt-Winters comparison.

### JavaScript (Vanilla)
**Selection rationale:** Vanilla JavaScript (ES2022) is used for all client-side interactivity: the global search autocomplete dropdown, the notification bell panel, dashboard live-polling (via `setInterval` and `fetch`), the forecast model selection tabs, and the ABC/XYZ drill-down modal. No JavaScript framework (React, Vue, Angular) is used, keeping the frontend dependencies minimal. All JavaScript is embedded in Razor views rather than in separate `.js` files, which is consistent with the MVC template's approach for a server-rendered application.

### Bootstrap 5
**Selection rationale:** Bootstrap 5 is the CSS framework used for responsive layout, component styling (cards, badges, tabs, modals, tables, progress bars), and colour utilities. It was selected because it is the default framework bundled with the ASP.NET Core MVC project template and requires no additional configuration. Bootstrap's grid system and utility classes allow rapid responsive layout without writing custom CSS. The dark sidebar uses Bootstrap utilities with custom overrides defined in `site.css`.

### Chart.js
**Selection rationale:** Chart.js is the JavaScript charting library used for all data visualisations: line charts (revenue trend, actual vs forecast), bar charts (category revenue, feature importance, growth %), doughnut charts (channel split), radar charts (store comparison), bubble charts (waste risk matrix), and horizontal bar charts (STR by category, markdown dependency). Chart.js was selected because it is lightweight, requires no build step, and supports all chart types needed by the platform. Data is injected from the server via `JsonSerializer.Serialize()` in Razor views, eliminating the need for client-side data fetching for most charts.

---

## 7. Database Design

### FashionStoreApiDb (Transactional Database)

#### Store
- **Purpose:** Represents a physical or digital retail location.
- **Fields:** `StoreId` (PK, identity), `StoreName`, `City`, `Country`, `StoreType` (e.g., "Flagship", "Urban Store", "Premium Boutique"), `Region`.
- **Seeded data:** 3 stores — Scuffer District (Bucharest, Romania, Flagship), Scuffer Downtown (Cluj-Napoca, Romania, Urban Store), Maison Toulouse (Paris, France, Premium Boutique).
- **Relationships:** One-to-many with Product, Order.

#### Product
- **Purpose:** Represents an individual SKU at a specific store. The same ProductCode exists as two separate Product records (one per Scuffer store) to accommodate store-specific pricing.
- **Fields:** `ProductId` (PK), `StoreId` (FK → Store), `ProductCode`, `ProductName`, `Category`, `Color`, `Season`, `UnitPrice`, `Brand`, `Gender`, `Material`, `BaseCost`, `IsSeasonal`, `LaunchDate`.
- **Unique constraint:** `(StoreId, ProductCode)` — a given SKU can only exist once per store.
- **Notes:** `BaseCost` is approximately 42% of `UnitPrice` for Scuffer and 30% for Maison Toulouse. Scuffer District `UnitPrice` = Downtown price × 1.05 (District premium pricing).
- **Relationships:** One-to-one with Inventory, one-to-many with OrderItem.

#### Inventory
- **Purpose:** Tracks real-time stock levels for each product. One inventory record per product (1:1 with Product).
- **Fields:** `InventoryId` (PK), `ProductId` (FK → Product, unique), `CurrentStock`, `MinimumStockThreshold`, `LastRestockDate`, `LastUpdated`.
- **Seeded values:** Scuffer Core: 350 units, min 40. Scuffer Seasonal: 250 units, min 30. Maison Toulouse Core: 100 units, min 12. Maison Toulouse Seasonal: 80 units, min 10.
- **Logic:** `CurrentStock` is decremented on each order item. When `CurrentStock ≤ MinimumStockThreshold`, the API triggers a restock. `LastRestockDate` is initialised to `LaunchDate` so aging analytics start correctly.

#### Order
- **Purpose:** Represents a customer purchase transaction.
- **Fields:** `OrderId` (PK), `StoreId` (FK → Store), `OrderDate`, `TotalAmount`, `CustomerId` (nullable, GUID string), `SalesChannel` ("Online", "MobileApp", "Physical").
- **Relationships:** One-to-many with OrderItem.

#### OrderItem
- **Purpose:** Represents one line in an order (one product, one quantity, one price).
- **Fields:** `OrderItemId` (PK), `OrderId` (FK → Order), `ProductId` (FK → Product, restricted delete), `Quantity`, `UnitPrice`, `LineTotal`, `Size` ("XS"/"S"/"M"/"L"/"XL"/"XXL"), `Color`, `DiscountPercent`, `TotalCost`, `Profit`.
- **Computed:** `LineTotal = UnitPrice × Quantity × (1 - DiscountPercent/100)`. `TotalCost = BaseCost × Quantity`. `Profit = LineTotal - TotalCost`.

---

### FashionRetailDb (Analytics Database)

#### StoreConnection
- **Purpose:** Configuration record for each connected retail API endpoint. Controls which APIs the sync service polls.
- **Fields:** `StoreConnectionId` (PK), `StoreName`, `StoreApiUrl` (e.g., `https://localhost:7151`), `IsActive` (bool), `LastSyncAt` (nullable datetime — the delta sync cursor).
- **Seeded:** One record ("FashionStoreAPI", `https://localhost:7151`, active) is seeded at startup if no connections exist.

#### Store (analytics)
- **Purpose:** Local mirror of API Store records. Foreign-keyed to `StoreConnection` to track the source.
- **Fields:** `StoreId` (PK, local), `StoreConnectionId` (FK → StoreConnection), `ExternalStoreId` (maps to API `Store.StoreId`), `StoreName`, `City`, `Country`, `StoreType`, `Region`.
- **Relationships:** One-to-many with Product, Sale, Inventory.

#### Product (analytics)
- **Purpose:** Local mirror of API Product records.
- **Fields:** `ProductId` (PK, local), `StoreConnectionId` (FK), `StoreId` (FK, local), `ExternalProductId` (maps to API `Product.ProductId`), `ProductCode`, `ProductName`, `Category`, `Color`, `Season`, `UnitPrice`, `Brand`, `Gender`, `Material`, `BaseCost`, `IsSeasonal`, `LaunchDate`.
- **Unique constraint:** `(StoreConnectionId, StoreId, ProductCode)`.
- **Relationships:** One-to-many with Sale; one-to-one with Inventory.

#### Sale (fact table)
- **Purpose:** The core fact table. Each row represents one order item that has been synced from the API. This is the primary data source for all revenue, profit, and volume analytics.
- **Fields:** `SaleId` (PK), `ProductId` (FK, local), `StoreConnectionId` (FK), `StoreId` (FK, local), `ExternalOrderId` (maps to API `Order.OrderId`), `ExternalOrderItemId` (maps to API `OrderItem.OrderItemId` — deduplication key), `ExternalProductCode`, `SaleDate`, `Quantity`, `UnitPrice`, `Revenue`, `Size`, `Color`, `DiscountPercent`, `TotalCost`, `Profit`, `CustomerId`, `SalesChannel` (actual values: `"Online"`, `"MobileApp"`, `"Physical"`).
- **Deduplication:** The pair `(StoreConnectionId, ExternalOrderId, ExternalOrderItemId)` is checked before each insert to prevent duplicate sales records.

#### Inventory (analytics)
- **Purpose:** Snapshot of current stock levels synced from the API.
- **Fields:** `InventoryId` (PK, local), `StoreConnectionId` (FK), `StoreId` (FK, local), `ExternalInventoryId` (maps to API `Inventory.InventoryId`), `ProductId` (FK, local), `CurrentStock`, `MinimumStockThreshold`, `LastRestockDate`, `LastUpdated`.

#### ForecastResults
- **Purpose:** Stores the output of each ML forecasting run. All rows are replaced on each new run (DELETE then INSERT).
- **Fields:** `ForecastResultId` (PK), `StoreId` (nullable int, references local Store), `StoreName`, `Category`, `ForecastMonth` (DATETIME2 — first day of month), `RevenueForecast` (DECIMAL 18,2), `OrdersForecast` (INT), `UnitsForecast` (INT), `GeneratedAt` (timestamp of the run), `ModelName` (name of the winning model — `NVARCHAR(50) NULL`).
- **Created:** Via `IF NOT EXISTS CREATE TABLE` DDL in both `Program.cs` and `ml/fashion_forecaster.py`. The `ModelName` column is absent from the `Program.cs` DDL and is added to the table by `ml/fashion_forecaster.py`'s `ensure_tables()` function via `ALTER TABLE` on the first forecast run.

#### ForecastAccuracies
- **Purpose:** Stores accuracy metrics for all three models evaluated in each forecasting run. Allows the UI to display a model comparison table.
- **Fields:** `ForecastAccuracyId` (PK), `ModelName` ("Naive Baseline"/"Holt-Winters"/"Random Forest"), `Target` ("Revenue"/"Orders"/"Units"), `MAE`, `RMSE`, `MAPE`, `AccuracyPercent`, `GeneratedAt`.
- **Note:** Three rows per Target × three models = 9 rows per run. Previous runs' rows are deleted on each new run.
- **Schema note:** The `ModelName` column is absent from the C# DDL in `Program.cs`. It is added to the table by `ml/fashion_forecaster.py`'s `ensure_tables()` function via `ALTER TABLE` on the first forecast run. The table does not match the schema described above until after the first forecast generation.

#### ForecastFeatureImportances
- **Purpose:** Stores feature importance values extracted from the Random Forest Revenue model. Used to render the horizontal bar chart and the feature interpretation text in the Forecasting module.
- **Fields:** `ForecastFeatureImportanceId` (PK), `FeatureName` (human-readable display name), `Importance` (DECIMAL 10,6 — raw `feature_importances_` value from scikit-learn), `Target` ("Revenue"), `GeneratedAt`.

#### Prediction (legacy, unused)
The `Prediction` model class exists in the codebase and is registered in `AppDbContext`. It was the placeholder for a future CRUD-based prediction table, now superseded by the three dedicated forecast tables. It is not used by any controller or service.

---

## 8. Authentication & Security

### Cookie Authentication
Authentication is implemented using ASP.NET Core's built-in cookie authentication middleware (`Microsoft.AspNetCore.Authentication.Cookies`). Configuration in `Program.cs`:
```
LoginPath:        /Account/Login
LogoutPath:       /Account/Logout
AccessDeniedPath: /Account/Login
ExpireTimeSpan:   8 hours (sliding)
Cookie.Name:      FashionBI.Auth
Cookie.HttpOnly:  true
Cookie.SecurePolicy: SameAsRequest
```
The cookie is `HttpOnly` (prevents JavaScript access, mitigating XSS-based cookie theft) and uses a sliding 8-hour expiry (session extends as long as the user is active).

### Claims
On successful login, the following claims are added to the `ClaimsIdentity`:
- `ClaimTypes.Name` — User's full name (e.g., "Demo User").
- `ClaimTypes.Email` — User's email address.
- `ClaimTypes.Role` — User's role (e.g., "Admin").
- `"Initials"` — Computed initials from the full name (e.g., "DU"), used in the avatar display in the top navigation bar.
- `"LoginAt"` — ISO 8601 timestamp of the login event, displayed on the profile page.

### Authorization
All analytics controllers use the `[Authorize]` attribute at class level, which requires an authenticated cookie session for every action within the controller. The `AccountController` uses `[AllowAnonymous]` on its Login actions. Unauthorized requests are automatically redirected to `/Account/Login` by the middleware.

### Demo Login
A `DemoLogin` POST action on `AccountController` bypasses credential checking and signs in the user directly using the configured demo credentials. This is protected by `[ValidateAntiForgeryToken]` and `[AllowAnonymous]`. It enables thesis evaluation without credential knowledge.

### CSRF Protection
All standard form POST actions use `[ValidateAntiForgeryToken]`, which validates the anti-forgery token embedded in the Razor form. AJAX POST endpoints in `StoreConnectionController` (`TestConnection`, `SyncNow`, `Toggle`) use `[IgnoreAntiforgeryToken]` because they are invoked by JavaScript `fetch()` calls and return JSON rather than form redirects.

### Protected Controllers
All of the following controllers are decorated with `[Authorize]`:
DashboardController, SalesController, InventoriesController, StoreComparisonController, ForecastingController, SmartInsightsController, SustainabilityController, StoreConnectionController, NotificationController, SearchController.

---

## 9. Core Modules

### 9.1 Executive Dashboard

**Purpose:** Provides a real-time top-line view of retail network performance. It is the landing page after login and the primary entry point for operational monitoring.

**Inputs:** Store filter (multi-select), date range selector (7d, 30d, 90d, 12m, all-time). Both are passed as query string parameters.

**Outputs:**
- 6 KPI cards: Total Revenue, Total Orders, Units Sold, Gross Profit, Average Order Value, Profit Margin — each with a prior-period trend indicator (up/down arrow + percentage change).
- Additional status indicators: Top Category, Low Stock Count.
- Revenue & Profit Trend chart (line chart) — granularity adapts: daily for 7d/30d, weekly for 90d, monthly for 12m/all-time.
- Revenue & Profit by Category (grouped bar chart, top 8 categories).
- Sales Channel Distribution (doughnut chart).
- Top 5 Products table (name, category, revenue, units, margin).
- 4 contextual alert cards (inventory health, profitability status, top category note, sync status).

**Business value:** Allows operational managers to assess network health in under 30 seconds without drilling into module-specific pages.

**Key calculations:**
- `AverageOrderValue = TotalRevenue / DistinctOrderCount`
- `ProfitMargin = GrossProfit / TotalRevenue × 100`
- `PriorPeriod = same duration immediately preceding the current period`
- Revenue trend granularity: 7d/30d → daily; 90d → weekly (ISO Monday of each week); 12m/all → monthly.

**Live refresh:** The `LiveMetrics` endpoint returns all KPIs and chart data as JSON. A JavaScript interval (15 seconds) calls this endpoint and updates the DOM without a full page reload, simulating a live dashboard.

---

### 9.2 Sales Analytics

**Purpose:** Provides detailed analysis of sales performance including revenue trends, profitability, channel mix, product performance, and discount efficiency.

**Inputs:** Store filter, date range selector. Single in-memory data fetch is performed at the top of the action method; all analytics are computed from this in-memory collection.

**Outputs:**
- 6 KPI cards with prior-period trends: Revenue, Orders, Units, Gross Profit, AOV, Profit Margin.
- Revenue Trend + AOV Trend (dual-axis line chart, granularity-adaptive).
- Discount Band Efficiency (bar chart) — sales split across 5 discount bands: 0%, 1–10%, 11–20%, 21–30%, 30%+; showing revenue, unit count, and profit margin per band.
- Top 5 Products table.
- Color Performance (top 6 colors by revenue — bar chart).
- Size Distribution (XS/S/M/L/XL/XXL — bar chart).
- Channel Split Summary (revenue, share%, AOV per channel).
- 6 auto-generated Smart Insights (revenue trend direction, best category, product concentration, discount efficiency, AOV trend, strongest channel).

**Business value:** Enables merchandising teams to identify top performers, understand channel performance, and optimise discount strategy.

**Key calculations:**
- Discount band assignment: `0% → band 0; ≤10% → band 1; ≤20% → band 2; ≤30% → band 3; >30% → band 4`
- Revenue trend direction: first half vs second half of the period (>5% change = significant).

---

### 9.3 Inventory Intelligence

**Purpose:** Provides a comprehensive snapshot of current inventory health, including stock coverage, aging, reorder priority, and dead stock identification.

**Inputs:** Store filter, date range (affects sales velocity calculations).

**Outputs:**
- 6 KPI cards: Total SKUs, Units on Hand, Inventory Value (at unit price), Low Stock Count, Out-of-Stock Count, Weeks of Cover.
- 4 secondary KPIs: Sell-Through Rate, Stock-to-Sales Ratio, Inventory Turnover, Overstock Count.
- Low Stock Alerts table (top 10 SKUs at or below threshold, sorted by stock level ascending).
- Dead Stock table (top 10 SKUs with no sales in 90+ days, sorted by idle days descending).
- Inventory Aging histogram (5 bands: <30d, 30–60d, 61–90d, 91–180d, >180d — based on `LastRestockDate`).
- Reorder Priority counts (Critical/OOS, High/below threshold, Medium/1–2× threshold, Low/>2× threshold).
- Category Inventory Value (horizontal bar chart, top 8 categories by retail value).
- 6 auto-generated Smart Insights.

**Business value:** Gives inventory managers an immediate view of where replenishment is needed and where capital is tied up unproductively.

**Key calculations:**
- `WeeksOfCover = UnitsOnHand / (TotalSoldInPeriod / RefWeeks)`
- `SellThroughRate = SoldUnits / (SoldUnits + CurrentStock) × 100`
- `StockToSalesRatio = CurrentStock / SoldUnits`
- `InventoryTurnover = (SoldUnits / (RefDays/365)) / CurrentStock` (annualised)
- `OverstockCount`: SKUs where `CurrentStock > (WeeklyVelocity × 8)` (8-week excess threshold)
- Dead stock: `CurrentStock > 0 AND no sales in last 90 days`

---

### 9.4 Store Comparison

**Purpose:** Enables side-by-side benchmarking of all active stores across revenue, profitability, inventory health, and channel mix dimensions.

**Inputs:** Date range selector. No store filter (shows all stores by design).

**Outputs:**
- Per-store metric cards: Revenue, Profit, Orders, Units, AOV, Margin, Channel split (Online%/MobileApp%/Physical%), Inventory Value, Inventory Turnover, Low Stock, OOS, Dead Stock.
- Prior-period comparisons for Revenue, Margin, and AOV per store.
- 6 auto-generated cross-store Smart Insights: revenue leader and gap, margin leader, highest AOV store, best inventory turnover, highest-risk store (OOS+lowStock+deadStock aggregate), channel dominance differences.

**Business value:** Allows management to identify high-performing stores and understand what drives their outperformance.

**Key calculations:**
- Dead stock per store: SKUs with `CurrentStock > 0` and `(today − lastSaleDate) ≥ 90 days`.
- Channel percentages: `ChannelRevenue / TotalRevenue × 100` per store.
- Revenue gap: `(topStoreRevenue − bottomStoreRevenue) / bottomStoreRevenue × 100`.
- All per-store analytics are computed in-memory after 5 DB queries (stores, sales, prior-period sales, inventories, product prices).

---

### 9.5 Forecasting

**Purpose:** Generates 3-month demand forecasts at Store × Category granularity using a multi-model ML pipeline. Provides inventory risk alerts, year-over-year comparisons, feature importance insights, and a Strategic Outlook with actionable recommendations.

*This module is described in depth in Section 10.*

---

### 9.6 Smart Insights

**Purpose:** The prescriptive analytics layer. Provides four analytical tools: Business Health Score, Strategic Action Board, KPI Exception Detection, and ABC/XYZ Portfolio Matrix.

*This module is described in depth in Section 11.*

---

### 9.7 Sustainability

**Purpose:** Quantifies inventory waste risk across seasons and categories using sell-through rate, markdown dependency, dead stock detection, and a composite Waste Risk Score.

*This module is described in depth in Section 12.*

---

### 9.8 Store Connections

**Purpose:** Management interface for the data source configuration of the analytics platform. Allows users to view, test, and control the synchronisation connections to retail APIs.

**Inputs:** Connection record ID (for actions), no form input for the index view.

**Outputs (Index view):**
- Table of all `StoreConnection` records: StoreName, StoreApiUrl, IsActive status, LastSyncAt timestamp.
- Per-connection action buttons: Test Connection (AJAX), Sync Now (AJAX), Toggle Active/Inactive (AJAX).

**Actions:**
- **TestConnection:** Makes a `GET /api/Stores` call to the connection's URL with a 5-second timeout. Returns JSON `{ok, message}`.
- **SyncNow:** Verifies the connection is reachable, then updates `LastSyncAt = DateTime.Now` to signal the background service to sync from that point. Returns JSON `{ok, message, lastSync}`.
- **Toggle:** Flips `IsActive` between true and false. Inactive connections are skipped by the background service.

---

### 9.9 Notifications

**Purpose:** Provides a live, context-aware notification system that surfaces actionable alerts from the platform's analytics without storing notification records in a database table.

*This module is described in depth in Section 13.*

---

### 9.10 Global Search

**Purpose:** Provides a unified autocomplete search across all platform entities and navigation pages, accessible from every page via the top navigation bar.

*This module is described in depth in Section 14.*

---

## 10. Forecasting Module (In-Depth)

### Workflow Overview
1. User navigates to `/Forecasting` and sees a message that no forecasts have been generated yet.
2. User clicks "Generate Forecasts". The `ForecastingController.TriggerRefresh()` POST action invokes `ForecastingService.TriggerRefreshAsync()`.
3. `ForecastingService` resolves the path to `ml/fashion_forecaster.py` and launches it as a Python subprocess with a 5-minute timeout.
4. The Python script executes the full pipeline: fetch data → engineer features → evaluate 3 models → compare → retrain best → generate forecasts → write to DB.
5. On completion, the controller redirects to `GET /Forecasting/Index`, which calls `ForecastingService.BuildViewModelAsync()` to render the dashboard.

### Data Preparation (Python)
The script queries `FashionRetailDb` via pyodbc for the last 24 months of sales, aggregated at the `(StoreName, StoreId, Category, Year, Month)` granularity:
```sql
SELECT st.StoreName, st.StoreId, p.Category,
       YEAR(s.SaleDate) AS [Year], MONTH(s.SaleDate) AS [Month],
       SUM(s.Revenue) AS Revenue,
       COUNT(DISTINCT s.ExternalOrderId) AS Orders,
       SUM(s.Quantity) AS Units
FROM Sales s
INNER JOIN Stores st ON s.StoreId = st.StoreId
INNER JOIN Products p ON s.ProductId = p.ProductId
WHERE s.SaleDate >= [24 months ago]
GROUP BY st.StoreName, st.StoreId, p.Category, YEAR(s.SaleDate), MONTH(s.SaleDate)
```

### Feature Engineering

The `build_features()` function adds the following columns to each `(StoreName, Category)` time series. The production model uses the 14-feature configuration below. Prior-year (YoY) features were engineered and evaluated but excluded from the production model following empirical testing (see Forecasting Investigation section).

| Feature | Description |
|---|---|
| `year_num` | Calendar year (integer) |
| `month_num` | Calendar month (1–12) |
| `quarter` | Calendar quarter (1–4) |
| `store_encoded` | Ordinal encoding of StoreName |
| `category_encoded` | Ordinal encoding of Category |
| `lag_1_revenue` | Revenue in previous month |
| `lag_3_revenue` | Rolling 3-month average (shifted 1) |
| `lag_6_revenue` | Rolling 6-month average (shifted 1) |
| `lag_1_orders` | Orders in previous month |
| `lag_1_units` | Units in previous month |
| `is_holiday_month` | 1 if month ∈ {11, 12, 1, 6} |
| `is_collection_launch` | 1 if month ∈ {2, 3, 4, 8, 9, 10} |
| `is_summer` | 1 if month ∈ {6, 7, 8} |
| `is_winter` | 1 if month ∈ {12, 1, 2} |

**Investigated but excluded from production:** `lag_12_revenue`, `lag_12_orders`, `lag_12_units` (revenue/orders/units in the same calendar month of the prior year). These features were built and tested alongside a missing-value indicator (`lag_12_available`) to handle the absence of prior-year data for months 1–12 of each series. Empirical evaluation showed the 14-feature model outperformed the 18-feature YoY model (56.1% vs 60.4% Revenue WMAPE), indicating that prior-year features provided net negative value at 24 months of history.

Lag NaN rows at the beginning of each series where lag values cannot be computed are dropped from the training set.

### Training Process
**Time-based split:** The last 3 months of the dataset are held out as the test set. All preceding months form the training set. The cutoff is `df["Period"].max() - 3 months`.

**Three model families are evaluated:**

1. **Naive Baseline:** For each `(Store, Category)` series, the last training month value is repeated for all 3 test months. Provides a lower-bound benchmark.

2. **Holt-Winters Exponential Smoothing (statsmodels):** Per-series Holt-Winters with additive trend + additive seasonality (period=12). Fallback chain: if seasonal fit fails (series too short), attempt trend-only; if that fails, use Simple Exponential Smoothing. Per-target fit (Revenue, Orders, Units evaluated independently).

3. **Random Forest (scikit-learn):** `MultiOutputRegressor(RandomForestRegressor(n_estimators=200, random_state=42, n_jobs=1))`. A single global model trained on all Store × Category combinations produces simultaneous predictions for Revenue, Orders, and Units. `n_jobs=1` is required on Windows to avoid multiprocessing spawn issues in subprocess context. Two RF configurations are empirically compared on each run: RF-14 (14 calendar + momentum features, no YoY) and RF-18 (same 14 features plus prior-year lag and availability flag). The configuration with the lower Revenue WMAPE is selected as the RF candidate.

**Model selection:** All three model families are compared by **Revenue WMAPE** (Weighted Mean Absolute Percentage Error). The model with the lowest WMAPE — including the best RF configuration from the internal RF-14 vs RF-18 comparison — is selected as the winner.

**Full retraining:** The winning model is retrained on the **full 24-month dataset** (not just the training split) before generating production forecasts.

### Evaluation Results

Final metrics from the held-out 3-month test window (the last 3 months of the 24-month dataset):

| Model | Revenue WMAPE | Revenue Accuracy |
|---|---|---|
| **Random Forest (14 features)** | **56.1%** | **43.9%** |
| Naive Baseline | 71.5% | 28.5% |
| Holt-Winters | 74.4% | 25.6% |

**Winner: Random Forest** with Revenue WMAPE 56.1% / Revenue Accuracy 43.9%.

Additional accuracy metrics for the winning RF model:
- Orders Accuracy: 52.5%
- Units Accuracy: 51.2%

**Validation setup:**
- Method: Time-based holdout split
- Training window: approximately 20 months per Store × Category series (~398 total training rows across 21 Store × Category combinations)
- Test window: final 3 months of the dataset (~63 test rows)

### Forecasting Investigation

During model evaluation, a methodological issue was identified in the prior-year lag feature implementation. The `shift(12).fillna(0)` pattern converted missing prior-year observations — present for all months in the first year of each series — into literal zeros, teaching the model that months with normal revenue had zero prior-year revenue and corrupting over half of all training rows.

Three correction strategies were evaluated:

- **Drop affected rows:** Removing rows where lag_12 was undefined shrank the training dataset from ~398 to ~189 rows and eliminated all spring/summer training observations (the only April–June 2025 data). This caused catastrophically worse accuracy — Revenue WMAPE increased to 68.2%.
- **Missing-value indicator:** Adding a binary `lag_12_available` flag allowed the RF to branch between the "no prior history" (year 1) and "genuine YoY value" (year 2) regimes without learning spurious zero-based associations. The 18-feature version with indicator achieved 60.4% Revenue WMAPE.
- **Exclude YoY features entirely:** Training on 14 calendar and momentum features (no prior-year lags) achieved 56.1% Revenue WMAPE — better than both the uncorrected 17-feature baseline (60.2%) and the corrected 18-feature model (60.4%).

**Conclusion:** The 14-feature model was selected for production. Further optimisation options (RF hyperparameter tuning, aggregation level changes, additional feature engineering) were investigated and found unlikely to produce improvements exceeding 5 percentage points without additional historical data. The primary accuracy constraint is structural: with only one seasonal cycle in the training window, the model cannot reliably learn the spring-to-summer and summer-to-winter transitions. The forecasting module was frozen following this investigation.

### Forecasting Logic
Forecasts are generated for the next 3 calendar months after the most recent month in the dataset.

**For Random Forest:** An autoregressive approach is used. For each step (M+1, M+2, M+3):
- The feature vector is constructed using the rolling lag buffer (updated after each step with the just-predicted value).
- `lag_12_*` features always pull from the actual training data (9–11 months back, always within the 24-month window).
- The prediction is made, clipped to 0 at minimum, and appended to the rolling buffer for the next step.

**For Holt-Winters:** `fitted.forecast(3)` generates all 3 months simultaneously from each series' fitted model.

**For Naive Baseline:** The last training month value is used for all 3 forecast months.

### Forecast Tables
All previous forecast rows are deleted before inserting the new run's results. Three tables are written:
- **ForecastResults:** One row per `(StoreName, Category, ForecastMonth)` combination. For 3 stores × 7 categories × 3 months = up to 63 rows (practical count depends on how many (store, category) combinations have sufficient history).
- **ForecastAccuracies:** 9 rows per run (3 targets × 3 models). Stores metrics for all models so the UI can show a comparison table.
- **ForecastFeatureImportances:** 14 rows (one per feature) extracted from the Revenue estimator of the winning Random Forest model (`estimators_[0].feature_importances_`).

### Accuracy Metrics
- **MAE (Mean Absolute Error):** Average absolute difference between predicted and actual values.
- **RMSE (Root Mean Squared Error):** Square root of the average squared error; penalises large errors more.
- **WMAPE (Weighted Mean Absolute Percentage Error):** `Σ|actual - predicted| / Σ|actual| × 100`. Used as the primary model selection criterion because it handles zero-actual cases better than standard MAPE.
- **AccuracyPercent:** `max(0, 100 - WMAPE)`. Used in the UI confidence label (≥70% = High Confidence, ≥50% = Medium, <50% = Low).

### Confidence Intervals
Confidence bands are computed in `ForecastingService` rather than in the Python script. For each forecast month:
- `UpperBound = ForecastedRevenue × (1 + MAPE/100)`
- `LowerBound = max(0, ForecastedRevenue × (1 - MAPE/100))`

This gives a simple MAPE-symmetric interval around the point forecast, visualised as a shaded area on the Actual vs Forecast chart. The minimum error fraction is capped at 0.05 (5%) to ensure intervals are always visible.

### Feature Importance
The Revenue model's `feature_importances_` array (from `estimators_[0]` of the MultiOutputRegressor) is extracted and stored in `ForecastFeatureImportances`. The .NET service groups the 14 production features into three business-language driver categories:
- **Sales Momentum:** lag_1, lag_3, lag_6 revenue/orders/units features.
- **Seasonal Patterns:** holiday month, collection launch, summer, winter, month, quarter features.
- **Calendar & Identity:** year, store, category encoded features.

The grouping percentages are displayed as a donut chart in the "What Drives the Forecast" section of the UI.

---

## 11. Smart Insights Methodology (In-Depth)

### Context
Smart Insights is the prescriptive analytics module, the fourth level of the Gartner Analytics Maturity Model. It synthesises signals from Sales, Inventory, Forecasting, and Products into four concrete analytical tools.

All computation occurs in `SmartInsightsService.BuildViewModelAsync()`, which loads data from 5 DB queries (sales, products, inventories, stores, forecasts) and then performs all calculations in-memory using C# LINQ. The data window is **24 months** for all four sections.

### Section 1: Business Health Score (0–100)

The Health Score is a composite metric made up of four equally-weighted components (25 points each):

**Component 1: Revenue Score (0–25)**
- Computes the 12-month rolling monthly average (`avgMonthly`).
- Compares the current calendar month's revenue to `avgMonthly`.
- `revRatio = currentRevenue / avgMonthly`
- `revScore = min(25, revRatio × 20)`
- Status labels: ≥1.1 ratio = "Above Trend"; ≥0.85 = "On Track"; <0.85 = "Below Trend".

**Component 2: Profitability Score (0–25)**
- Computes gross profit margin over the last 3 months: `margin = Profit / Revenue × 100`.
- Benchmarked against 25% industry standard.
- `profScore = min(25, margin / 25 × 25)`
- Status labels: ≥25% = "Strong"; ≥15% = "Adequate"; <15% = "Weak".

**Component 3: Inventory Score (0–25)**
- `healthy = count of SKUs where CurrentStock > MinimumStockThreshold`
- `invScore = round(healthy / total × 25)`
- Status labels: ≥20/25 score = "Healthy"; ≥12/25 = "Monitor"; <12/25 = "At Risk".

**Component 4: Forecast Score (0–25)**
- Uses the best Revenue model's AccuracyPercent from `ForecastAccuracies`.
- `fcastScore = min(25, forecastAccuracy / 100 × 25)`
- If no forecasts exist, defaults to 12 (neutral/midpoint).
- Status labels: ≥70% accuracy = "High Confidence"; ≥50% = "Medium Confidence"; else "Low Confidence".

**Composite Health Score:**
- `HealthScore = revScore + profScore + invScore + fcastScore`
- Status: ≥70 = "Healthy" (emerald); ≥45 = "Caution" (amber); <45 = "At Risk" (red).
- Explanation text identifies which components are dragging the score below threshold.

### Section 2: Strategic Action Board

Actions are generated by evaluating 8 distinct signals from the pre-loaded data. Actions are classified into 4 urgency tiers:

**URGENT (🔴 — revenue at immediate risk)**
1. **Inventory gaps from forecast:** For each `(Store, Category)` where `ForecastedUnits > CurrentStock`, compute `impact = gap × avgCategoryPrice × (forecastAccuracy/100)`. Threshold: impact > €150. Explanation includes months of coverage.
2. **OOS with recent sales velocity:** Products with `CurrentStock == 0` that had ≥€200 revenue in the last 30 days. Impact = last 30-day revenue (as an ongoing cost per month).

**OPPORTUNITY (🟠 — revenue to capture)**
3. **Forecast growth + adequate stock:** Categories where `ForecastedRevenue > Last3mActual × 1.08`. Impact = forecast revenue − actual revenue.
4. **Fastest-growing category (actual trend):** Category with highest QoQ revenue growth (>12%) in actuals. Impact = current quarter revenue − prior quarter.
5. **High-AOV underweighted channel:** A sales channel with AOV >12% above the network average but <30% revenue share. Impact = estimated uplift if grown to 30% share.

**OPTIMIZE (🟡 — capital recovery)**
6. **Overstock vs forecast:** Categories where `CurrentStock > ForecastedUnits × 2`. Impact = `excess × avgPrice × 0.35` (estimated recovery at 35% markdown).
7. **Margin erosion by category:** Categories running ≥8 percentage points below the network average margin. Impact = `Revenue × (halfTheGap / 100)`.

**MONITOR (🟢 — watch for deterioration)**
8. **Declining category trend (−5% to −25% QoQ):** Moderate declines that are not yet critical but warrant attention.
9. **Low-stock below threshold (not OOS):** Aggregate count of SKUs below minimum threshold but not yet out of stock.

Each action contains: `Title`, `Explanation`, `SuggestedAction`, `EstimatedImpact` (€), `ImpactLabel`, `Store`, `ProductCategory`, `DataBadge` (source modules cited), `Icon`, `ActionCategory`.

Actions within each tier are sorted by `EstimatedImpact` descending. Maximum 6 actions per tier displayed.

### Section 3: KPI Exception Detection (SPC)

The approach is adapted from Statistical Process Control: monitor whether the current month's KPI deviates significantly from the 11-month baseline (i.e., the 11 months prior to the current month).

**Dimensions monitored:**
1. Revenue by Store — one series per store.
2. Revenue by Category — one series per category.
3. Profit Margin by Category — one series per category.

**Algorithm per series:**
1. Compute the baseline: 11 monthly data points (months −11 to −1 from current month).
2. Compute `mean` and `stddev` (population standard deviation).
3. If `baseline.Count < 4` or `mean < 200`, skip (insufficient data or trivially small KPI).
4. Compute `sigma = (currentMonthValue - mean) / stddev`.
5. If `|sigma| < 1.5`, no exception — within normal bounds.
6. If `|sigma| ≥ 1.5`: severity = Warning if 1.5 ≤ |σ| < 2.0, Critical/Positive if |σ| ≥ 2.0.
7. Negative sigma (below baseline) = Warning/Critical. Positive sigma (above baseline) = Positive/Success.

**Population standard deviation formula:**
`stddev = sqrt( mean( [ (x - mean)^2 for x in baseline ] ) )`

Exceptions are sorted by absolute sigma descending to surface the most extreme deviations first.

### Section 4: ABC/XYZ Portfolio Matrix

The matrix classifies all products across two independent dimensions:

**ABC (revenue concentration, last 12 months):**
- Sort all products by 12-month revenue descending.
- Accumulate cumulative revenue as a percentage of total.
- **A:** products accounting for the first 80% of cumulative revenue (high-value, typically few products).
- **B:** products accounting for the next 15% (80–95% cumulative).
- **C:** remaining products (95–100% cumulative) + products with zero sales (forced to CZ "Exit").

**XYZ (demand variability, last 12 months):**
- Compute monthly revenue per product.
- Calculate Coefficient of Variation: `CV = stddev(monthly revenues) / mean(monthly revenue)`.
- **X:** CV ≤ 0.30 (stable, predictable demand).
- **Y:** 0.30 < CV ≤ 0.60 (moderate variability).
- **Z:** CV > 0.60 (highly variable or unpredictable) or fewer than 3 months of sales data.
- Products with zero average monthly revenue → forced to Z.

**Nine-cell matrix (3×3) with strategic labels:**

| | X (Stable) | Y (Variable) | Z (Unpredictable) |
|---|---|---|---|
| **A (High Value)** | AX: Protect | AY: Invest | AZ: Manage |
| **B (Mid Value)** | BX: Maintain | BY: Review | BZ: Reduce |
| **C (Low Value)** | CX: Consolidate | CY: Defer | CZ: Exit |

Each cell shows: product count, aggregate revenue, revenue% of total, and a list of up to 20 products (sorted by revenue descending) with name, code, category, revenue, CV score, current stock, and classification. Clicking any cell opens a Bootstrap modal with the full product list and a cell-specific strategic recommendation text.

---

## 12. Sustainability Methodology (In-Depth)

### Framework Rationale
The Sustainability module treats **inventory waste** (unsold goods at cost price) as a financial proxy for environmental waste. The underlying logic is: goods that cannot be sold at full price, that sit in stock past their season deadline, or that accumulate zero sales represent production resources consumed without generating commercial value. While CO2 emissions or water footprint are not computed, minimising financial inventory waste is directionally aligned with minimising environmental impact.

### Data Sources
Three DB queries: Products (all, unfiltered for season data), Inventories (store-filtered), Sales (all-time, store-filtered). No new tables are created. `SeasonEnds` is a hardcoded dictionary in the service.

### Season End Date Map
```
AW24 → 2025-03-01
SS25 → 2025-09-01
AW25 → 2026-03-01
SS26 → 2026-09-01
Core → 9999-01-01 (no deadline)
```
`DaysToEnd = (SeasonEndDate - Today).TotalDays`. Negative = season has ended.

### Sell-Through Rate (STR)
**Formula:** `STR = AllTimeSoldUnits / (AllTimeSoldUnits + CurrentStock) × 100`

This is a lifetime STR, not period-bounded, because sustainability assessment requires the full lifecycle view of the product.

**Industry benchmarks applied:**
- ≥80% STR: "Healthy" (emerald).
- 60–79% STR: "Monitor" (amber).
- <60% STR: "Critical" (red).

### At-Risk Value
`AtRiskValue = CurrentStock × BaseCost`

Applied only to **seasonal products** (`IsSeasonal = true`) with `STR < 70%` AND `DaysToEnd ≤ 180` (within 6 months of season end). This represents the cost-of-goods value at risk of being marked down or written off.

### Dead Stock Detection (Sustainability Module)
`IsDeadStock = CurrentStock > 0 AND no sales in last 45 days`

Note: The 45-day threshold in `SustainabilityService` differs from the 90-day threshold used in `InventoriesController` and `NotificationService`. The sustainability module uses a more sensitive threshold for internal SKU-level analysis; the notification system uses a more conservative threshold to avoid alarm fatigue.

### Markdown Dependency
`MarkdownDependency = UnitsWithDiscount≥20% / TotalUnitsSold × 100`

The 20% discount threshold represents a meaningful commercial markdown indicating the product could not sell at or near full price. Computed per product and per category.

**Interpretation scale:**
- ≥60%: "Systematic overproduction signal — review buy quantities" (red).
- ≥40%: "High discount reliance — pricing or demand mismatch" (amber).
- ≥20%: "Moderate markdown use — acceptable for fashion retail" (yellow-green).
- <20%: "Low markdown dependency — strong demand alignment" (emerald).

### Waste Risk Score (0–100)

A composite score combining three components:

**Component 1: STR Deficit (0–50 points)**
`strComponent = (1 - STR/100) × 50`
A product with 0% STR scores 50; a product with 100% STR scores 0.

**Component 2: Time Pressure (0–30 points, seasonal products only)**
Based on `DaysToEnd`:
- ≤0 days (expired): 30 pts
- ≤30 days: 26 pts
- ≤60 days: 20 pts
- ≤90 days: 14 pts
- ≤180 days: 7 pts
- >180 days: 3 pts
Core products (no deadline): 0 pts from this component.

**Component 3: Dead Stock Penalty (0 or 20 points)**
`deadComponent = 20 if IsDeadStock (45-day threshold) else 0`

**Total:** `WasteRiskScore = min(100, strComponent + timeComponent + deadComponent)`

**Risk bands:**
- ≥65: High risk (red) — immediate action recommended.
- 35–64: Moderate risk (amber) — monitor closely.
- <35: Low risk (green) — maintain current strategy.

### Waste Risk Bubble Chart
Products are aggregated by `(Category, Season)`. The bubble chart plots:
- **X-axis:** Days to season end (urgency — negative = expired).
- **Y-axis:** Sell-Through Rate (sell-through health).
- **Bubble size:** At-risk value in € (financial exposure, proportional to radius).
- **Bubble colour:** Red/Amber/Green based on maximum waste risk score in the group.

### Sustainability Recommendations (3 tabs)

**Tab 1: Act Now (immediate intervention)**
- Critical seasonal waste risk: seasonal items with WasteRiskScore ≥65 AND DaysToEnd ≤90. Recommendation: 25–30% markdown. Recovery estimate: `AtRiskValue × 0.45`.
- Dead stock alert: aggregate count and cost value of dead stock SKUs. Recommendation: 40–60% liquidation markdown or charity donation.
- Expired season stock: seasonal items past their end date with remaining stock. Recommendation: immediate clearance exit.

**Tab 2: Monitor (pre-emptive tracking)**
- Approaching season risk: seasonal items with 90 < DaysToEnd ≤ 180 AND STR < 65%. Monitor weekly velocity.
- Moderate risk categories: WasteRiskScore 40–64 without immediate deadline pressure.

**Tab 3: Plan Better (structural buying decisions)**
- Chronic markdown dependency: categories with ≥50% markdown dependency. Recommendation: reduce next-season open-to-buy by 15–20%.
- Post-season retrospective: for recently closed seasons — if STR <80%, recommend buy reduction; if STR ≥80%, use as buying baseline for equivalent next season.

---

## 13. Notification System

### Architecture
The `NotificationService` is a scoped service that generates notifications on every request from `NotificationController.GetAll()`. There is **no notifications table** in the database. All notifications are generated by querying existing analytics tables (Sales, Inventories, Products, ForecastResults, ForecastAccuracies) and applying business rules in-memory.

### `AppNotification` Model Fields
- `Id` — Deterministic string ID (e.g., "oos-{productId}", "forecast-stock-gap")
- `Title` — Short headline (shown in the notification panel)
- `Message` — Explanatory detail text
- `Severity` — "Critical" | "Warning" | "Info" | "Success"
- `Module` — Which platform module this notification belongs to
- `ModuleUrl` — Navigation target when user clicks the notification
- `TimeLabel` — Human-readable recency ("Now", "Today", "This week")

### Notification Sources (6 generators)

**Source 1a: Out-of-Stock Products**
- Identifies all product IDs with `CurrentStock == 0`.
- For the top 3 by all-time revenue: generates an individual OOS notification.
- Severity: "Critical" if the product had sales in the last 90 days; "Warning" if not (expired seasonal product that went OOS).

**Source 1b: Low-Stock Products (above zero but at/below threshold)**
- Count of SKUs with `0 < CurrentStock ≤ MinimumStockThreshold`.
- Severity: "Warning" if ≥8 SKUs; "Info" if fewer.

**Source 2: Revenue Trend Anomaly by Category**
- Compares the last 30 days vs the prior 30 days per product category.
- Minimum category revenue threshold: €500 (to filter noise from very small categories).
- A decline >15% in a category generates a Warning or Critical notification.
- Only the single worst-performing category is surfaced per notification generation run.
- Critical if decline ≥35%; Warning otherwise.

**Source 3: Forecasting — Low Confidence**
- Checks the best Revenue model accuracy from `ForecastAccuracies`.
- If `AccuracyPercent < 60%`, generates a Warning (or Critical if <45%).

**Source 4: Forecasting — Stock Gap**
- Compares forecasted Units per `(Store, Category)` (from the latest run) against current stock per category.
- Gap threshold: `ForecastedUnits > CurrentStock` AND `gap > 20 units`.
- If ≥2 categories have gaps: generates a Critical (if ≥4 categories) or Warning notification.

**Source 5: Sustainability — Dead Stock**
- 90-day no-sale threshold (conservative, aligned with notification purpose).
- Count of SKUs with `CurrentStock > 0` and no sales in 90 days.
- Severity: "Warning" if ≥10 SKUs; "Info" otherwise.

**Source 6: Sustainability — Expired Season Stock**
- Counts `IsSeasonal = true` AND `Season ∈ ExpiredSeasons` (hardcoded list: ["AW24", "SS25", "AW25"]) AND `CurrentStock > 0`.
- Severity: "Critical" if ≥5 products; "Warning" otherwise.

### Sorting and Fallback
Notifications are sorted by severity: Critical → Warning → Info → Success. If no notifications are generated (all indicators healthy), a single "System Health Check Passed" Info notification is shown so the panel is never empty.

### Client-Side Integration
The notification bell in the top navigation bar calls `GET /Notification/GetAll` via JavaScript `fetch()` on page load. The response is rendered into a dropdown panel. The badge count shows the number of Critical and Warning notifications. "Mark all as read" is managed client-side using `localStorage` to persist read notification IDs between page loads.

---

## 14. Global Search

### Architecture
The `SearchController` provides a single `GET /Search/Autocomplete?q={term}` endpoint that returns up to 8 results as JSON. Results are filtered by a minimum query length of 2 characters.

### Search Categories and Result Types

**Pages (static, always checked first):**
Hardcoded list of 8 platform pages:
- Executive Dashboard (`/Dashboard`)
- Sales Analytics (`/Sales`)
- Inventory Intelligence (`/Inventories`)
- Store Comparison (`/StoreComparison`)
- Forecasting (`/Forecasting`)
- Smart Insights (`/SmartInsights`)
- Sustainability (`/Sustainability`)
- Store Connections (`/StoreConnection`)

Matched if `Name.Contains(term, StringComparison.OrdinalIgnoreCase)`.

**Stores (DB query, ≤3 results):**
Matches `StoreName.Contains(term)`. Displays `StoreName`, `City · StoreType` as secondary text. Navigates to `/StoreComparison`.

**Categories (DB query, ≤3 results):**
Matches distinct `Category` values. Secondary text: "Browse in Sales Analytics". Navigates to `/Sales`.

**Products (DB query, ≤4 results):**
Matches `ProductName.Contains(term)`. Secondary text: `ProductCode · Category`. Navigates to `/Inventories`.

**SKUs (DB query, ≤3 results):**
Matches `ProductCode.Contains(term)` where the ProductName does NOT also match (avoids duplicates). Secondary text: `ProductName`. Navigates to `/Inventories`.

### Navigation Behaviour
Each result carries a `url` field. JavaScript intercepts selection and performs `window.location.href = result.url`. No client-side routing is used.

### UI Implementation
The search input is in the top navigation bar (`_Layout.cshtml`). A JavaScript event listener on the input triggers `fetch()` calls with 300ms debounce. Results render as a dropdown with type badges (Page / Store / Category / Product / SKU) and secondary text. Clicking a result navigates to the associated page. The dropdown dismisses on outside click or Escape key.

---

## 15. Store Synchronisation

### FashionStoreAPI as a Data Source
The FashionStoreAPI is a standalone ASP.NET Core Web API application that owns its own database (`FashionStoreApiDb`). It exposes four read endpoints consumed by the sync service:
- `GET /api/Stores` — returns all stores as JSON.
- `GET /api/Products` — returns all products (all stores) as JSON.
- `GET /api/Inventory` — returns all inventory records as JSON.
- `GET /api/Orders?page={N}&pageSize={500}&since={ISO8601}` — returns paginated orders with optional delta filter.

The `since` parameter filters to `OrderDate >= since`, enabling incremental sync. Pagination with `pageSize=500` prevents memory issues when the dataset is large.

### Store Connections
A `StoreConnection` record defines one data source. It contains:
- `StoreApiUrl`: The base URL of the FashionStoreAPI.
- `IsActive`: Controls whether the background service polls this connection.
- `LastSyncAt`: The delta cursor. On first sync (null), the full order history is fetched. On subsequent syncs, only orders created since `LastSyncAt` are requested.

### Sync Now Behaviour
The `SyncNow` action in `StoreConnectionController` does not directly invoke the sync pipeline. It:
1. Tests connectivity by calling `GET /api/Stores` with a 5-second timeout.
2. If successful, sets `LastSyncAt = DateTime.Now` on the connection record.
3. The background service will pick up this change on its next 5-second poll and perform a full sync from the new cursor.

This means "Sync Now" is accurately described as "reset the cursor so the background service syncs on its next poll cycle".

### Background Sync Service (StoreSyncBackgroundService)
Extends `BackgroundService` and runs indefinitely while the application is alive:
1. Checks configuration. Loops: for each active `StoreConnection`, calls `SyncConnectionAsync()`.
2. Waits 5 seconds between cycles using `Task.Delay`.
3. Handles `OperationCanceledException` (application shutdown) by breaking the loop cleanly.
4. Handles `HttpRequestException` (API unreachable) and `TaskCanceledException` (10-second HTTP timeout) by logging a warning and continuing to the next connection.

The HTTP client ("StoreSync") is created from `IHttpClientFactory` with a 10-second timeout. This prevents a slow or unresponsive API from blocking the sync cycle indefinitely.

### Deduplication Logic
The deduplication check occurs in `SyncOrdersAsync()`:
1. After fetching all orders for the cycle, the incoming `ExternalOrderId` set is extracted.
2. A single query loads all existing `Sale` rows that match `(StoreConnectionId, ExternalOrderId)` in the incoming batch.
3. A `HashSet<(int orderId, int itemId)>` of `(ExternalOrderId, ExternalOrderItemId)` pairs is constructed from the existing records.
4. Before inserting each new `Sale`, the `(orderId, itemId)` pair is checked against the HashSet.
5. If found, the item is skipped (`skippedDuplicate++`). If not, it is inserted and added to the HashSet for intra-cycle deduplication.

This avoids N+1 queries by loading all existing dedup keys in a single query per cycle.

### Update Workflow (Cursor Advancement)
The `LastSyncAt` cursor is only advanced after the sync if:
- `salesAdded > 0` (sales were inserted — normal progress), OR
- `ordersDownloaded == 0` (no new orders from the API — cursor should advance to avoid re-fetching same empty window), OR
- `ordersDownloaded > 0` AND `missingProducts == 0` (orders arrived but were all genuine duplicates).

The cursor is **NOT** advanced if orders were downloaded but could not be inserted due to missing local products (`missingProducts > 0`). This ensures the same batch is retried on the next cycle after the product sync catches up.

---

## 16. Design Patterns Used

### Model-View-Controller (MVC)
The analytics platform implements the standard ASP.NET Core MVC pattern:
- **Models:** EF Core entity classes (`Sale`, `Product`, `Store`, `Inventory`, `StoreConnection`, `ForecastResult`, `ForecastAccuracy`, `ForecastFeatureImportance`).
- **ViewModels:** Separate `ViewModel` classes for complex modules (`ForecastingViewModel`, `SmartInsightsViewModel`, `SustainabilityViewModel`, `DashboardViewModel`). Simpler modules pass data via `ViewBag`.
- **Controllers:** One controller per module, responsible for query orchestration, data assembly, and view selection.
- **Views:** Razor `.cshtml` files with inline Chart.js initialisation and JavaScript event handlers.

### Dependency Injection
ASP.NET Core's built-in DI container is used throughout. Service registrations in `Program.cs`:
```csharp
builder.Services.AddDbContext<AppDbContext>(...)       // scoped (per-request)
builder.Services.AddScoped<ForecastingService>()
builder.Services.AddScoped<SmartInsightsService>()
builder.Services.AddScoped<SustainabilityService>()
builder.Services.AddScoped<NotificationService>()
builder.Services.AddHttpClient("StoreSync", ...)       // named HTTP client
builder.Services.AddHostedService<StoreSyncBackgroundService>()
```
All controllers receive `AppDbContext` via constructor injection. Service classes also receive `AppDbContext` via constructor injection.

### Service Layer Pattern
Complex business logic is extracted from controllers into dedicated service classes:
- `ForecastingService`: Python subprocess management + ViewModel assembly.
- `SmartInsightsService`: ABC/XYZ classification, SPC, Health Score, Action Board.
- `SustainabilityService`: Waste risk scoring, STR, markdown dependency, recommendations.
- `NotificationService`: Live notification generation from analytics signals.

Simple controllers (Dashboard, Sales, Inventory, StoreComparison) retain their own EF Core queries directly, because the logic is primarily query orchestration + aggregation without complex business rules. This is a deliberate choice: a service layer is justified only when it reduces controller complexity meaningfully.

### Background Service Pattern
`StoreSyncBackgroundService` implements `BackgroundService` (an ASP.NET Core abstraction over `IHostedService`). The pattern:
- `ExecuteAsync()` contains an infinite loop with `CancellationToken` checks.
- `IServiceProvider.CreateScope()` is used inside the background service to resolve scoped services (`AppDbContext`) within the long-lived singleton background service, which cannot directly inject scoped services.

### Repository-Like EF Core Usage
The platform does not implement explicit Repository or Unit of Work patterns. `AppDbContext` is used directly in controllers and services. This is the recommended approach for small-to-medium ASP.NET Core applications where the repository pattern would add abstraction without meaningful benefit. The trade-off is acknowledged: tighter coupling between business logic and EF Core, but simpler code and no redundant interface layers.

### Claims-Based Authentication
The `AccountController` builds a `ClaimsIdentity` with typed claims (`ClaimTypes.Name`, `ClaimTypes.Email`, `ClaimTypes.Role`) and custom claims (`"Initials"`, `"LoginAt"`). The `ClaimsPrincipal` is persisted in the auth cookie. Views access claims via `User.FindFirstValue(ClaimTypes.Name)` etc. This is the standard ASP.NET Core identity model applied without the full `Microsoft.AspNetCore.Identity` framework, appropriate for a single-user thesis system.

### Delta Sync Pattern
The `StoreSyncBackgroundService` implements a delta (incremental) sync pattern: instead of fetching all historical orders on every cycle, it uses the `LastSyncAt` cursor to fetch only records created after the last successful sync. This keeps the sync cycle fast even as the dataset grows.

### In-Memory Aggregation Pattern
Analytics services perform a single DB query to load raw data, then compute all derived metrics in-memory using LINQ. This trades memory usage for simplicity — avoiding complex multi-join SQL queries that are hard to debug and maintain. The data volumes for a 24-month, three-store simulation are well within the memory budget of a modern machine.

### Subprocess Integration Pattern
`ForecastingService` invokes the Python ML script via `System.Diagnostics.Process.Start()`. stdout/stderr are redirected and captured. The calling code awaits process completion with a `CancellationTokenSource`-based 5-minute timeout. This is the standard .NET pattern for integrating external command-line tools into a web application.

---

## 17. Important Design Decisions

### Decision 1: Analytics-Focused vs CRUD-Focused
**What was decided:** The platform is primarily an analytics system, not a CRUD management system. Users cannot create, edit, or delete products, stores, orders, or inventory records through the platform UI. The only write operations are: login/logout, store connection management (toggle/test/sync), and ML forecast triggering.

**Why:** Fashion retail analytics requires a separation between the transactional system (where data is created) and the analytical system (where data is read, aggregated, and interpreted). Allowing writes to the analytics database would create data consistency problems with the API-synced data. The platform's value is in surfacing insights from the data, not managing the data.

**Trade-off:** The system requires the FashionStoreAPI and data generator to be running to populate the database. Without them, the platform has no data. This is acceptable for a thesis demonstration but would require architectural reconsideration for a production deployment.

### Decision 2: Removal of CSV Import
**What was decided:** An earlier version of the system included a CSV file import feature for uploading sales data directly. This was removed in the final submission.

**Why:** The CSV import approach was replaced by the API synchronisation pipeline, which is architecturally superior. CSV import requires manual steps, is error-prone (format mismatches, encoding issues), and cannot simulate real-time data flow. The API sync pipeline is automatic, incremental, and provides a realistic demonstration of how a real BI platform connects to a transactional system.

### Decision 3: API Synchronisation as Primary Data Ingestion
**What was decided:** All data enters the analytics database exclusively through the `StoreSyncBackgroundService` pull-based HTTP synchronisation from the FashionStoreAPI. No other data ingestion mechanism exists in the final submission.

**Why:** Pull-based HTTP polling was chosen over webhooks, message queues, or database replication for simplicity. For a thesis prototype, a 5-second polling interval is practically equivalent to real-time, and the implementation complexity is dramatically lower than event-driven alternatives. The `since` parameter enables incremental sync, so polling does not degrade with dataset size.

### Decision 4: Services Created Only for Complex Business Logic
**What was decided:** Only four service classes were created beyond the built-in `BackgroundService`. Simple controllers (Dashboard, Sales, Inventory, StoreComparison) query the database and aggregate data directly in their action methods.

**Why:** The principle applied is that a service layer should reduce complexity, not add it. For analytics controllers, the logic is essentially "run queries, aggregate results, pass to view". Extracting this into a service class would add a layer of indirection without reducing the cognitive complexity of the code. The four services that do exist (Forecasting, SmartInsights, Sustainability, Notifications) all have significant business logic that genuinely benefits from encapsulation.

### Decision 5: Manual-Only Forecast Trigger
**What was decided:** Forecasts must be triggered manually by clicking "Generate Forecasts" in the UI. No automatic scheduling or background forecast generation exists.

**Why:** The Python ML script takes 30–120 seconds to run depending on the data volume. Running it automatically on a schedule would either consume significant server resources during demos or require complex scheduling infrastructure. Manual triggering gives the demonstrator full control over when forecasts are generated during a live presentation.

### Decision 6: DDL for Forecast Tables in the Analytics App
**What was decided:** The three forecast tables (`ForecastResults`, `ForecastAccuracies`, `ForecastFeatureImportances`) are created via raw `IF NOT EXISTS CREATE TABLE` DDL statements in `FashionDataAnalysisPlatform/Program.cs` at startup. The `FashionStoreAPI`, by contrast, uses EF Core migrations for all schema changes.

**Why:** The forecasting module was added to `FashionDataAnalysisPlatform` after its initial schema was deployed via `EnsureCreated()`. Because `EnsureCreated()` only creates the full schema when the database is absent (it cannot add tables to an existing database), and switching the analytics app to migrations would have required generating migration files and coordinating `dotnet ef database update` steps, idempotent raw DDL was used instead. This is a pragmatic thesis decision; in a production system, EF Core migrations would be the correct approach for both components.

**Important:** The `ModelName` column on `ForecastResults` and `ForecastAccuracies` is **not** present in the C# DDL in `Program.cs`. It is added by the Python script's `ensure_tables()` function via `ALTER TABLE` on first forecast run. These tables reach their final described schema only after at least one forecast generation has been triggered.

### Decision 7: Hardcoded Season End Dates
**What was decided:** Season end dates (AW24 → 2025-03-01, SS25 → 2025-09-01, AW25 → 2026-03-01, SS26 → 2026-09-01) are hardcoded in `SustainabilityService.SeasonEnds` and `NotificationService.ExpiredSeasons`.

**Why:** The season calendar is a business configuration that in a real system would be stored in a database table. For the thesis, the seasons are fixed and well-known, so hardcoding eliminates a configuration management dependency. The hardcoded values match the `SEASON_END_DATES` dictionary in the Python data generator, ensuring consistency across all three components.

---

## 18. Known Limitations

1. **Single-user system:** The authentication model (single `DemoUser` in `appsettings.json`) does not support multiple users, roles, or per-user data isolation. Appropriate for thesis demonstration, not for production.

2. **LocalDB dependency:** Both databases use SQL Server LocalDB, which runs as a user-instance process. It cannot run as a system service or handle concurrent connections at scale. Production deployment would require full SQL Server or Azure SQL Database.

3. **Manual forecast generation:** The ML forecast pipeline requires manual user triggering. There is no scheduled refresh, no forecast staleness indicator beyond the `LastGeneratedAt` timestamp, and no alert when forecasts become outdated.

4. **Python path dependency:** `ForecastingService` invokes Python via `Process.Start("python", ...)`. This requires Python 3 to be installed on the machine and available on the system PATH. The script also requires `scikit-learn`, `pandas`, `numpy`, `statsmodels`, and `pyodbc` to be installed in the active Python environment. No virtual environment management is implemented.

5. **ODBC Driver dependency:** The Python ML script connects to SQL Server LocalDB via `ODBC Driver 17 for SQL Server`. This driver must be installed separately on the machine. The connection string in `ml/config.json` is hardcoded to the specific LocalDB instance name; it must be updated for different environments.

6. **No real-time push:** The dashboard's "live" updates are implemented via polling (`setInterval` every 15 seconds). This is not true real-time push (WebSockets, Server-Sent Events). For a thesis, polling is sufficient.

7. **No error recovery for partial sync:** If the sync cycle fails halfway through, the `LastSyncAt` cursor may or may not have been advanced depending on which step failed. This can cause orders to be re-fetched on the next cycle, which is handled by deduplication. However, a truly robust system would use a transactional cursor update.

8. **Notification persistence is client-side only:** "Read/unread" notification state is managed via `localStorage` in the browser. Clearing browser data resets the read state. There is no server-side record of which notifications have been seen.

9. **ABC/XYZ matrix uses fixed windows:** The ABC/XYZ matrix always uses the last 24 months for ABC classification and the last 12 months for XYZ (CV) calculation, regardless of the store/date filter selected elsewhere in the platform. This is by design for analytical stability but may be unexpected for users.

10. **SyncNow does not immediately sync:** The "Sync Now" button in Store Connections updates `LastSyncAt` but does not directly invoke the sync pipeline. The actual sync happens on the background service's next 5-second poll. There is a potential delay of up to 5 seconds between clicking "Sync Now" and data appearing.

11. **Forecasting accuracy is constrained by the limited historical dataset (24 months).** Literature generally recommends at least 3 seasonal cycles for stable seasonal forecasting. The current implementation achieved 43.9% Revenue Accuracy (56.1% WMAPE) on the held-out test set, which spans the spring-to-summer seasonal transition — the most challenging forecast window given only one prior year of training data. The forecasting module should therefore be interpreted as directional decision support rather than precise demand prediction.

---

## 19. Future Improvements

1. **Multiple user accounts with role-based access:** Add ASP.NET Core Identity to support multiple users with differentiated access levels (read-only analyst, admin, store manager).

2. **Scheduled forecast generation:** Add a Quartz.NET or hosted timer service that automatically triggers forecast regeneration on a nightly or weekly schedule.

3. **WebSocket-based live updates:** Replace the 15-second polling mechanism with SignalR WebSockets to push sync events and KPI updates to the dashboard in real time.

4. **Migration-based schema management:** Replace `EnsureCreated()` + raw DDL with EF Core migrations to support proper versioned schema evolution.

5. **Forecast confidence band improvement:** Replace the MAPE-based confidence interval with proper prediction intervals derived from the Random Forest's tree ensemble variance (quantile regression or conformal prediction).

6. **Additional ML models:** Add XGBoost, LightGBM, or Prophet as additional model candidates in the comparison pipeline.

7. **Carbon footprint proxy metric:** Extend the Sustainability module to estimate CO2e emissions using inventory waste values multiplied by per-category emission factors.

8. **Store connection wizard:** Add a form for creating new store connections dynamically (currently only one connection is seeded).

9. **Export to Excel/CSV:** Add export functionality for reports, forecast data, and the At-Risk SKU table.

10. **Mobile-responsive dashboard improvements:** While Bootstrap provides basic responsiveness, the dashboard charts are not optimised for small screens. Dedicated mobile layouts for the KPI cards and charts would improve usability.

11. **Forecast feature engineering improvement:** Add category-specific seasonal patterns as additional binary features to improve accuracy for highly seasonal categories (e.g., outerwear, dresses).

12. **Notification persistence:** Store notification read/unread state server-side in a `NotificationReadState` table linked to the user account.

13. **Extend historical data window for forecasting:** The 24-month training window limits the ML model to a single seasonal cycle, which is the primary constraint on achievable accuracy. Extending the data generator's history window or operating the platform for an additional year before re-evaluation would allow the model to observe multiple seasonal cycles and learn seasonal transition patterns more reliably.

14. **Implement scheduled automatic retraining:** Add a background timer or Quartz.NET job that automatically retrains and refreshes forecasts on a nightly or weekly schedule without requiring manual user trigger.

15. **Evaluate gradient boosting and advanced ensemble methods:** Add XGBoost or LightGBM as additional model candidates in the comparison pipeline. Gradient boosting typically outperforms Random Forest on tabular data with strong feature interactions and may handle the seasonal transition problem better.

16. **Introduce statistically grounded prediction intervals:** Replace the WMAPE-based symmetric confidence band with proper quantile regression forests or conformal prediction intervals derived from the RF ensemble's tree-level variance, providing calibrated coverage guarantees.

17. **Revisit prior-year (YoY) features once additional seasonal cycles are available:** The lag_12 features were investigated and excluded from the production model because 24 months of history was insufficient for them to provide net positive value. Once 3 or more seasonal cycles are present in the training data, prior-year features should be re-evaluated as they carry theoretically sound information about year-on-year demand patterns.

---

## 20. Final Thesis Submission State

### Submission Date
2026-06-17

### What Was Delivered
The final thesis submission includes a fully functional, deployed-locally application consisting of:

**Four runnable components:**
1. `app/FashionStoreAPI` — ASP.NET Core Web API (.NET 8) running on `https://localhost:7151` with Swagger UI.
2. `app/FashionDataAnalysisPlatform` — ASP.NET Core MVC Web App (.NET 8) running on `https://localhost:7000`.
3. `data_generator/fashion_order_generator.py` — Python 3 script capable of seeding 24 months of historical orders and generating live orders.
4. `ml/fashion_forecaster.py` — Python 3 ML script (invoked by the web app via subprocess; not run independently in normal operation).

**Three stores seeded:**
- Scuffer District (Bucharest, Romania, Flagship) — ID 1 in FashionStoreApiDb.
- Scuffer Downtown (Cluj-Napoca, Romania, Urban Store) — ID 2 in FashionStoreApiDb.
- Maison Toulouse (Paris, France, Premium Boutique) — ID 3 in FashionStoreApiDb.

**100 product records seeded:**
- 62 Scuffer shared products (31 unique SKUs × 2 stores).
- 6 Scuffer District exclusives.
- 6 Scuffer Downtown exclusives.
- 26 Maison Toulouse products.

**Approximately 24 months of sales data:** Generated by the Python order generator in historical mode, covering July 2024 through June 2026 (the submission date). The database contains many thousands of sale records across all three stores.

**Analytics modules implemented and fully functional:**
1. Executive Dashboard (with 15-second live refresh via LiveMetrics JSON endpoint)
2. Sales Analytics
3. Inventory Intelligence
4. Store Comparison
5. Forecasting (Random Forest with Naive/Holt-Winters comparison, 14-feature production configuration; finalized and frozen)
6. Smart Insights (Health Score + Action Board + SPC + ABC/XYZ 3×3 matrix)
7. Sustainability Intelligence (Waste Risk Score, STR, Markdown Dependency)
8. Store Connections
9. Notification System (live, no storage, 6 signal sources)
10. Global Search (8 types, autocomplete, 8 result cap)
11. Account (Login/Logout/Demo Login/Profile)

**Python ML pipeline functional:** At least one forecast run has been executed, populating `ForecastResults`, `ForecastAccuracies`, and `ForecastFeatureImportances` tables. The system compares Naive Baseline, Holt-Winters, and Random Forest models (including an internal RF-14 vs RF-18 empirical comparison) and selects the winner by Revenue WMAPE. The production winning model is Random Forest with 14 features (Revenue Accuracy 43.9%, Revenue WMAPE 56.1%).

### Application State at Submission
- Both databases exist as LocalDB instances on the developer machine.
- The `FashionStoreApiDb` contains seeded stores, products, inventory, and all orders generated by the Python generator.
- The `FashionRetailDb` contains all synced data (stores, products, inventory snapshots, sale fact records) and at least one set of forecast results.
- The `StoreConnections` table contains one active record pointing to `https://localhost:7151`.
- The background sync service runs automatically when the MVC application starts, polling the API every 5 seconds.
- Cookie authentication is configured with `DemoUser:Email` and `DemoUser:Password` defined in `appsettings.json`.
- The platform is accessible at `https://localhost:7000` after starting both applications.

### Forecasting Module Status
- Forecasting module finalized and frozen as of submission date (2026-06-17).
- All model investigations completed: Naive Baseline, Holt-Winters, RF-14 (production), RF-18 (experimental YoY variant).
- Final production model: Random Forest with 14 features — Revenue WMAPE 56.1%, Revenue Accuracy 43.9%, Orders Accuracy 52.5%, Units Accuracy 51.2%.
- No further forecasting tuning planned before submission. The primary accuracy constraint is structural (24-month training window, single seasonal cycle) rather than implementation-related; further tuning without additional historical data was found unlikely to exceed a 5 percentage point improvement threshold.

### Technology Versions at Submission
- .NET SDK: 8.0 (LTS)
- ASP.NET Core: 8.0
- Entity Framework Core: 8.0
- Python: 3.11+
- scikit-learn: 1.4+
- pandas: 2.1+
- numpy: 1.26+
- statsmodels: 0.14+
- pyodbc: 5.0+
- Bootstrap: 5.3
- Chart.js: 4.x
- Font Awesome: 6.x
- ODBC Driver 17 for SQL Server
- SQL Server LocalDB (bundled with Visual Studio)

### Repository Structure
```
FahionDataAnalysisPlatform/          (root)
├── app/
│   ├── FashionStoreAPI/             (.NET 8 Web API)
│   │   ├── Controllers/             (Stores, Products, Inventory, Orders)
│   │   ├── Data/StoreDbContext.cs
│   │   ├── Models/                  (Store, Product, Inventory, Order, OrderItem)
│   │   ├── Dtos/CreateOrderDto.cs
│   │   └── Program.cs               (seeds 3 stores + 100 products on first run)
│   └── FashionDataAnalysisPlatform/ (.NET 8 MVC Web App)
│       ├── Controllers/             (11 controllers — see Section 5)
│       ├── Services/                (5 services: 4 business + 1 background)
│       ├── Models/                  (EF Core entity classes)
│       ├── ViewModels/              (complex module ViewModels)
│       ├── Dtos/                    (API response DTOs used by sync service)
│       ├── Data/AppDbContext.cs
│       ├── Views/                   (Razor views per controller)
│       └── Program.cs               (DI registration, EnsureCreated, forecast DDL)
├── data_generator/
│   └── fashion_order_generator.py
├── ml/
│   ├── fashion_forecaster.py
│   ├── config.json                  (ODBC connection string for LocalDB)
│   └── requirements.txt
└── PROJECT_CONTEXT.md               (this file)
```

---

*End of PROJECT_CONTEXT.md — Generated 2026-06-17 from direct source code analysis of the final thesis submission.*
