#!/usr/bin/env python3
"""
FashionBI Demand Forecasting Script

Trains three models on last 24 months of Store × Category × Month sales:
  1. Naive Baseline  — persistence (last known value repeated for all 3 months)
  2. Holt-Winters    — exponential smoothing with additive trend + seasonality
  3. Random Forest   — multi-output regressor with lag / calendar features

Evaluates all three on the same 3-month held-out test set, selects the best
by Revenue WMAPE, and writes forecasts + all accuracy metrics to FashionRetailDb.

Usage:
    python fashion_forecaster.py

Requires:  pandas, numpy, scikit-learn, statsmodels, pyodbc
Config:    config.json (same directory) — ODBC connection string
"""

import json
import os
import sys
import warnings
import traceback
import datetime

import numpy as np
import pandas as pd
import pyodbc
from sklearn.ensemble import RandomForestRegressor
from sklearn.multioutput import MultiOutputRegressor
from sklearn.metrics import mean_absolute_error, mean_squared_error
from statsmodels.tsa.holtwinters import ExponentialSmoothing

warnings.filterwarnings("ignore")

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
# Original 14-feature set (used for before/after comparison)
FEATURE_COLS_ORIGINAL = [
    "year_num", "month_num", "quarter",
    "store_encoded", "category_encoded",
    "lag_1_revenue", "lag_3_revenue", "lag_6_revenue",
    "lag_1_orders", "lag_1_units",
    "is_holiday_month", "is_collection_launch",
    "is_summer", "is_winter",
]

# Extended 18-feature set — adds YoY lags and a binary flag that tells the
# model which rows have genuine prior-year data (months 1-12 of a 24-month
# window have no real YoY observation; the flag lets the RF branch cleanly
# between "no prior history" and "true YoY value").
FEATURE_COLS = FEATURE_COLS_ORIGINAL + [
    "lag_12_revenue", "lag_12_orders", "lag_12_units",
    "lag_12_available",
]

TARGET_COLS = ["Revenue", "Orders", "Units"]

FEATURE_DISPLAY_NAMES = {
    "year_num":             "Year",
    "month_num":            "Month",
    "quarter":              "Quarter",
    "store_encoded":        "Store",
    "category_encoded":     "Category",
    "lag_1_revenue":        "Lag-1 Revenue",
    "lag_3_revenue":        "Lag-3 Revenue",
    "lag_6_revenue":        "Lag-6 Revenue",
    "lag_1_orders":         "Lag-1 Orders",
    "lag_1_units":          "Lag-1 Units",
    "is_holiday_month":     "Holiday Month",
    "is_collection_launch": "Collection Launch",
    "is_summer":            "Summer Season",
    "is_winter":            "Winter Season",
    "lag_12_revenue":       "Lag-12 Revenue (YoY)",
    "lag_12_orders":        "Lag-12 Orders (YoY)",
    "lag_12_units":         "Lag-12 Units (YoY)",
    "lag_12_available":     "Prior-Year Data Available",
}


# 1. Config + DB connection
def load_config():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    with open(os.path.join(script_dir, "config.json")) as f:
        return json.load(f)


def get_connection(config):
    return pyodbc.connect(config["connection_string"], autocommit=False)


# 2. Fetch aggregated sales data (last 24 months)
def fetch_sales_data(conn):
    cutoff = (pd.Timestamp.today() - pd.DateOffset(months=24)).replace(day=1)
    query = """
        SELECT
            st.StoreName, st.StoreId, p.Category,
            YEAR(s.SaleDate)  AS [Year], MONTH(s.SaleDate) AS [Month],
            SUM(s.Revenue)                    AS Revenue,
            COUNT(DISTINCT s.ExternalOrderId) AS Orders,
            SUM(s.Quantity)                   AS Units
        FROM   Sales s
        INNER  JOIN Stores   st ON s.StoreId   = st.StoreId
        INNER  JOIN Products p  ON s.ProductId = p.ProductId
        WHERE  s.SaleDate >= ? AND s.StoreId IS NOT NULL AND p.Category IS NOT NULL
        GROUP  BY st.StoreName, st.StoreId, p.Category,
                  YEAR(s.SaleDate), MONTH(s.SaleDate)
        ORDER  BY st.StoreName, p.Category,
                  YEAR(s.SaleDate), MONTH(s.SaleDate)
    """
    df = pd.read_sql(query, conn, params=[cutoff.to_pydatetime()])
    for col in TARGET_COLS:
        df[col] = df[col].astype(float)
    return df


