/*
  Assessment SQL (ANSWERS.md questions 12-20), collected so it can run against the seeded database.
  Generated from the ```sql blocks in ANSWERS.md, so edit it there.
  Commented out: Q14 and Q16 (the migrations already apply them) and Q19 (it creates demo objects and needs a staging table).
*/
SET NOCOUNT ON;
GO

-- ========== Q12. Pagination query with separate COUNT ==========
-- 12. A customer's orders, newest first, one page at a time, plus the total count.
-- Parameterised through sp_executesql (what ADO.NET/EF send), so the plan is cached and reused and injection is impossible.
DECLARE @CustomerId UNIQUEIDENTIFIER = (SELECT TOP (1) Id FROM dbo.Customers ORDER BY Name);
DECLARE @Offset INT = 0;      -- (page - 1) * pageSize
DECLARE @PageSize INT = 20;   -- capped at 100 by the API

EXEC sp_executesql
    N'SELECT o.Id, o.Status, o.CreatedAt, o.TotalAmount, o.CurrencyCode
      FROM dbo.Orders AS o
      WHERE o.CustomerId = @CustomerId
      ORDER BY o.CreatedAt DESC, o.Id DESC          -- Id tie-breaker = stable pages
      OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

      SELECT COUNT(*) AS TotalCount
      FROM dbo.Orders
      WHERE CustomerId = @CustomerId;',
    N'@CustomerId UNIQUEIDENTIFIER, @Offset INT, @PageSize INT',
    @CustomerId = @CustomerId, @Offset = @Offset, @PageSize = @PageSize;
GO

-- ========== Q13. Top 10 spenders over the last 90 days, including customers with no orders ==========
-- 13. Top 10 customers by spend in the last 90 days. Customers with no orders show 0.
DECLARE @Since DATETIME2 = DATEADD(DAY, -90, SYSUTCDATETIME());

SELECT TOP (10)
    c.Id,
    c.Name,
    c.CountryCode,
    COUNT(o.Id)                     AS OrderCount,   -- COUNT(column) ignores the NULLs from the LEFT JOIN
    COALESCE(SUM(o.TotalAmount), 0) AS TotalSpend
FROM dbo.Customers AS c
LEFT JOIN dbo.Orders AS o
       ON o.CustomerId = c.Id
      AND o.CreatedAt >= @Since          -- in the ON clause: in WHERE it would turn the LEFT JOIN into an INNER JOIN
      AND o.Status <> N'Cancelled'       -- cancelled orders aren't spend
GROUP BY c.Id, c.Name, c.CountryCode
ORDER BY TotalSpend DESC, c.Name;
GO

-- ========== Q13. Top 10 spenders over the last 90 days, including customers with no orders ==========
-- 13b. The same ranking in a common currency (ZAR) using a rates table (e.g. loaded daily from a rates feed).
--      CMA currencies (NAD, LSL, SZL) convert 1:1 to ZAR by definition.
-- CREATE TABLE dbo.FxRates (RateDate DATE, CurrencyCode CHAR(3), ZarPerUnit DECIMAL(18,6), PRIMARY KEY (RateDate, CurrencyCode));
-- SELECT TOP (10) c.Id, c.Name, COALESCE(SUM(o.TotalAmount * r.ZarPerUnit), 0) AS TotalSpendZar
-- FROM dbo.Customers c
-- LEFT JOIN dbo.Orders o ON o.CustomerId = c.Id AND o.CreatedAt >= @Since AND o.Status <> N'Cancelled'
-- LEFT JOIN dbo.FxRates r ON r.CurrencyCode = o.CurrencyCode AND r.RateDate = CAST(o.CreatedAt AS DATE)
-- GROUP BY c.Id, c.Name ORDER BY TotalSpendZar DESC;
GO

-- ========== Q14. Indexing Orders for (CustomerId, Status, CreatedAt) ==========
-- -- 14. The listing index, as created by the InitialCreate migration.
-- CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_Status_CreatedAt
--     ON dbo.Orders (CustomerId, Status, CreatedAt)
--     INCLUDE (CurrencyCode, TotalAmount);
GO

-- ========== Q15. Detecting and removing key lookups ==========
-- 15. Orders + line items for a customer and status. Look at the actual execution plan.
DECLARE @Cust UNIQUEIDENTIFIER = (SELECT TOP (1) CustomerId FROM dbo.Orders GROUP BY CustomerId ORDER BY COUNT(*) DESC);
SET STATISTICS IO ON;
SELECT o.Id, o.CreatedAt, o.TotalAmount, li.ProductSku, li.Quantity, li.UnitPrice
FROM dbo.Orders AS o
JOIN dbo.OrderLineItems AS li ON li.OrderId = o.Id
WHERE o.CustomerId = @Cust AND o.Status = N'Fulfilled';
SET STATISTICS IO OFF;
GO

