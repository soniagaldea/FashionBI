"""
Fashion Retail Data Generator

Simulation window: July 2024 → June 2026 (24 months).

Works with FashionStoreAPI:
- GET  /api/Stores
- GET  /api/Products   (must return launchDate, isSeasonal, season fields)
- POST /api/Orders

Usage:
    pip install requests

Historical data generation (run once, API must be running):
    python fashion_order_generator.py --historical

Live demo generation:
    python fashion_order_generator.py

Important:
- Start FashionStoreAPI before running this script.
- For historical generation, stop FashionDataAnalysisPlatform first so the
  BackgroundService does not interfere. Start it again after generation finishes.
"""

import random
import sys
import time
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import Dict, List, Optional

import requests
import urllib3

urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

# =============================================================================
# CONFIGURATION
# =============================================================================

API_BASE_URL = "https://localhost:7151"

RANDOM_SEED = 42
LIVE_INTERVAL_SECONDS = 15

# Historical simulation starts here (matches Core product LaunchDate in seed).
HISTORICAL_START = datetime(2024, 7, 1)

ORDERS_PER_DAY_MIN = 12
ORDERS_PER_DAY_MAX = 35

REQUEST_DELAY_SECONDS = 0.015

WEEKEND_UPLIFT_FACTOR = 1.45
BASKET_ATTACH_CHANCE  = 0.15   # fallback for categories not in BASKET_ATTACH_RATES

# Unique customers per store. Weighted selection creates a Pareto loyalty pattern:
# top ~10% of customers generate ~40% of orders.
CUSTOMER_POOL_SIZES = {
    "Scuffer District": 1200,
    "Scuffer Downtown":  800,
    "Maison Toulouse":   300,
}
# Non-overlapping ID bands — store origin of each customer is identifiable in analytics.
CUSTOMER_ID_RANGES = {
    "Scuffer District": (10000, 29999),
    "Scuffer Downtown": (30000, 49999),
    "Maison Toulouse":  (50000, 69999),
}

# =============================================================================
# STORE PROFILES
# =============================================================================

STORE_PROFILES_BY_NAME = {
    "Scuffer District": {
        "brand": "Scuffer",
        "profile": "Flagship urban streetwear store",
        "volume_weight": 1.60,
        "discount_modifier": 1.00,
        "channel_weights": {
            "Online":    0.55,
            "MobileApp": 0.35,
            "Physical":  0.10,
        },
    },
    "Scuffer Downtown": {
        "brand": "Scuffer",
        "profile": "Secondary urban store",
        "volume_weight": 1.10,
        "discount_modifier": 0.90,
        "channel_weights": {
            "Online":    0.35,
            "MobileApp": 0.25,
            "Physical":  0.40,
        },
    },
    "Maison Toulouse": {
        "brand": "Maison Toulouse",
        "profile": "Premium old-money boutique",
        "volume_weight": 0.40,
        "discount_modifier": 0.25,
        "channel_weights": {
            "Online":    0.40,
            "MobileApp": 0.20,
            "Physical":  0.40,
        },
    },
}

DEFAULT_STORE_PROFILE = {
    "brand": "Unknown",
    "volume_weight": 1.00,
    "discount_modifier": 1.00,
    "channel_weights": {"Online": 0.50, "MobileApp": 0.30, "Physical": 0.20},
}

# =============================================================================
# CATEGORY SEASONALITY
# =============================================================================

# Monthly demand weights (January = index 0, December = index 11).
SEASONAL_CURVES = {
    "Tops":        [3.0, 4.0, 6.0, 8.0, 9.0, 10.0, 10.0,  9.0,  7.0,  5.0,  4.0,  3.0],
    "Knitwear":    [8.0, 7.0, 5.0, 3.0, 1.5,  0.5,  0.3,  0.5,  3.0,  7.0,  9.0, 10.0],
    "Trousers":    [7.0, 7.0, 8.0, 8.0, 8.0,  7.0,  6.0,  7.0,  8.0,  9.0,  8.0,  7.0],
    "Outerwear":   [10.0, 9.0, 5.0, 1.5, 0.5, 0.1,  0.1,  0.5,  3.0,  7.0,  9.0, 10.0],
    "Accessories": [3.0, 3.0, 3.5, 4.0, 4.5,  5.0,  5.0,  5.0,  4.5,  4.0,  4.0,  4.5],
    "Dresses":     [1.0, 1.0, 3.0, 6.0, 8.5, 10.0, 10.0,  9.0,  4.5,  2.0,  1.5,  1.0],
    "Blazers":     [6.0, 6.0, 8.0, 9.0, 7.0,  4.0,  3.0,  4.0,  8.0,  9.0,  7.0,  6.0],
}