# 3. Feature engineering (adds Period, lags, calendar flags, ordinal encoding)
def build_features(df):
    df = df.copy()
    df["Period"] = pd.to_datetime({"year": df["Year"], "month": df["Month"], "day": 1})
    df = df.sort_values(["StoreName", "Category", "Period"]).reset_index(drop=True)

    def add_lags(group):
        g = group.sort_values("Period").copy()
        g["lag_1_revenue"]  = g["Revenue"].shift(1)
        g["lag_3_revenue"]  = g["Revenue"].shift(1).rolling(3, min_periods=1).mean()
        g["lag_6_revenue"]  = g["Revenue"].shift(1).rolling(6, min_periods=1).mean()
        g["lag_1_orders"]   = g["Orders"].shift(1)
        g["lag_1_units"]    = g["Units"].shift(1)
        # Same calendar month in the previous year.
        # With only 24 months of history, months 1-12 have no real prior-year observation.
        # lag_12_available=1 flags rows where the value is genuine; the RF uses this
        # to branch between the "no prior history" and "real YoY" regimes without
        # learning spurious relationships from a literal zero.
        raw_lag12           = g["Revenue"].shift(12)
        g["lag_12_available"] = raw_lag12.notna().astype(int)
        g["lag_12_revenue"] = raw_lag12.fillna(0)
        g["lag_12_orders"]  = g["Orders"].shift(12).fillna(0)
        g["lag_12_units"]   = g["Units"].shift(12).fillna(0)
        return g

    df = df.groupby(["StoreName", "Category"], group_keys=False).apply(add_lags)

    df["month_num"] = df["Period"].dt.month
    df["year_num"]  = df["Period"].dt.year
    df["quarter"]   = df["Period"].dt.quarter

    df["is_holiday_month"]     = df["month_num"].isin([11, 12, 1, 6]).astype(int)
    df["is_collection_launch"] = df["month_num"].isin([2, 3, 4, 8, 9, 10]).astype(int)
    df["is_summer"]            = df["month_num"].isin([6, 7, 8]).astype(int)
    df["is_winter"]            = df["month_num"].isin([12, 1, 2]).astype(int)

    stores     = sorted(df["StoreName"].unique())
    categories = sorted(df["Category"].unique())
    store_map  = {s: i for i, s in enumerate(stores)}
    cat_map    = {c: i for i, c in enumerate(categories)}

    df["store_encoded"]    = df["StoreName"].map(store_map)
    df["category_encoded"] = df["Category"].map(cat_map)

    return df, store_map, cat_map


# 4. Shared helpers
def _test_cutoff(df):
    return df["Period"].max() - pd.DateOffset(months=3)


def _compute_metrics(all_actuals, all_preds):
    metrics = {}
    for col in TARGET_COLS:
        actual = np.array(all_actuals[col])
        pred   = np.clip(np.array(all_preds[col]), 0, None)

        mae  = float(mean_absolute_error(actual, pred))
        rmse = float(np.sqrt(mean_squared_error(actual, pred)))

        total_actual = float(np.sum(actual))
        wmape        = float(np.sum(np.abs(actual - pred)) / total_actual * 100) if total_actual > 0 else 0.0
        accuracy     = max(0.0, round(100.0 - wmape, 2))

        metrics[col] = {
            "mae":      round(mae,   4),
            "rmse":     round(rmse,  4),
            "mape":     round(wmape, 4),
            "accuracy": accuracy,
        }
    return metrics


def _print_metrics(metrics):
    for col in TARGET_COLS:
        m = metrics[col]
        print(f"    {col:8s}  MAE={m['mae']:8.2f}  RMSE={m['rmse']:8.2f}"
              f"  WMAPE={m['mape']:5.1f}%  Acc={m['accuracy']:.1f}%")


