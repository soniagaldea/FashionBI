# FashionBI

FashionBI is an end-to-end Business Intelligence and decision-support platform developed as my Bachelor's thesis in Economic Informatics.

The platform is designed for multi-store fashion retail and transforms transactional, sales, inventory, and product data into actionable business insights through descriptive, diagnostic, predictive, and prescriptive analytics.

## What the platform does

FashionBI supports decision-making across the full analytics maturity cycle:

- **Descriptive analytics** – executive KPIs, revenue, profit, orders, AOV, margin and sales trends
- **Diagnostic analytics** – product, category, channel and store performance analysis
- **Predictive analytics** – 3-month demand forecasting using multiple forecasting models
- **Prescriptive analytics** – automated business recommendations, inventory risk alerts and strategic actions

## Main Features

- Executive dashboard with live KPI monitoring
- Sales and profitability analytics
- Inventory intelligence and stock-risk detection
- Multi-store benchmarking
- Machine learning demand forecasting
- Model comparison between Naive Baseline, Holt-Winters and Random Forest
- ABC/XYZ product portfolio classification
- Statistical Process Control anomaly detection
- Composite Business Health Score
- Automated strategic recommendations with estimated financial impact
- Sustainability and inventory waste analysis
- Live notifications and global search
- Multi-store API synchronization

## Machine Learning

The forecasting module predicts demand for the next 3 months at Store × Category level.

Three forecasting approaches are evaluated:

- Naive Baseline
- Holt-Winters Exponential Smoothing
- Random Forest Regression

The final model is selected based on Revenue WMAPE using a time-based holdout validation strategy.

The production Random Forest model uses engineered calendar, sales momentum and seasonal features.

## Technology Stack

### Backend
- C#
- ASP.NET Core MVC (.NET 8)
- Entity Framework Core 8
- SQL Server LocalDB

### Data & Machine Learning
- Python
- pandas
- NumPy
- scikit-learn
- statsmodels
- pyodbc

### Frontend
- Razor Views
- JavaScript
- Bootstrap 5
- Chart.js
- HTML/CSS

## Project Structure
The repository is organized into three main areas:

1. **Application Layer (`app/`)** – contains the ASP.NET Core projects that power the transactional API and the FashionBI analytics platform.
2. **Data Generator (`data_generator/`)** – Python scripts used to generate historical and live fashion retail transaction data.
3. **Machine Learning (`ml/`)** – Python-based forecasting logic used for demand prediction and model evaluation.

## Analytics Modules

### Executive Dashboard
Provides a high-level overview of network performance through revenue, profit, orders, units sold, AOV, margin and trend analysis.

<img width="1917" height="912" alt="Screenshot 2026-06-18 002529" src="https://github.com/user-attachments/assets/d2f5ebf7-b13a-4956-af2c-c1eddab3ae08" />

### Sales Analytics
Analyses revenue trends, category performance, products, channels, colours, sizes and discount efficiency.

<img width="1918" height="868" alt="Screenshot 2026-06-18 122553" src="https://github.com/user-attachments/assets/11d0a5e9-4278-4dd0-b2f6-7391c85b17d4" />

### Inventory Intelligence
Monitors stock levels, stockouts, dead stock, inventory aging, sell-through, turnover and reorder priorities.

<img width="1918" height="863" alt="Screenshot 2026-06-18 124228" src="https://github.com/user-attachments/assets/b544e0bc-a87c-4f04-bae3-2e8d5b2f380d" />

### Store Comparison
Benchmarks stores across revenue, profitability, channel mix and inventory health.

<img width="1918" height="871" alt="Screenshot 2026-06-18 113119" src="https://github.com/user-attachments/assets/0ae61c12-ebc4-4dd4-bba1-74fbfeeabd13" />

### Forecasting
Generates 3-month demand forecasts and compares multiple forecasting models using MAE, RMSE and WMAPE.

<img width="1918" height="868" alt="Screenshot 2026-06-18 132044" src="https://github.com/user-attachments/assets/ab14520c-494c-487f-9762-ddae3f74486c" />

### Smart Insights
Transforms analytical signals into prescriptive actions using: Business Health Score, ABC/XYZ portfolio analysis, Statistical Process Control and Strategic Action Board.

<img width="1918" height="908" alt="Screenshot 2026-06-18 133732" src="https://github.com/user-attachments/assets/48662141-68e7-4c26-9f3a-157cb42c8101" />

### Sustainability
Identifies inventory waste risk using sell-through rate, markdown dependency, seasonal overstock and dead stock analysis.

<img width="1918" height="912" alt="Screenshot 2026-06-18 134359" src="https://github.com/user-attachments/assets/6daaf4b8-e72f-4a19-85cf-a7f4ca7e5b56" />

## Data Simulation

A Python data generator creates 24 months of simulated fashion retail transactions using:

- seasonal demand weighting
- store-specific sales profiles
- product lifecycle logic
- basket affinity
- multiple sales channels

The generator can also run continuously to simulate real-time retail activity.

## Project Goal

The project explores how Business Intelligence, machine learning and software engineering can be integrated into a single system to support data-driven decision-making in fashion retail.

It was developed as my Bachelor's thesis:
**Fashion Retail Data Analysis Platform**
