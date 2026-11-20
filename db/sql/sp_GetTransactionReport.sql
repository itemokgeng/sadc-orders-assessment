/*
  sp_GetTransactionReport
  -----------------------
  The AddIdempotencyOutboxAndReportProc migration deploys this procedure. This file keeps the
  source readable for review and DBA hand-off. Keep the two in sync if you change it (add a new
  migration; never edit an applied one).

  Result set 1: detail rows, one per order line item, with customer and order columns.
  Result set 2: summary (TotalOrders, GrandTotalAmount) as the brief asks.
  Result set 3: the same summary per currency. A grand total that adds ZAR to MWK means nothing
                financially, so treat result set 2 as meaningful only when filtered to a single
                currency, or to CMA currencies (ZAR/NAD/LSL/SZL), which trade at par.

  Example:
    EXEC dbo.sp_GetTransactionReport @StartDate = '2026-08-01', @EndDate = '2026-08-31';
    EXEC dbo.sp_GetTransactionReport '2026-01-01', '2026-12-31', N'Fulfilled', 'A1B2...';
*/
CREATE OR ALTER PROCEDURE dbo.sp_GetTransactionReport
    @StartDate  DATE,
    @EndDate    DATE,
    @Status     NVARCHAR(20)     = NULL,
    @CustomerId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @StartDate IS NULL OR @EndDate IS NULL OR @EndDate < @StartDate
        THROW 50001, N'Invalid range: @StartDate and @EndDate are required and @EndDate must be on or after @StartDate.', 1;

    -- The end date is inclusive but CreatedAt is datetime2, so use the half-open range
    -- [StartDate 00:00, EndDate + 1 day 00:00). It is sargable and doesn't miss 23:59:59.9999999.
    DECLARE @From DATETIME2(7) = CAST(@StartDate AS DATETIME2(7));
    DECLARE @To   DATETIME2(7) = DATEADD(DAY, 1, CAST(@EndDate AS DATETIME2(7)));

    -- Filter once so every result set describes exactly the same set of orders.
    SELECT o.Id, o.CustomerId, o.Status, o.CreatedAt, o.CurrencyCode, o.TotalAmount
    INTO #MatchingOrders
    FROM dbo.Orders AS o
    WHERE o.CreatedAt >= @From
      AND o.CreatedAt <  @To
      AND (@Status     IS NULL OR o.Status     = @Status)
      AND (@CustomerId IS NULL OR o.CustomerId = @CustomerId)
    -- Optional parameters ("catch-all query"): RECOMPILE builds a plan for the actual values, so a
    -- customer-specific call seeks IX_Orders_CustomerId_Status_CreatedAt instead of reusing a scan plan.
    OPTION (RECOMPILE);

    -- Result set 1: details
    SELECT
        c.Name         AS CustomerName,
        c.Email        AS CustomerEmail,
        c.CountryCode  AS CountryCode,
        m.Id           AS OrderId,
        m.Status       AS Status,
        m.CreatedAt    AS CreatedAt,
        m.CurrencyCode AS CurrencyCode,
        m.TotalAmount  AS TotalAmount,
        li.ProductSku  AS ProductSku,
        li.Quantity    AS Quantity,
        li.UnitPrice   AS UnitPrice
    FROM #MatchingOrders AS m
    INNER JOIN dbo.Customers      AS c  ON c.Id = m.CustomerId
    INNER JOIN dbo.OrderLineItems AS li ON li.OrderId = m.Id
    ORDER BY m.CreatedAt, m.Id, li.ProductSku;

    -- Result set 2: summary. Count orders, not line items: summing TotalAmount over the joined
    -- detail rows would multiply each order by its number of lines.
    SELECT
        COUNT(*)                         AS TotalOrders,
        COALESCE(SUM(m.TotalAmount), 0)  AS GrandTotalAmount
    FROM #MatchingOrders AS m;

    -- Result set 3: summary per currency.
    SELECT
        m.CurrencyCode,
        COUNT(*)           AS TotalOrders,
        SUM(m.TotalAmount) AS GrandTotalAmount
    FROM #MatchingOrders AS m
    GROUP BY m.CurrencyCode
    ORDER BY m.CurrencyCode;
END;
