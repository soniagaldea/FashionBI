-- Display, for each store, the total revenue, total units sold, and the number of distinct orders.
SELECT st.StoreName, SUM(s.Revenue) AS TotalRevenue, SUM(s.Quantity) AS UnitsSold, COUNT(DISTINCT s.ExternalOrderID) AS TotalOrders
FROM Sales s
INNER JOIN Stores st ON s.StoreID = st.StoreID
GROUP BY st.StoreName
ORDER BY TotalRevenue DESC;

--Display the total revenue and total profit for each product category, ordered descending by revenue.
SELECT p.Category, SUM(s.Revenue) AS TotalRevenue, SUM(s.Profit) AS TotalProfit
FROM Sales s
INNER JOIN Products p ON s.ProductID = p.ProductID
GROUP BY p.Category
ORDER BY TotalRevenue DESC;

--Display total revenue for each store and product category.
SELECT st.StoreName, p.Category, SUM(s.Revenue) AS TotalRevenue
FROM Sales s
INNER JOIN Stores st ON s.StoreID = st.StoreID
INNER JOIN Products p ON s.ProductID = p.ProductID
GROUP BY st.StoreName, p.Category
ORDER BY TotalRevenue DESC;

--Display the top 5 products by total revenue.
SELECT TOP 5 p.ProductName, SUM(s.Revenue) AS TotalRevenue
FROM Sales s
INNER JOIN Products p ON s.ProductID = p.ProductID
GROUP BY p.ProductName
ORDER BY TotalRevenue DESC;

--Display the total revenue for one specific store, for example Maison Toulouse.
SELECT st.StoreName, SUM(s.Revenue) AS TotalRevenue
FROM Sales s
INNER JOIN Stores st ON s.StoreID = st.StoreID
WHERE st.StoreName = 'Maison Toulouse'
GROUP BY st.StoreName;

--Display the total revenue by month.
SELECT MONTH(SaleDate) AS SaleMonth, YEAR(SaleDate) AS SaleYear, SUM(Revenue) AS TotalRevenue
FROM Sales
GROUP BY YEAR(SaleDate), MONTH(SaleDate)
ORDER BY SaleYear, SaleMonth;

--Display the product categories where total revenue is greater than 100,000.
SELECT p.Category, SUM(s.Revenue) AS TotalRevenue
FROM Sales s
INNER JOIN Products p ON s.ProductID = p.ProductID
GROUP BY p.Category
HAVING SUM(s.Revenue) > 100000
ORDER BY TotalRevenue DESC;

--Display the products that exist in inventory but have not had sales in the last 90 days.
SELECT p.ProductName, i.CurrentStock, MAX(s.SaleDate) AS LastSaleDate
FROM Products p
LEFT JOIN Inventories i ON p.ProductID = i.ProductID
LEFT JOIN Sales s ON p.ProductID = s.ProductID 
GROUP BY p.ProductName, s.SaleDate, i.CurrentStock
HAVING (MAX(s.SaleDate) IS NULL OR MAX(s.SaleDate) < DATEADD(DAY, -90, GETDATE()))
ORDER BY LastSaleDate DESC;

--Display the forecasted revenue, orders, and units by forecast month, store, and category.
SELECT ForecastMonth, StoreName, Category, SUM(RevenueForecast) AS TotalRevenue, SUM(OrdersForecast) AS TotalOrders, SUM(UnitsForecast) AS TotaldUnits
FROM ForecastResults
GROUP BY ForecastMonth, StoreName, Category
ORDER BY ForecastMonth, StoreName, Category;