# 5a. Naive Baseline — predict all test months = last training actual
def evaluate_naive(df):
    cutoff = _test_cutoff(df)
    all_actuals = {c: [] for c in TARGET_COLS}
    all_preds   = {c: [] for c in TARGET_COLS}

    for (_, cat), group in df.groupby(["StoreName", "Category"]):
        g_train = group[group["Period"] <= cutoff].sort_values("Period")
        g_test  = group[group["Period"] >  cutoff].sort_values("Period")
        if g_train.empty or g_test.empty:
            continue
        for col in TARGET_COLS:
            last_val = float(g_train[col].iloc[-1])
            all_actuals[col].extend(g_test[col].values.astype(float))
            all_preds[col].extend([last_val] * len(g_test))

    return _compute_metrics(all_actuals, all_preds)


# 5b. Holt-Winters — per-series exponential smoothing with fallback chain
def _fit_hw(y, _stats=None):
    """Fit Holt-Winters with additive seasonal → trend-only → SES fallback.
    Optionally accumulates tier counts into the _stats dict for diagnostics."""
    if len(y) >= 12:
        try:
            m = ExponentialSmoothing(
                y, trend="add", seasonal="add",
                seasonal_periods=12, initialization_method="estimated",
            )
            fit = m.fit(optimized=True)
            if _stats is not None:
                _stats["seasonal"] = _stats.get("seasonal", 0) + 1
            return fit
        except Exception:
            pass
    try:
        m = ExponentialSmoothing(
            y, trend="add", seasonal=None, initialization_method="estimated"
        )
        fit = m.fit(optimized=True)
        if _stats is not None:
            _stats["trend_only"] = _stats.get("trend_only", 0) + 1
        return fit
    except Exception:
        pass
    try:
        m = ExponentialSmoothing(y, trend=None, seasonal=None)
        fit = m.fit()
        if _stats is not None:
            _stats["ses"] = _stats.get("ses", 0) + 1
        return fit
    except Exception:
        if _stats is not None:
            _stats["failed"] = _stats.get("failed", 0) + 1
        return None


def evaluate_holtwinters(df):
    cutoff      = _test_cutoff(df)
    all_actuals = {c: [] for c in TARGET_COLS}
    all_preds   = {c: [] for c in TARGET_COLS}
    stats       = {}   # tier counts accumulated across all (series × target) fits
    n_fallback  = 0
    skipped     = 0

    for (_, cat), group in df.groupby(["StoreName", "Category"]):
        g_train = group[group["Period"] <= cutoff].sort_values("Period")
        g_test  = group[group["Period"] >  cutoff].sort_values("Period")
        if len(g_train) < 7 or g_test.empty:
            skipped += 1
            continue
        for col in TARGET_COLS:
            y_train = g_train[col].values.astype(float)
            fitted  = _fit_hw(y_train, _stats=stats)
            if fitted is not None:
                preds = np.clip(fitted.forecast(len(g_test)), 0, None)
            else:
                preds      = np.full(len(g_test), y_train[-1])
                n_fallback += 1
            all_actuals[col].extend(g_test[col].values.astype(float))
            all_preds[col].extend(preds)

    tier_str = "  ".join(f"{k}={v}" for k, v in sorted(stats.items()))
    print(f"    Fitting tiers — {tier_str or '(none)'}"
          f"  |  persistence fallback={n_fallback}"
          f"  |  series skipped={skipped}")
    return _compute_metrics(all_actuals, all_preds)


def train_holtwinters_full(df):
    """Train Holt-Winters on full dataset per (Store, Category) per target."""
    models = {}
    for (store_name, cat), group in df.groupby(["StoreName", "Category"]):
        if len(group) < 7:
            continue
        g = group.sort_values("Period")
        models[(store_name, cat)] = {
            col: _fit_hw(g[col].values.astype(float)) for col in TARGET_COLS
        }
    return models