# Category cross-sell pairs used to build multi-item baskets.
MARKET_BASKET_AFFINITY = {
    "Trousers":  "Accessories",
    "Tops":      "Trousers",
    "Knitwear":  "Trousers",
    "Outerwear": "Accessories",
    "Dresses":   "Accessories",
    "Blazers":   "Trousers",
}

# Per-category attach probabilities (override the global BASKET_ATTACH_CHANCE).
# Accessories is only a minor add-on; Tops → Trousers is the strongest pairing.
BASKET_ATTACH_RATES = {
    "Tops":      0.25,   # outfit completion: top + trouser
    "Trousers":  0.12,   # occasional accessory add-on
    "Knitwear":  0.15,   # knitwear often paired with trousers
    "Outerwear": 0.10,   # coat + scarf/bag — uncommon impulse
    "Dresses":   0.08,   # dress + accessory — rare at purchase
    "Blazers":   0.20,   # tailoring set: blazer + trouser
}

# =============================================================================
# VELOCITY AND LIFECYCLE TABLES
# =============================================================================

# Demand velocity mapped by ProductCode prefix (first 4 characters).
# Controls the velocity multiplier in get_demand_weight().
VELOCITY_BY_PREFIX = {
    "SC-T": "medium",
    "SC-K": "medium",
    "SC-P": "medium",
    "SC-O": "medium",
    "SC-A": "medium",  # Accessories are supportive, not dominant
    "SC-D": "medium",
    "MT-D": "slow",
    "MT-T": "slow",
    "MT-P": "slow",
    "MT-B": "slow",
    "MT-K": "slow",
    "MT-O": "slow",
    "MT-A": "low_medium",  # MT accessories slower than Scuffer but above boutique clothing
}

# Date after which IsSeasonal products are no longer sold.
# Core products are not in this table and never retire.
SEASON_END_DATES: Dict[str, datetime] = {
    "AW24": datetime(2025, 3, 1),
    "SS25": datetime(2025, 9, 1),
    "AW25": datetime(2026, 3, 1),
    "SS26": datetime(2026, 9, 1),
}

SIZES_STANDARD    = ["XS", "S", "M", "L", "XL"]
SIZES_TROUSERS    = ["26", "28", "30", "32", "34", "36"]
SIZES_ACCESSORIES = ["ONE-SIZE"]

# Bell-curve size weights. Standard peaks at M; trousers peak at 30/32/34.
SIZE_WEIGHTS_STANDARD = [0.08, 0.18, 0.34, 0.28, 0.12]           # XS   S    M    L   XL
SIZE_WEIGHTS_TROUSERS = [0.06, 0.10, 0.24, 0.30, 0.22, 0.08]     # 26   28   30   32  34  36

COLORS_WARM_SEASON = ["Cream", "Beige", "Sage", "Coral", "Yellow", "Floral-Pink"]
COLORS_COLD_SEASON = ["Black", "Navy", "Charcoal", "Grey", "Burgundy", "Camel"]

# =============================================================================
# PRODUCT METADATA
# =============================================================================

@dataclass
class ProductMeta:
    code:        str
    category:    str
    brand:       str
    velocity:    str
    is_seasonal: bool
    season:      str
    launch_date: Optional[datetime]


