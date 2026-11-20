using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Context.Migrations
{
    internal class _20261116100000_AddIdempotencyOutboxAndReportProc
    {
    }
}

#nullable disable

namespace DataLayer.Context.Migrations
{
    /// <summary>
    /// Evolution 2, additive only: new tables for the reliability features plus the reporting proc.
    /// - IdempotencyRecords: Idempotency-Key responses for PUT /api/orders/{id}/status
    /// - OutboxMessages: transactional outbox for OrderCreated
    /// - ProcessedMessages: idempotent-consumer (dedup) table used by the worker
    /// - dbo.sp_GetTransactionReport (source: db/sql/sp_GetTransactionReport.sql)
    /// The proc text is copied in here on purpose. A migration must stay the same forever after it
    /// has been applied, so it must not read a file that might change later.
    /// </summary>
    [DbContext(typeof(OrdersDbContext))]
    [Migration("20261116100000_AddIdempotencyOutboxAndReportProc")]
    public partial class AddIdempotencyOutboxAndReportProc : Migration
    {
        private const string CreateTransactionReportProc = @"
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
    -- Optional parameters (""catch-all query""): RECOMPILE builds a plan for the actual values, so a
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
";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "int", nullable: false),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedMessages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMessages", x => x.MessageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_CreatedAt",
                table: "IdempotencyRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OccurredAt",
                table: "OutboxMessages",
                column: "OccurredAt",
                filter: "[ProcessedAt] IS NULL");

            migrationBuilder.Sql(CreateTransactionReportProc);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.sp_GetTransactionReport;");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "ProcessedMessages");
        }
    }
}