# 5c. Random Forest — multi-output regressor
def evaluate_rf(df_model, feature_cols=None, verbose=True):
    if feature_cols is None:
        feature_cols = FEATURE_COLS
    cutoff = _test_cutoff(df_model)
    train  = df_model[df_model["Period"] <= cutoff]
    test   = df_model[df_model["Period"] >  cutoff]

    if train.empty or test.empty:
        print("    WARNING: Insufficient data for split — using full data.")
        train = df_model
        test  = df_model.tail(min(10, len(df_model)))

    model = MultiOutputRegressor(
        RandomForestRegressor(n_estimators=200, random_state=42, n_jobs=1),
        n_jobs=1,
    )
    try:
        model.fit(train[feature_cols], train[TARGET_COLS])
    except Exception:
        print("    ERROR during RF training:")
        traceback.print_exc()
        raise

    y_pred = model.predict(test[feature_cols])

    if verbose:
        print(f"\n    {'Store':<22} {'Category':<18} {'Period':<10}"
              f"  {'Rev_Act':>8} {'Rev_Prd':>8}  {'Ord_Act':>7} {'Ord_Prd':>7}  {'Unt_Act':>7} {'Unt_Prd':>7}")
        print("    " + "─" * 102)
        sample = test.reset_index(drop=True)
        for idx in range(min(10, len(sample))):
            row = sample.iloc[idx]
            p   = y_pred[idx]
            print(f"    {row['StoreName']:<22} {row['Category']:<18} "
                  f"{row['Period'].strftime('%Y-%m'):<10}"
                  f"  {row['Revenue']:>8.0f} {max(0,p[0]):>8.0f}"
                  f"  {row['Orders']:>7.0f} {max(0,p[1]):>7.0f}"
                  f"  {row['Units']:>7.0f} {max(0,p[2]):>7.0f}")
        print()

    all_actuals = {col: list(test[col].values)               for col in TARGET_COLS}
    all_preds   = {col: list(np.clip(y_pred[:, i], 0, None)) for i, col in enumerate(TARGET_COLS)}
    return _compute_metrics(all_actuals, all_preds)


def retrain_rf_full(df_model, feature_cols=None):
    if feature_cols is None:
        feature_cols = FEATURE_COLS
    model = MultiOutputRegressor(
        RandomForestRegressor(n_estimators=200, random_state=42, n_jobs=1),
        n_jobs=1,
    )
    try:
        model.fit(df_model[feature_cols], df_model[TARGET_COLS])
    except Exception:
        print("    ERROR during RF full retrain:")
        traceback.print_exc()
        raise
    return model


# 6. Model comparison — print table, return best model name
def compare_models(all_metrics):
    print(f"\n    {'Model':<22} {'Rev WMAPE':>10} {'Rev Acc%':>9}  "
          f"{'Ord WMAPE':>10} {'Unt WMAPE':>10}")
    print("    " + "─" * 68)

    best_name  = None
    best_wmape = float("inf")
    rows = []
    for name, metrics in all_metrics.items():
        rw = metrics["Revenue"]["mape"]
        if rw < best_wmape:
            best_wmape = rw
            best_name  = name
        rows.append((name, rw,
                     metrics["Revenue"]["accuracy"],
                     metrics["Orders"]["mape"],
                     metrics["Units"]["mape"]))

    for name, rw, ra, ow, uw in rows:
        marker = "  ◄ selected" if name == best_name else ""
        print(f"    {name:<22} {rw:>9.1f}%  {ra:>8.1f}%  {ow:>9.1f}%  {uw:>9.1f}%{marker}")
    print("    " + "─" * 68)
    print(f"\n    Winner: {best_name}  (Revenue WMAPE = {best_wmape:.1f}%)")
    return best_name


# 7. Forecast generation — one function per model family
def _lag_buffer_at_anchor(group_df, anchor_period):
    """Build 6-element lag buffer (oldest → newest) ending at anchor_period,
    zero-filling months where this Store+Category had no sales."""
    rev_buf, ord_buf, unt_buf = [], [], []
    for months_back in range(6, 0, -1):
        target = anchor_period - pd.DateOffset(months=months_back)
        row = group_df[
            (group_df["Period"].dt.year  == target.year) &
            (group_df["Period"].dt.month == target.month)
        ]
        if not row.empty:
            rev_buf.append(float(row["Revenue"].iloc[0]))
            ord_buf.append(float(row["Orders"].iloc[0]))
            unt_buf.append(float(row["Units"].iloc[0]))
        else:
            rev_buf.append(0.0); ord_buf.append(0.0); unt_buf.append(0.0)
    return rev_buf, ord_buf, unt_buf


def _future_months(anchor):
    return [anchor + pd.DateOffset(months=s) for s in range(1, 4)]