-- ========== Q15. Detecting and removing key lookups ==========
CREATE NONCLUSTERED INDEX IX_OrderLineItems_OrderId_Covering
    ON dbo.OrderLineItems (OrderId)
    INCLUDE (ProductSku, Quantity, UnitPrice)
    WITH (ONLINE = ON);   -- Enterprise/Azure SQL; drop the old IX_OrderLineItems_OrderId afterwards (it's now redundant)
GO

-- ========== Q16. Optimistic concurrency with rowversion ==========
-- ALTER TABLE dbo.Orders ADD RowVersion ROWVERSION NOT NULL;
GO

-- ========== Q17. Deadlocks: a reader/writer scenario and a mitigation ==========
-- 17. RCSI: readers see the last committed row version and take no shared locks.
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
-- Check: SELECT name, is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();
GO

-- ========== Q18. Running total per customer with a window function ==========
-- 18. Running total of spend per customer, in order of CreatedAt.
SELECT
    o.CustomerId,
    o.Id          AS OrderId,
    o.CreatedAt,
    o.CurrencyCode,
    o.TotalAmount,
    SUM(o.TotalAmount) OVER (
        PARTITION BY o.CustomerId
        ORDER BY o.CreatedAt, o.Id
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningTotal
FROM dbo.Orders AS o
WHERE o.Status <> N'Cancelled'
ORDER BY o.CustomerId, o.CreatedAt, o.Id;
GO

-- ========== Q19. Partitioning millions of orders, and archiving with partition switching ==========
-- -- 19. Monthly partitions (shown on a separate demo table; repartitioning dbo.Orders is a planned migration).
-- CREATE PARTITION FUNCTION pf_OrdersMonthly (DATETIME2(7))
--     AS RANGE RIGHT FOR VALUES ('2026-01-01', '2026-02-01', '2026-03-01' /* ... one boundary per month, created ahead of time */);

-- CREATE PARTITION SCHEME ps_OrdersMonthly
--     AS PARTITION pf_OrdersMonthly ALL TO ([PRIMARY]);   -- or one filegroup per year: old ones on cheaper storage / read-only

-- -- The partitioning column must be part of every unique index, including the clustered one:
-- -- PK becomes NONCLUSTERED (Id, CreatedAt); cluster on (CreatedAt, Id).
-- CREATE TABLE dbo.OrdersPartitioned (
--     Id           UNIQUEIDENTIFIER NOT NULL,
--     CustomerId   UNIQUEIDENTIFIER NOT NULL,
--     Status       NVARCHAR(20)     NOT NULL,
--     CreatedAt    DATETIME2(7)     NOT NULL,
--     CurrencyCode CHAR(3)          NOT NULL,
--     TotalAmount  DECIMAL(18,2)    NOT NULL,
--     CONSTRAINT PK_OrdersPartitioned PRIMARY KEY NONCLUSTERED (Id, CreatedAt) ON ps_OrdersMonthly (CreatedAt)
-- );
-- CREATE CLUSTERED INDEX CIX_OrdersPartitioned_CreatedAt_Id ON dbo.OrdersPartitioned (CreatedAt, Id) ON ps_OrdersMonthly (CreatedAt);
-- CREATE INDEX IX_OrdersPartitioned_CustomerId_Status_CreatedAt
--     ON dbo.OrdersPartitioned (CustomerId, Status, CreatedAt) INCLUDE (CurrencyCode, TotalAmount)
--     ON ps_OrdersMonthly (CreatedAt);   -- aligned index, needed for SWITCH
GO

-- ========== Q19. Partitioning millions of orders, and archiving with partition switching ==========
-- -- 1) Ahead of time: always keep an empty partition at the leading edge.
-- ALTER PARTITION SCHEME ps_OrdersMonthly NEXT USED [PRIMARY];
-- ALTER PARTITION FUNCTION pf_OrdersMonthly() SPLIT RANGE ('2026-04-01');   -- splitting an EMPTY range is metadata-only

-- -- 2) Archive the oldest month: switch it out into an identical, empty staging table
-- --    (same columns, same indexes, same filegroup, plus a CHECK constraint matching the range).
-- -- CREATE TABLE dbo.OrdersArchive_Staging (...same definition...) ON [PRIMARY];
-- ALTER TABLE dbo.OrdersPartitioned SWITCH PARTITION 1 TO dbo.OrdersArchive_Staging;   -- metadata-only: milliseconds, not hours

-- -- 3) Remove the now-empty boundary.
-- ALTER PARTITION FUNCTION pf_OrdersMonthly() MERGE RANGE ('2026-01-01');

-- -- 4) Move the staging rows to cold storage at leisure (archive DB, BCP to Blob, or SWITCH into a partitioned archive table), then truncate staging.
GO

-- ========== Q20. sp_GetTransactionReport ==========
-- 20. Usage
EXEC dbo.sp_GetTransactionReport @StartDate = '2026-08-01', @EndDate = '2026-11-30';
EXEC dbo.sp_GetTransactionReport @StartDate = '2026-01-01', @EndDate = '2026-12-31', @Status = N'Fulfilled';
GO