def get_product_meta(product: dict) -> ProductMeta:
    code     = str(product.get("productCode", ""))
    category = str(product.get("category", "Unknown"))
    brand    = str(product.get("brand", ""))
    is_seasonal = bool(product.get("isSeasonal", False))
    season   = str(product.get("season") or "Core")

    launch_raw  = product.get("launchDate")
    launch_date = None
    if launch_raw and isinstance(launch_raw, str):
        try:
            launch_date = datetime.fromisoformat(launch_raw.rstrip("Z"))
        except ValueError:
            pass

    # Velocity from the first 4 characters of the product code (e.g. "SC-T", "MT-O").
    prefix   = code[:4] if len(code) >= 4 else code
    velocity = VELOCITY_BY_PREFIX.get(prefix, "medium")

    return ProductMeta(
        code=code,
        category=category,
        brand=brand,
        velocity=velocity,
        is_seasonal=is_seasonal,
        season=season,
        launch_date=launch_date,
    )


def get_sizes_for_category(category: str) -> List[str]:
    if category == "Trousers":
        return SIZES_TROUSERS
    if category == "Accessories":
        return SIZES_ACCESSORIES
    return SIZES_STANDARD


def pick_size(category: str) -> str:
    """Weighted size selection — bell-curve peaking at M/L for apparel and 30/32/34 for trousers."""
    if category == "Accessories":
        return "ONE-SIZE"
    if category == "Trousers":
        return random.choices(SIZES_TROUSERS, weights=SIZE_WEIGHTS_TROUSERS, k=1)[0]
    return random.choices(SIZES_STANDARD, weights=SIZE_WEIGHTS_STANDARD, k=1)[0]


# =============================================================================
# LIFECYCLE HELPERS
# =============================================================================

def is_product_active(product: dict, date: datetime) -> bool:
    """Returns False if the product has not launched yet or its season has ended."""
    meta = get_product_meta(product)

    # Do not sell before launch date.
    if meta.launch_date and date < meta.launch_date:
        return False

    # Seasonal products retire at their season end date.
    if meta.is_seasonal and meta.season in SEASON_END_DATES:
        return date < SEASON_END_DATES[meta.season]

    return True


# =============================================================================
# PROBABILISTIC BUSINESS RULES
# =============================================================================

def get_demand_weight(meta: ProductMeta, date: datetime) -> float:
    month_index    = date.month - 1
    category_curve = SEASONAL_CURVES.get(meta.category, [5.0] * 12)
    base_weight    = category_curve[month_index]

    velocity_multiplier = {"fast": 2.20, "medium": 1.00, "low_medium": 0.60, "slow": 0.30}.get(meta.velocity, 1.0)
    premium_multiplier  = 0.70 if meta.brand == "Maison Toulouse" else 1.0

    weight = base_weight * velocity_multiplier * premium_multiplier

    # Lifecycle modifiers for seasonal products only.
    if meta.is_seasonal and meta.launch_date and meta.season in SEASON_END_DATES:
        days_since_launch = (date - meta.launch_date).days

        if 0 <= days_since_launch <= 30:
            # New drop boost: demand spikes in the first month after launch.
            weight *= 1.5
        else:
            # Demand decay: weight falls linearly to 30% of peak by season end.
            season_end  = SEASON_END_DATES[meta.season]
            total_days  = max((season_end - meta.launch_date).days, 1)
            days_elapsed = (date - meta.launch_date).days
            decay = max(0.30, 1.0 - 0.70 * (days_elapsed / total_days))
            weight *= decay

    return max(weight, 0.01)