def _fc_row(store_name, store_id, cat, period, rev, orders, units, generated_at):
    return {
        "store_name":    store_name,
        "store_id":      store_id,
        "category":      cat,
        "forecast_month": datetime.date(int(period.year), int(period.month), 1),
        "revenue":        max(0.0, round(float(rev),    2)),
        "orders":         max(0,   int(round(float(orders)))),
        "units":          max(0,   int(round(float(units)))),
        "generated_at":   generated_at,
    }


def generate_forecasts_rf(df, model, store_map, cat_map, feature_cols=None):
    if feature_cols is None:
        feature_cols = FEATURE_COLS
    use_yoy = "lag_12_revenue" in feature_cols

    generated_at = datetime.datetime.now()
    anchor       = df["Period"].max()
    months       = _future_months(anchor)
    print(f"    Anchor: {anchor.strftime('%Y-%m')}"
          f"  →  {months[0].strftime('%Y-%m')} to {months[-1].strftime('%Y-%m')}")
    print(f"    Feature set: {len(feature_cols)} features"
          f"  (YoY lag features {'included' if use_yoy else 'excluded'})")

    store_id_map = df.groupby("StoreName")["StoreId"].first().to_dict()
    combos = (
        df[["StoreName"]].drop_duplicates()
        .merge(pd.DataFrame({"Category": sorted(df["Category"].unique())}), how="cross")
    )

    forecasts = []
    skipped   = 0

    for _, row in combos.iterrows():
        store_name = row["StoreName"]
        cat        = row["Category"]
        group      = df[(df["StoreName"] == store_name) & (df["Category"] == cat)].sort_values("Period")

        if len(group) < 7 or store_name not in store_map or cat not in cat_map:
            skipped += 1
            continue

        store_id  = int(store_id_map.get(store_name, 0))
        store_enc = store_map[store_name]
        cat_enc   = cat_map[cat]
        rev_buf, ord_buf, unt_buf = _lag_buffer_at_anchor(group, anchor)

        for step, next_period in enumerate(months, 1):
            m = int(next_period.month)
            y = int(next_period.year)
            q = (m - 1) // 3 + 1

            lag_1_rev = float(rev_buf[-1])
            lag_3_rev = float(np.mean(rev_buf[-3:])) if len(rev_buf) >= 3 else lag_1_rev
            lag_6_rev = float(np.mean(rev_buf[-6:])) if len(rev_buf) >= 6 else float(np.mean(rev_buf))
            lag_1_ord = float(ord_buf[-1])
            lag_1_unt = float(unt_buf[-1])

            base_row = [
                y, m, q, store_enc, cat_enc,
                lag_1_rev, lag_3_rev, lag_6_rev, lag_1_ord, lag_1_unt,
                1 if m in [11, 12, 1, 6]         else 0,
                1 if m in [2, 3, 4, 8, 9, 10]    else 0,
                1 if m in [6, 7, 8]               else 0,
                1 if m in [12, 1, 2]              else 0,
            ]

            if use_yoy:
                # lag_12: same calendar month one year prior — always from actual data.
                # For a 3-month horizon (+1/+2/+3) the lookback is 9-11 months into the
                # 24-month training window, so it is always available.
                yr_back = next_period - pd.DateOffset(months=12)
                yr_row  = group[
                    (group["Period"].dt.year  == yr_back.year) &
                    (group["Period"].dt.month == yr_back.month)
                ]
                lag_12_rev = float(yr_row["Revenue"].iloc[0]) if not yr_row.empty else 0.0
                lag_12_ord = float(yr_row["Orders"].iloc[0])  if not yr_row.empty else 0.0
                lag_12_unt = float(yr_row["Units"].iloc[0])   if not yr_row.empty else 0.0
                base_row += [lag_12_rev, lag_12_ord, lag_12_unt,
                             1]  # lag_12_available — always 1 for forecast months

            X = pd.DataFrame([base_row], columns=feature_cols)

            pred  = model.predict(X)[0]
            rev_f = max(0.0, round(float(pred[0]), 2))
            ord_f = max(0, int(round(float(pred[1]))))
            unt_f = max(0, int(round(float(pred[2]))))
            rev_buf.append(rev_f); ord_buf.append(float(ord_f)); unt_buf.append(float(unt_f))
            forecasts.append(_fc_row(store_name, store_id, cat, next_period, rev_f, ord_f, unt_f, generated_at))

    if skipped:
        print(f"    Skipped {skipped} combination(s) with insufficient history.")
    return forecasts, generated_at


