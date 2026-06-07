"""
Fashion Retail Data Generation Engine
=====================================

Final clean version for the thesis project.

Works with the current FashionStoreAPI:
- GET  /api/Stores
- GET  /api/Products
- POST /api/Orders

Usage:
    pip install requests

Historical data generation:
    python fashion_order_generator.py --historical

Live demo generation:
    python fashion_order_generator.py

Important:
- Run FashionStoreAPI before this script.
- For historical generation, stop FashionDataAnalysisPlatform first.
- After historical generation finishes, start FashionDataAnalysisPlatform so the BackgroundService can sync the data.
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
LIVE_INTERVAL_SECONDS = 10

HISTORICAL_MONTHS_BACK = 18
ORDERS_PER_DAY_MIN = 12
ORDERS_PER_DAY_MAX = 35

REQUEST_DELAY_SECONDS = 0.015

WEEKEND_UPLIFT_FACTOR = 1.45
BASKET_ATTACH_CHANCE = 0.35

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
            "Online": 0.55,
            "MobileApp": 0.35,
            "Physical": 0.10,
        },
    },
    "Scuffer Downtown": {
        "brand": "Scuffer",
        "profile": "Secondary urban store",
        "volume_weight": 1.10,
        "discount_modifier": 0.90,
        "channel_weights": {
            "Online": 0.35,
            "MobileApp": 0.25,
            "Physical": 0.40,
        },
    },
    "Maison Toulouse": {
        "brand": "Maison Toulouse",
        "profile": "Premium old-money boutique",
        "volume_weight": 0.40,
        "discount_modifier": 0.25,
        "channel_weights": {
            "Online": 0.40,
            "MobileApp": 0.20,
            "Physical": 0.40,
        },
    },
}

DEFAULT_STORE_PROFILE = {
    "brand": "Unknown",
    "profile": "Generic retail store",
    "volume_weight": 1.00,
    "discount_modifier": 1.00,
    "channel_weights": {
        "Online": 0.50,
        "MobileApp": 0.30,
        "Physical": 0.20,
    },
}

# =============================================================================
# CATEGORY SEASONALITY
# =============================================================================

SEASONAL_CURVES = {
    # January to December
    "Tops":        [3.0, 4.0, 6.0, 8.0, 9.0, 10.0, 10.0, 9.0, 7.0, 5.0, 4.0, 3.0],
    "Trousers":    [7.0, 7.0, 8.0, 8.0, 8.0,  7.0,  6.0, 7.0, 8.0, 9.0, 8.0, 7.0],
    "Outerwear":   [10.0, 9.0, 5.0, 1.5, 0.5, 0.1,  0.1, 0.5, 3.0, 7.0, 9.0, 10.0],
    "Accessories": [4.0, 4.0, 5.0, 6.0, 7.0,  8.0,  9.0, 9.0, 7.0, 6.0, 8.0, 10.0],
    "Dresses":     [1.0, 1.0, 3.0, 6.0, 8.5, 10.0, 10.0, 9.0, 4.5, 2.0, 1.5, 1.0],
    "Blazers":     [6.0, 6.0, 8.0, 9.0, 7.0,  4.0,  3.0, 4.0, 8.0, 9.0, 7.0, 6.0],
}

MARKET_BASKET_AFFINITY = {
    "Trousers": "Accessories",
    "Tops": "Trousers",
    "Outerwear": "Accessories",
    "Dresses": "Accessories",
    "Blazers": "Trousers",
}

SIZES_STANDARD = ["XS", "S", "M", "L", "XL"]
SIZES_TROUSERS = ["26", "28", "30", "32", "34", "36"]
SIZES_ACCESSORIES = ["ONE-SIZE"]

COLORS_WARM_SEASON = ["Cream", "Beige", "Sage", "Coral", "Yellow", "Floral-Pink"]
COLORS_COLD_SEASON = ["Black", "Navy", "Charcoal", "Grey", "Burgundy", "Camel"]

# =============================================================================
# PRODUCT METADATA
# =============================================================================

@dataclass
class ProductMeta:
    code: str
    category: str
    brand: str
    velocity: str


def get_product_meta(product: dict) -> ProductMeta:
    code = str(product.get("productCode", "P000"))
    category = str(product.get("category", "Unknown"))
    brand = str(product.get("brand", ""))

    try:
        number = int(code.replace("P", ""))
    except ValueError:
        number = 0

    if brand == "Maison Toulouse":
        # Premium goods move slower, but with higher value.
        velocity = "slow" if number % 3 == 0 else "medium"
    else:
        # Streetwear has stronger fast-fashion velocity.
        if number % 4 == 0:
            velocity = "fast"
        elif number % 4 == 3:
            velocity = "slow"
        else:
            velocity = "medium"

    return ProductMeta(
        code=code,
        category=category,
        brand=brand,
        velocity=velocity,
    )


def get_sizes_for_category(category: str) -> List[str]:
    if category == "Trousers":
        return SIZES_TROUSERS
    if category == "Accessories":
        return SIZES_ACCESSORIES
    return SIZES_STANDARD


# =============================================================================
# PROBABILISTIC BUSINESS RULES
# =============================================================================

def get_demand_weight(meta: ProductMeta, date: datetime) -> float:
    month_index = date.month - 1
    category_curve = SEASONAL_CURVES.get(meta.category, [5.0] * 12)
    base_weight = category_curve[month_index]

    velocity_multiplier = {
        "fast": 2.20,
        "medium": 1.00,
        "slow": 0.30,
    }.get(meta.velocity, 1.0)

    premium_multiplier = 0.70 if meta.brand == "Maison Toulouse" else 1.0

    return base_weight * velocity_multiplier * premium_multiplier


def get_discount_percent(meta: ProductMeta, date: datetime, discount_modifier: float) -> int:
    month = date.month
    day = date.day

    # Premium brand discounts are intentionally rare.
    if meta.brand == "Maison Toulouse":
        if meta.velocity == "slow" and random.random() < 0.06:
            return random.choice([10, 15])
        if month == 11 and 24 <= day <= 30 and random.random() < 0.08:
            return random.choice([10, 15])
        return 0

    # Scuffer discount behaviour: seasonal campaigns and aggressive retail promotions.
    roll = random.random() * discount_modifier

    if month == 1 and meta.category in {"Outerwear", "Blazers"} and roll > 0.40:
        return random.choice([20, 30, 50])

    if month == 7 and meta.category in {"Tops", "Dresses"} and roll > 0.40:
        return random.choice([20, 30, 40])

    if month == 11 and 22 <= day <= 30 and roll > 0.30:
        return random.choice([20, 25, 30, 40])

    if meta.velocity == "slow" and random.random() < 0.18:
        return random.choice([10, 15, 20])

    # Occasional light campaigns.
    if random.random() < 0.05:
        return random.choice([10, 15])

    return 0


def get_seasonal_color(base_color: Optional[str], date: datetime, brand: str = "Scuffer") -> str:
    if base_color is None or str(base_color).strip() == "":
        base_color = "Black"

    warm_season = date.month in {4, 5, 6, 7, 8, 9}

    # Premium luxury palette
    if brand == "Maison Toulouse":
        if warm_season and random.random() < 0.40:
            return random.choice(["Cream", "Beige", "Camel"])

        if not warm_season and random.random() < 0.40:
            return random.choice(["Black", "Navy", "Charcoal", "Grey"])

        return str(base_color)

    # Streetwear palette
    if warm_season and random.random() < 0.65:
        return random.choice(COLORS_WARM_SEASON)

    if not warm_season and random.random() < 0.65:
        return random.choice(COLORS_COLD_SEASON)

    return str(base_color)


def choose_sales_channel(profile: dict) -> str:
    channel_weights = profile["channel_weights"]
    channels = list(channel_weights.keys())
    weights = list(channel_weights.values())
    return random.choices(channels, weights=weights, k=1)[0]


# =============================================================================
# API CLIENT
# =============================================================================

class FashionStoreApiClient:
    def __init__(self, base_url: str):
        self.base_url = base_url.rstrip("/")
        self.session = requests.Session()

    def get_json(self, path: str) -> list:
        url = f"{self.base_url}{path}"
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

def get_store_name(store: dict) -> str:
    return str(store.get("storeName") or store.get("name") or "")


def get_store_id(store: dict) -> int:
    return int(store.get("storeId") or store.get("id"))


def get_store_profile(store: dict) -> dict:
    return STORE_PROFILES_BY_NAME.get(get_store_name(store), DEFAULT_STORE_PROFILE)


def select_store(stores: list) -> dict:
    weights = [get_store_profile(store)["volume_weight"] for store in stores]
    return random.choices(stores, weights=weights, k=1)[0]


def select_anchor_product(store_products: list, date: datetime) -> dict:
    weights = []

    for product in store_products:
        meta = get_product_meta(product)
        weight = get_demand_weight(meta, date)

        # Avoid selecting products that the API reports as currently out of stock.
        if int(product.get("currentStock", 1) or 0) <= 0:
            weight *= 0.05

        weights.append(max(weight, 0.01))

    return random.choices(store_products, weights=weights, k=1)[0]


def construct_basket(store_products: list, anchor_product: dict, date: datetime, discount_modifier: float) -> List[dict]:
    selected_products = [anchor_product]
    anchor_category = anchor_product.get("category")
    target_category = MARKET_BASKET_AFFINITY.get(anchor_category)

    if target_category and random.random() < BASKET_ATTACH_CHANCE:
        matches = [
            product for product in store_products
            if product.get("category") == target_category
            and product.get("productCode") != anchor_product.get("productCode")
        ]

        if matches:
            selected_products.append(random.choice(matches))

    items = []

    for product in selected_products:
        meta = get_product_meta(product)
        category = product.get("category", "")
        quantity = 1

        if category == "Accessories" and meta.brand == "Scuffer" and random.random() < 0.20:
            quantity = 2
        elif meta.brand == "Scuffer" and random.random() < 0.08:
            quantity = 2

        items.append({
            "productCode": product["productCode"],
            "quantity": quantity,
            "size": random.choice(get_sizes_for_category(category)),
            "color": get_seasonal_color(
                product.get("color"),
                date,
                meta.brand
            ),
            "discountPercent": get_discount_percent(meta, date, discount_modifier),
        })

    return items


def generate_order_payload(stores: list, products: list, target_date: datetime) -> Optional[dict]:
    store = select_store(stores)
    store_id = get_store_id(store)
    profile = get_store_profile(store)

    store_products = [product for product in products if int(product.get("storeId", -1)) == store_id]

    if not store_products:
        print(f"[WARN] No products found for storeId={store_id}")
        return None

    anchor = select_anchor_product(store_products, target_date)
    items = construct_basket(store_products, anchor, target_date, profile["discount_modifier"])

    if not items:
        return None

    payload = {
        "storeId": store_id,
        "customerId": random.randint(10000, 99999),
        "salesChannel": choose_sales_channel(profile),
        "orderDate": target_date.strftime("%Y-%m-%dT%H:%M:%S"),
        "items": items,
    }

    return payload


def send_order(client: FashionStoreApiClient, payload: dict) -> Optional[dict]:
    return client.post_json("/api/Orders", payload)


# =============================================================================
# EXECUTION MODES
# =============================================================================

def run_historical_mode(client: FashionStoreApiClient, stores: list, products: list) -> None:
    end_date = datetime.now()
    start_date = end_date - timedelta(days=int(HISTORICAL_MONTHS_BACK * 30.4))

    current_date = start_date
    successful_orders = 0
    failed_orders = 0

    print(f"Historical generation started: {start_date.date()} -> {end_date.date()}")

    while current_date <= end_date:
        daily_quota = random.randint(ORDERS_PER_DAY_MIN, ORDERS_PER_DAY_MAX)

        if current_date.weekday() >= 5:
            daily_quota = int(daily_quota * WEEKEND_UPLIFT_FACTOR)

        print(f"[{current_date.date()}] generating {daily_quota} orders...")

        for _ in range(daily_quota):
            hour = random.randint(9, 21)
            minute = random.randint(0, 59)
            second = random.randint(0, 59)

            target_time = datetime(
                year=current_date.year,
                month=current_date.month,
                day=current_date.day,
                hour=random.randint(9, 21),
                minute=random.randint(0, 59),
                second=random.randint(0, 59)
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
    print("Start FashionDataAnalysisPlatform now and let the sync service import the historical data.")
    print("=" * 80)


def run_live_mode(client: FashionStoreApiClient, stores: list, products: list) -> None:
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

    is_historical = "--historical" in sys.argv

    print("=" * 80)
    print("Fashion Retail Data Generation Engine")
    print(f"Target API: {API_BASE_URL}")
    print(f"Mode: {'Historical' if is_historical else 'Live'}")
    print("=" * 80)

    client = FashionStoreApiClient(API_BASE_URL)

    try:
        stores = client.get_json("/api/Stores")
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
        print("Important: FashionDataAnalysisPlatform should be stopped during historical generation.")
        run_historical_mode(client, stores, products)
    else:
        run_live_mode(client, stores, products)


if __name__ == "__main__":
    main()