def get_discount_percent(meta: ProductMeta, date: datetime, discount_modifier: float) -> int:
    month = date.month
    day   = date.day

    # Priority 1: end-of-season clearance — 8 weeks before the season retires.
    if meta.is_seasonal and meta.season in SEASON_END_DATES:
        days_to_end = (SEASON_END_DATES[meta.season] - date).days
        if 0 < days_to_end <= 56:
            if meta.brand == "Maison Toulouse":
                # Luxury brand: rare, light markdowns only.
                if random.random() < 0.55:
                    return random.choice([10, 15])
                return 0
            else:
                # Scuffer: aggressive clearance to clear stock.
                if random.random() < 0.70 * discount_modifier:
                    return random.choice([20, 30, 40, 50])

    # Premium brand: discounts are intentionally rare outside clearance windows.
    if meta.brand == "Maison Toulouse":
        if meta.velocity == "slow" and random.random() < 0.04:
            return random.choice([10, 15])
        if month == 11 and 24 <= day <= 30 and random.random() < 0.06:
            return random.choice([10, 15])
        return 0

    # Scuffer: Black Friday campaign.
    roll = random.random() * discount_modifier
    if month == 11 and 22 <= day <= 30 and roll > 0.30:
        return random.choice([20, 25, 30, 40])

    # Scuffer: slow-mover markdown.
    if meta.velocity == "slow" and random.random() < 0.15:
        return random.choice([10, 15, 20])

    # Scuffer: light occasional campaign.
    if random.random() < 0.05:
        return random.choice([10, 15])

    return 0


def get_seasonal_color(base_color: Optional[str], date: datetime, brand: str = "Scuffer") -> str:
    if base_color is None or str(base_color).strip() == "":
        base_color = "Black"

    warm_season = date.month in {4, 5, 6, 7, 8, 9}

    if brand == "Maison Toulouse":
        if warm_season and random.random() < 0.40:
            return random.choice(["Cream", "Beige", "Camel"])
        if not warm_season and random.random() < 0.40:
            return random.choice(["Black", "Navy", "Charcoal", "Grey"])
        return str(base_color)

    if warm_season and random.random() < 0.65:
        return random.choice(COLORS_WARM_SEASON)
    if not warm_season and random.random() < 0.65:
        return random.choice(COLORS_COLD_SEASON)

    return str(base_color)


def choose_sales_channel(profile: dict) -> str:
    channel_weights = profile["channel_weights"]
    channels = list(channel_weights.keys())
    weights  = list(channel_weights.values())
    return random.choices(channels, weights=weights, k=1)[0]


def get_daily_volume_multiplier(date: datetime) -> float:
    """Returns an order-volume multiplier for campaign and seasonal traffic events."""
    m, d = date.month, date.day
    if m == 11 and 22 <= d <= 30:                    return 2.50  # Black Friday / Cyber Week
    if m == 12 and 10 <= d <= 24:                    return 1.80  # Pre-Christmas
    if m == 1  and  2 <= d <= 14:                    return 1.40  # January clearance
    if (m == 8 and d >= 15) or (m == 9 and d <= 7): return 1.50  # AW collection launch
    if (m == 2 and d >=  1) or (m == 3 and d <= 7): return 1.50  # SS collection launch
    return 1.0


# =============================================================================
# API CLIENT
# =============================================================================

class FashionStoreApiClient:
    def __init__(self, base_url: str):
        self.base_url = base_url.rstrip("/")
        self.session  = requests.Session()

    def get_json(self, path: str) -> list:
        url      = f"{self.base_url}{path}"
        response = self.session.get(url, timeout=60, verify=False)
        response.raise_for_status()
        return response.json()

    def post_json(self, path: str, body: dict) -> Optional[dict]:
        url = f"{self.base_url}{path}"
        try:
            response = self.session.post(url, json=body, timeout=60, verify=False)
            if response.status_code not in (200, 201):
                print(f"[WARN] POST {path} failed: {response.status_code} | {response.text[:250]}")
                return None
            return response.json()
        except requests.RequestException as exc:
            print(f"[ERROR] POST {path} exception: {exc}")
            return None


# =============================================================================
# ORDER GENERATION
# =============================================================================

# Populated by _build_customer_pools() in main() — maps store_name → (ids, weights).
_CUSTOMER_POOLS: Dict[str, tuple] = {}


def _build_customer_pools() -> Dict[str, tuple]:
    """
    Pre-generate a fixed customer pool per store with power-law weighted selection.
    Weight for rank r = 1 / r^0.8, so rank-1 customer is ~5× as likely as rank-100.
    Top ~10% of customers generate ~40% of orders (standard retail Pareto pattern).
    """
    pools: Dict[str, tuple] = {}
    for store_name, size in CUSTOMER_POOL_SIZES.items():
        lo, hi  = CUSTOMER_ID_RANGES[store_name]
        ids     = random.sample(range(lo, hi + 1), size)
        weights = [1.0 / (rank ** 0.8) for rank in range(1, size + 1)]
        pools[store_name] = (ids, weights)
    return pools