def generate_forecasts_hw(df, hw_models):
    generated_at = datetime.datetime.now()
    anchor       = df["Period"].max()
    months       = _future_months(anchor)
    print(f"    Anchor: {anchor.strftime('%Y-%m')}"
          f"  →  {months[0].strftime('%Y-%m')} to {months[-1].strftime('%Y-%m')}")

    store_id_map = df.groupby("StoreName")["StoreId"].first().to_dict()
    forecasts    = []

    for (store_name, cat), col_models in hw_models.items():
        store_id = int(store_id_map.get(store_name, 0))
        preds    = {}

        for col in TARGET_COLS:
            fitted = col_models.get(col)
            if fitted is not None:
                try:
                    preds[col] = list(np.clip(fitted.forecast(3), 0, None))
                    continue
                except Exception:
                    pass
            # Fallback: use last actual value
            g = df[(df["StoreName"] == store_name) & (df["Category"] == cat)].sort_values("Period")
            last_val = float(g[col].iloc[-1]) if not g.empty else 0.0
            preds[col] = [last_val, last_val, last_val]

        for i, next_period in enumerate(months):
            forecasts.append(_fc_row(
                store_name, store_id, cat, next_period,
                preds["Revenue"][i], preds["Orders"][i], preds["Units"][i],
                generated_at,
            ))

    return forecasts, generated_at


def generate_forecasts_naive(df):
    generated_at = datetime.datetime.now()
    anchor       = df["Period"].max()
    months       = _future_months(anchor)
    print(f"    Anchor: {anchor.strftime('%Y-%m')}"
          f"  →  {months[0].strftime('%Y-%m')} to {months[-1].strftime('%Y-%m')}")

    store_id_map = df.groupby("StoreName")["StoreId"].first().to_dict()
    forecasts    = []

    for (store_name, cat), group in df.groupby(["StoreName", "Category"]):
        if len(group) < 7:
            continue
        store_id = int(store_id_map.get(store_name, 0))
        g_sorted = group[group["Period"] <= anchor].sort_values("Period")
        if g_sorted.empty:
            continue
        last_rev = float(g_sorted["Revenue"].iloc[-1])
        last_ord = float(g_sorted["Orders"].iloc[-1])
        last_unt = float(g_sorted["Units"].iloc[-1])

        for next_period in months:
            forecasts.append(_fc_row(store_name, store_id, cat, next_period,
                                     last_rev, last_ord, last_unt, generated_at))

    return forecasts, generated_at


# 8. Ensure tables exist — idempotent, adds ModelName to existing tables
def ensure_tables(conn):
    cursor = conn.cursor()

    cursor.execute("""
        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ForecastResults' AND xtype='U')
        CREATE TABLE ForecastResults (
            ForecastResultId  INT IDENTITY(1,1) PRIMARY KEY NOT NULL,
            StoreId           INT           NULL,
            StoreName         NVARCHAR(100) NOT NULL,
            Category          NVARCHAR(100) NOT NULL,
            ForecastMonth     DATETIME2     NOT NULL,
            RevenueForecast   DECIMAL(18,2) NOT NULL,
            OrdersForecast    INT           NOT NULL,
            UnitsForecast     INT           NOT NULL,
            GeneratedAt       DATETIME2     NOT NULL,
            ModelName         NVARCHAR(50)  NULL
        )
    """)
    cursor.execute("""
        IF NOT EXISTS (
            SELECT * FROM sys.columns
            WHERE object_id = OBJECT_ID('ForecastResults') AND name = 'ModelName'
        )
        ALTER TABLE ForecastResults ADD ModelName NVARCHAR(50) NULL
    """)

    cursor.execute("""
        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ForecastAccuracies' AND xtype='U')
        CREATE TABLE ForecastAccuracies (
            ForecastAccuracyId  INT IDENTITY(1,1) PRIMARY KEY NOT NULL,
            ModelName           NVARCHAR(50)  NULL,
            Target              NVARCHAR(20)  NOT NULL,
            MAE                 DECIMAL(18,4) NOT NULL,
            RMSE                DECIMAL(18,4) NOT NULL,
            MAPE                DECIMAL(18,4) NOT NULL,
            AccuracyPercent     DECIMAL(5,2)  NOT NULL,
            GeneratedAt         DATETIME2     NOT NULL
        )
    """)
    cursor.execute("""
        IF NOT EXISTS (
            SELECT * FROM sys.columns
            WHERE object_id = OBJECT_ID('ForecastAccuracies') AND name = 'ModelName'
        )
        ALTER TABLE ForecastAccuracies ADD ModelName NVARCHAR(50) NULL
    """)

    cursor.execute("""
        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ForecastFeatureImportances' AND xtype='U')
        CREATE TABLE ForecastFeatureImportances (
            ForecastFeatureImportanceId INT IDENTITY(1,1) PRIMARY KEY NOT NULL,
            FeatureName                 NVARCHAR(100) NOT NULL,
            Importance                  DECIMAL(10,6) NOT NULL,
            Target                      NVARCHAR(20)  NOT NULL,
            GeneratedAt                 DATETIME2     NOT NULL
        )
    """)

    conn.commit()
    cursor.close()
    print("    Tables verified.")