def get_store_name(store: dict) -> str:
    return str(store.get("storeName") or store.get("name") or "")

def get_store_id(store: dict) -> int:
    return int(store.get("storeId") or store.get("id"))

def get_store_profile(store: dict) -> dict:
    return STORE_PROFILES_BY_NAME.get(get_store_name(store), DEFAULT_STORE_PROFILE)


def select_store(stores: list) -> dict:
    weights = [get_store_profile(s)["volume_weight"] for s in stores]
    return random.choices(stores, weights=weights, k=1)[0]


def select_anchor_product(store_products: list, date: datetime) -> dict:
    weights = []
    for product in store_products:
        meta   = get_product_meta(product)
        weight = get_demand_weight(meta, date)
        if int(product.get("currentStock", 1) or 0) <= 0:
            weight *= 0.05
        weights.append(max(weight, 0.01))
    return random.choices(store_products, weights=weights, k=1)[0]


def construct_basket(
    store_products: list,
    anchor_product: dict,
    date: datetime,
    discount_modifier: float
) -> List[dict]:
    selected = [anchor_product]
    anchor_category = anchor_product.get("category")
    target_category = MARKET_BASKET_AFFINITY.get(anchor_category)

    attach_rate = BASKET_ATTACH_RATES.get(anchor_category, BASKET_ATTACH_CHANCE)
    if target_category and random.random() < attach_rate:
        matches = [
            p for p in store_products
            if p.get("category") == target_category
            and p.get("productCode") != anchor_product.get("productCode")
        ]
        if matches:
            selected.append(random.choice(matches))

    items = []
    for product in selected:
        meta     = get_product_meta(product)
        category = product.get("category", "")
        quantity = 1

        if category == "Accessories" and meta.brand == "Scuffer" and random.random() < 0.05:
            quantity = 2
        elif meta.brand == "Scuffer" and random.random() < 0.08:
            quantity = 2

        items.append({
            "productCode":     product["productCode"],
            "quantity":        quantity,
            "size":            pick_size(category),
            "color":           get_seasonal_color(product.get("color"), date, meta.brand),
            "discountPercent": get_discount_percent(meta, date, discount_modifier),
        })

    return items


def generate_order_payload(
    stores: list, products: list, target_date: datetime
) -> Optional[dict]:
    store    = select_store(stores)
    store_id = get_store_id(store)
    profile  = get_store_profile(store)

    # Only consider products that belong to this store AND are currently active
    # (launched and not past their season end date).
    store_products = [
        p for p in products
        if int(p.get("storeId", -1)) == store_id
        and is_product_active(p, target_date)
    ]

    if not store_products:
        print(f"[WARN] No active products for storeId={store_id} on {target_date.date()}")
        return None

    anchor = select_anchor_product(store_products, target_date)
    items  = construct_basket(store_products, anchor, target_date, profile["discount_modifier"])

    if not items:
        return None

    store_name  = get_store_name(store)
    pool        = _CUSTOMER_POOLS.get(store_name)
    customer_id = random.choices(pool[0], weights=pool[1], k=1)[0] if pool \
                  else random.randint(10000, 99999)

    return {
        "storeId":      store_id,
        "customerId":   customer_id,
        "salesChannel": choose_sales_channel(profile),
        "orderDate":    target_date.strftime("%Y-%m-%dT%H:%M:%S"),
        "items":        items,
    }


def send_order(client: FashionStoreApiClient, payload: dict) -> Optional[dict]:
    return client.post_json("/api/Orders", payload)


# =============================================================================
# EXECUTION MODES
# =============================================================================

def run_historical_mode(
    client: FashionStoreApiClient, stores: list, products: list
) -> None:
    start_date = HISTORICAL_START
    end_date   = datetime.now()

    current_date      = start_date
    successful_orders = 0
    failed_orders     = 0
    last_refresh: Optional[tuple] = None   # (year, month) of last product fetch

    print(f"Historical generation: {start_date.date()} → {end_date.date()}")

    while current_date <= end_date:
        # Re-fetch products once per simulated month so CurrentStock values stay fresh.
        current_ym = (current_date.year, current_date.month)
        if current_ym != last_refresh:
            try:
                fresh = client.get_json("/api/Products")
                if fresh:
                    products     = fresh
                    last_refresh = current_ym
                    print(f"[{current_date.date()}] Products refreshed ({len(products)} items)")
            except Exception as exc:
                print(f"[WARN] Product refresh failed: {exc}")

        daily_quota = random.randint(ORDERS_PER_DAY_MIN, ORDERS_PER_DAY_MAX)

        if current_date.weekday() >= 5:  # Saturday / Sunday
            daily_quota = int(daily_quota * WEEKEND_UPLIFT_FACTOR)

        campaign = get_daily_volume_multiplier(current_date)
        if campaign > 1.0:
            daily_quota = int(daily_quota * campaign)

        print(f"[{current_date.date()}] generating {daily_quota} orders...")

        for _ in range(daily_quota):
            target_time = datetime(
                year=current_date.year,
                month=current_date.month,
                day=current_date.day,
                hour=random.randint(9, 21),
                minute=random.randint(0, 59),
                second=random.randint(0, 59),
            )

            payload = generate_order_payload(stores, products, target_time)
            if payload is None:
                failed_orders += 1
                continue

            result = send_order(client, payload)
            if result:
                successful_orders += 1
            else:
                failed_orders += 1

            if REQUEST_DELAY_SECONDS > 0:
                time.sleep(REQUEST_DELAY_SECONDS)

        current_date += timedelta(days=1)

    print("=" * 80)
    print("Historical generation completed.")
    print(f"Successful orders: {successful_orders}")
    print(f"Failed orders:     {failed_orders}")
    print("Start FashionDataAnalysisPlatform now — the sync service will import the data.")
    print("=" * 80)


def run_live_mode(
    client: FashionStoreApiClient, stores: list, products: list
) -> None:
    successful_orders = 0

    print(f"Live mode started. New order every {LIVE_INTERVAL_SECONDS} seconds.")
    print("Press CTRL + C to stop.")

    while True:
        payload = generate_order_payload(stores, products, datetime.now())

        if payload is not None:
            result = send_order(client, payload)
            if result:
                successful_orders += 1
                print(
                    f"[LIVE] Order #{result.get('orderId')} | "
                    f"Store {result.get('storeId')} | "
                    f"Items {result.get('itemsCount')} | "
                    f"Total €{result.get('totalAmount')} | "
                    f"Count {successful_orders}"
                )

        time.sleep(LIVE_INTERVAL_SECONDS)


# =============================================================================
# MAIN
# =============================================================================

def main() -> None:
    random.seed(RANDOM_SEED)
    _CUSTOMER_POOLS.update(_build_customer_pools())

    is_historical = "--historical" in sys.argv

    print("=" * 80)
    print("Fashion Retail Data Generation Engine")
    print(f"Target API: {API_BASE_URL}")
    print(f"Mode: {'Historical (Jul 2024 → now)' if is_historical else 'Live'}")
    print("=" * 80)

    client = FashionStoreApiClient(API_BASE_URL)

    try:
        stores   = client.get_json("/api/Stores")
        products = client.get_json("/api/Products")
    except Exception as exc:
        print(f"[CRITICAL] Could not connect to FashionStoreAPI: {exc}")
        return

    if not stores:
        print("[ABORT] No stores found in API.")
        return
    if not products:
        print("[ABORT] No products found in API.")
        return

    print(f"Loaded stores:   {len(stores)}")
    print(f"Loaded products: {len(products)}")

    if is_historical:
        print("Important: stop FashionDataAnalysisPlatform before running historical generation.")
        run_historical_mode(client, stores, products)
    else:
        run_live_mode(client, stores, products)


if __name__ == "__main__":
    main()