# 9. Write results to database
def write_results(conn, forecasts, all_metrics, feature_importance, generated_at, best_model_name):
    cursor = conn.cursor()

    cursor.execute("DELETE FROM ForecastResults")
    cursor.execute("DELETE FROM ForecastAccuracies")
    cursor.execute("DELETE FROM ForecastFeatureImportances")

    for f in forecasts:
        cursor.execute(
            """INSERT INTO ForecastResults
               (StoreName, StoreId, Category, ForecastMonth,
                RevenueForecast, OrdersForecast, UnitsForecast, GeneratedAt, ModelName)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            f["store_name"], f["store_id"], f["category"], f["forecast_month"],
            f["revenue"], f["orders"], f["units"], f["generated_at"], best_model_name,
        )

    # Store accuracy for all 3 models so the UI can show the comparison table
    for model_name, metrics in all_metrics.items():
        for target, m in metrics.items():
            cursor.execute(
                """INSERT INTO ForecastAccuracies
                   (ModelName, Target, MAE, RMSE, MAPE, AccuracyPercent, GeneratedAt)
                   VALUES (?, ?, ?, ?, ?, ?, ?)""",
                model_name, target, m["mae"], m["rmse"], m["mape"], m["accuracy"], generated_at,
            )

    for feat_name, importance_val in feature_importance:
        display_name = FEATURE_DISPLAY_NAMES.get(feat_name, feat_name)
        cursor.execute(
            """INSERT INTO ForecastFeatureImportances
               (FeatureName, Importance, Target, GeneratedAt)
               VALUES (?, ?, ?, ?)""",
            display_name, round(float(importance_val), 6), "Revenue", generated_at,
        )

    conn.commit()
    cursor.close()


# Main
def main():
    print("=" * 65)
    print("  FashionBI — Naive / Holt-Winters / Random Forest Comparison")
    print("=" * 65)

    print("\n[1/8] Loading configuration...")
    config = load_config()

    print("[2/8] Connecting to FashionRetailDb...")
    conn = get_connection(config)

    print("[3/8] Fetching last 24 months of sales data...")
    df_raw = fetch_sales_data(conn)
    print(f"      {len(df_raw)} store-category-month rows retrieved.")
    if df_raw.empty:
        print("ERROR: No sales data found. Run the data generator and sync first.")
        sys.exit(1)

    df, store_map, cat_map = build_features(df_raw)
    df_model = df.dropna(subset=FEATURE_COLS).copy()
    print(f"      {len(df_model)} rows available for modelling (after lag computation).")
    if len(df_model) < 20:
        print("ERROR: Insufficient training data. Each store-category needs 7+ months.")
        sys.exit(1)

    # Evaluate all 3 models on the held-out test set (last 3 months)
    # Naive and HW use df (all rows; no lag-feature requirement).
    # RF uses df_model (lag NaN rows dropped).
    # All share the same test cutoff: df["Period"].max() - 3 months.
    print("\n[4/8] Evaluating models on held-out test set (last 3 months)...")

    print("\n  Naive Baseline:")
    naive_metrics = evaluate_naive(df)
    _print_metrics(naive_metrics)

    print("\n  Holt-Winters Exponential Smoothing:")
    hw_metrics = evaluate_holtwinters(df)
    _print_metrics(hw_metrics)

    print("\n  Random Forest (14 features — calendar + momentum, no YoY):")
    rf_metrics_14 = evaluate_rf(df_model, FEATURE_COLS_ORIGINAL, verbose=False)
    _print_metrics(rf_metrics_14)

    print("\n  Random Forest (18 features — YoY lags + availability flag):")
    rf_metrics_18 = evaluate_rf(df_model)
    _print_metrics(rf_metrics_18)

    # Select the better RF configuration
    # With only 24 months of history, the lag-12 YoY features may be net
    # harmful (first 12 months have no real prior-year data). The pipeline
    # evaluates both and uses whichever achieves lower Revenue WMAPE.
    wmape_14 = rf_metrics_14["Revenue"]["mape"]
    wmape_18 = rf_metrics_18["Revenue"]["mape"]
    if wmape_14 <= wmape_18:
        rf_metrics      = rf_metrics_14
        rf_feature_cols = FEATURE_COLS_ORIGINAL
        rf_label        = "RF-14 (no YoY)"
    else:
        rf_metrics      = rf_metrics_18
        rf_feature_cols = FEATURE_COLS
        rf_label        = "RF-18 (with YoY)"
    print(f"\n    RF feature-set comparison (Revenue WMAPE):")
    print(f"    14 features (no YoY)  : {wmape_14:.1f}%")
    print(f"    18 features (YoY+flag): {wmape_18:.1f}%")
    print(f"    Selected for production: {rf_label}")

    print("\n[5/8] Comparing models...")
    all_metrics = {
        "Naive Baseline": naive_metrics,
        "Holt-Winters":   hw_metrics,
        "Random Forest":  rf_metrics,
    }
    best_model_name = compare_models(all_metrics)

    # Retrain best model on full dataset
    print(f"\n[6/8] Retraining {best_model_name} on full dataset...")
    final_rf     = None
    hw_full      = None
    feat_imp     = []

    if best_model_name == "Random Forest":
        final_rf = retrain_rf_full(df_model, rf_feature_cols)
        raw_imp  = final_rf.estimators_[0].feature_importances_
        feat_imp = sorted(zip(rf_feature_cols, raw_imp), key=lambda x: x[1], reverse=True)
        print(f"\n    Top feature importances (Revenue model, {len(rf_feature_cols)} features):")
        print(f"    {'Feature':<26} {'Importance':>9}")
        print("    " + "─" * 38)
        for feat_name, imp in feat_imp[:10]:
            display = FEATURE_DISPLAY_NAMES.get(feat_name, feat_name)
            bar     = "▓" * max(1, int(imp * 400))
            print(f"    {display:<26} {imp * 100:5.1f}%  {bar}")
    elif best_model_name == "Holt-Winters":
        hw_full = train_holtwinters_full(df)
    # Naive needs no retraining

    # Generate 3-month forecasts using the winning model
    print(f"\n[7/8] Generating 3-month forecasts ({best_model_name})...")
    if best_model_name == "Random Forest":
        forecasts, generated_at = generate_forecasts_rf(
            df, final_rf, store_map, cat_map, rf_feature_cols
        )
    elif best_model_name == "Holt-Winters":
        forecasts, generated_at = generate_forecasts_hw(df, hw_full)
    else:
        forecasts, generated_at = generate_forecasts_naive(df)
    print(f"    {len(forecasts)} forecast rows generated.")

    if not forecasts:
        print("WARNING: No forecasts generated. Ensure 7+ months of history per store-category.")
        sys.exit(1)

    print("\n[8/8] Ensuring tables and writing results to FashionRetailDb...")
    ensure_tables(conn)
    write_results(conn, forecasts, all_metrics, feat_imp, generated_at, best_model_name)
    conn.close()

    print("\n" + "=" * 65)
    print(f"  Complete. Selected model: {best_model_name}")
    print("=" * 65)
    sys.exit(0)


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as e:
        print(f"\nFATAL ERROR: {e}")
        traceback.print_exc()
        sys.exit(1)
