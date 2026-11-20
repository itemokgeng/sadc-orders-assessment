# ANSWERS

These are my answers to the assessment questions. Where something is built in this repo, I mention the file so you can check it in the code. The SQL for questions 12 to 19 is also in [`db/sql/assessment-queries.sql`](db/sql/assessment-queries.sql), and you can run it against the seeded database. The stored procedure for question 20 is in [`db/sql/sp_GetTransactionReport.sql`](db/sql/sp_GetTransactionReport.sql). A migration deploys it.

---

## General questions

### 1. async/await for I/O-bound work, and when to use Task.Run

For I/O work (database calls, HTTP calls, file access) I use async from the endpoint all the way down: `ToListAsync`, `SaveChangesAsync`, `SendAsync`. While a request is waiting for the database, its thread goes back to the thread pool and can serve another request. That is why a small pool can handle a lot of traffic.

The things I avoid:

- Blocking on async code with `.Result`, `.Wait()` or `GetAwaiter().GetResult()`. This holds a thread for the whole wait. Under load the pool runs out of threads, and the API gets slow even though the CPU is not busy (thread pool starvation).
- `async void`. An exception in it can crash the process. I return `Task` instead. The only exception is event handlers.
- Running two queries at the same time on one `DbContext` (for example with `Task.WhenAll`). A `DbContext` only supports one operation at a time. I either run them one after the other or use separate scopes, like `OutboxDispatcher` does.

I also pass the `CancellationToken` through every layer. If the client disconnects, the database query is cancelled too. `ConfigureAwait(false)` is not needed in ASP.NET Core app code because there is no `SynchronizationContext`, but it still matters in library code. I only use `ValueTask` on hot paths that usually finish synchronously.

**Task.Run in a web API:** almost never. Wrapping I/O in `Task.Run` just moves the work from one pool thread to another and adds overhead. For CPU work inside a request it does not make that request faster. It only takes a thread away from other requests. I would only use it in two cases:

1. Running CPU-heavy work in parallel with I/O in the same request, and only after measuring that it helps.
2. Calling a third-party library that only has a sync API and cannot be avoided. Even then, a queue or a background worker is usually better.

For background work I use a `BackgroundService` (like `OutboxDispatcher` here) or a queue (RabbitMQ). Fire-and-forget `Task.Run` loses errors and can die with the request scope.

### 2. Minimal APIs vs controllers

**Minimal APIs**
- Less code: a method per route, grouped with `MapGroup`.
- Slightly faster because there is no MVC filter pipeline, and they support AOT.
- Validation is not built in, so you add it yourself. I used a FluentValidation endpoint filter.
- In a big project you need to organise them yourself, for example one file per feature.

**Controllers**
- More ceremony: classes, attributes, a base class.
- Mature features: MVC filters, model binding, automatic 400 responses with `[ApiController]`.
- The structure comes from convention, which helps big teams.

I used Minimal APIs in the project. Each feature has its own file (`Controllers/OrderEndpoints.cs`) with static handlers that return `TypedResults`, which also gives accurate Swagger documentation. A route group adds the auth policies, and `ValidationFilter<T>` runs validation. Most of the logic is in the services, so controllers would only add boilerplate.

I would choose controllers for a large existing MVC codebase, for a team that already knows MVC well, or when I need things Minimal APIs handle less neatly, like complex form binding, content negotiation or OData.

### 3. Tracking vs AsNoTracking, and optimistic concurrency

With **tracking**, EF keeps a copy of every entity it loads so it can detect changes and save them later. That is what you want when you are going to update the data. **AsNoTracking** skips this, so it uses less memory and CPU. All the read endpoints here use `AsNoTracking()` or project straight into DTOs with `Select`. Projection is even better because it only fetches the columns you need. Writes (`UpdateStatusAsync` and the worker) load tracked entities on purpose.

**Optimistic concurrency** is built in this repo like this:

1. `Orders` has a `rowversion` column, configured with `IsRowVersion()` in `OrderConfiguration.cs`. SQL Server changes it on every update.
2. EF adds `WHERE Id = @id AND RowVersion = @original` to the UPDATE. If nothing matches, someone else changed the row first, and EF throws `DbUpdateConcurrencyException`.
3. In `OrderService.UpdateStatusAsync` I catch it, clear the change tracker, read the current order again and return **409 Conflict** with the current order in the response. The user can then see what changed and decide. I don't retry automatically with "my version wins", because that could undo someone else's change without them knowing (for example, paying an order that was just cancelled).
4. The same rowversion is used as the HTTP **ETag**. A client can send `If-Match` on `PUT /status` and get a 412 if its copy is old, and `If-None-Match` on GET returns a 304 when nothing changed.

To do it safely, keep the read, the change and the save in one `DbContext` scope. Never set `RowVersion` from the client on a tracked entity's current value, because EF compares the *original* value. If a version comes from the client, set it as the original value or compare it yourself, like the `If-Match` check does.

### 4. Where business rules go, and FluentValidation vs manual validation

I check rules in different places, depending on what the rule needs:

- **Request (DTO):** the shape of the input: required fields, lengths, ISO code format, `pageSize ≤ 100`, `quantity > 0`, prices with at most 2 decimals. Done with FluentValidation in `Helpers/Validators.cs`, run by `ValidationFilter<T>` before the handler.
- **Domain:** rules that must always hold, no matter who calls: total = sum of quantity × price, valid status changes, at least one line item. Done in `Order.Create`, `Order.TransitionTo` and `OrderStatusTransitions`.
- **Service:** rules that need data from the database: the customer exists, the currency is allowed for the customer's country, only an Admin can cancel a paid order, idempotency. Done in `OrderService`.
- **Database:** the last safety net: NOT NULL, foreign keys, unique email, `CHECK (Quantity > 0)`, `decimal(18,2)`.

Some checks are repeated on purpose. Quantity is checked by the validator, the domain and a CHECK constraint, because each one protects a different way in. For example, the worker never goes through the validator.

I use **FluentValidation** for request validation. The rules are easy to read, easy to test on their own (`ValidatorTests.cs`), and every error comes back in the same format. The error keys are camelCase JSON paths like `lineItems[1].unitPrice` (set up in `ValidationSetup.cs`), so the UI can show each error next to the right input. I use domain methods for the core rules, because a validator can be skipped and a domain rule cannot. One thing I make sure of: a validator that is registered but never called is worse than no validator. Here the filter *requires* a validator (`GetRequiredService`), and every write endpoint calls `.Validate<T>()`.

### 5. Global exception handling and Problem Details

I have one `IExceptionHandler` (`Middleware/GlobalExceptionHandler.cs`), together with `AddProblemDetails()`, `UseExceptionHandler()` and `UseStatusCodePages()`. This way every error comes back as `application/problem+json`, including empty 401/403/404 responses and bad JSON (`ThrowOnBadRequest = true`).

Exceptions are mapped to status codes in that one place:

- validation error: 400, with an `errors` list per field
- not found: 404
- invalid status change or concurrency conflict: 409
- Idempotency-Key reused with a different body: 422
- old `If-Match` version: 412
- not allowed by a business rule: 403
- anything else: 500 with a general message. No stack trace goes to the client. The details are logged on the server with the correlation id.

This is what an error looks like (RFC 7807/9457 plus a few extra fields):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Invalid status transition",
  "status": 409,
  "detail": "An order cannot move from Pending to Fulfilled.",
  "instance": "/api/orders/3f2b.../status",
  "code": "invalid_status_transition",
  "currentStatus": "Pending",
  "allowedTransitions": ["Paid", "Cancelled"],
  "traceId": "00-4bf92f...-01",
  "correlationId": "7c9e6679..."
}
```

Clients should check the `code` field, which never changes, and not the `title` or `detail` text. `errors` maps each field to its messages for form errors. `traceId` and `correlationId` help support find the logs. Expected client errors are logged as Information and 5xx errors as Error, so the error dashboards are not full of 404s.

### 6. Search endpoints, pagination, sorting, GraphQL and N+1

I use query parameters for filters and paging, for example `GET /api/orders?customerId=&status=&sort=-totalAmount&page=2&pageSize=20`. Sorting only accepts known values (`createdAt`, `-createdAt`, `totalAmount`, `-totalAmount`). Accepting any text for ORDER BY would open the door to injection and to slow, unindexed sorts. An invalid value returns a 400 instead of being ignored.

The response includes paging information: `{ items, page, pageSize, totalCount, totalPages }`. `pageSize` is limited to 100. I always sort by `Id` last as a tie-breaker so the pages don't shuffle.

`OFFSET/FETCH` is simple and lets you jump to any page, but deep pages get slower, and rows can move between pages when new data comes in. For infinite scroll or exports I would use **keyset (cursor) pagination**: `WHERE (CreatedAt, Id) < (@lastCreatedAt, @lastId)` with a `nextCursor` value. `COUNT(*)` can also get expensive on very big tables. Then I would return an estimate, or just "has more pages".

**GraphQL** makes sense when many different clients need different data. For example, a mobile app needs 3 fields while an admin screen needs customer, orders and line items in one go. It also helps when the REST calls are fetching too much or too little, or when combining data from several services. It costs more, though: HTTP caching doesn't work naturally, you need authorization per field and limits on query cost, and N+1 moves into the resolvers (you need DataLoader). This API is simple CRUD with one UI, so REST with ETags is simpler. I skipped the optional GraphQL endpoint because of time. With HotChocolate I would put `[UseProjection][UseFiltering]` on an `IQueryable<Order>` resolver, which turns into one SQL query.

**N+1** happens when you load a list and then load each item's children separately, so you get one extra query per row. I avoid it by projecting: `o.LineItems.Count` in the list and `LineItems = o.LineItems.Select(...)` on the details page each become one SQL statement. Lazy loading is not enabled, so N+1 can't happen by accident. When including several collections, `AsSplitQuery()` stops the result from multiplying.

### 7. Security with Entra ID

**Token validation:** `AddMicrosoftIdentityWebApi(Configuration.GetSection("AzureAd"))` in `Services/AuthSetup.cs` (when `Auth:Mode` is `Entra`). It downloads the tenant's signing keys (cached and rotated automatically) and checks the signature, issuer, audience (`api://<client-id>`) and expiry. The settings are in `appsettings.json` under `AzureAd`.

**Roles:** I define three app roles in the API's app registration: `Orders.Read`, `Orders.Write` and `Orders.Admin`. They are assigned to users or groups, or to other apps as application permissions. They arrive in the `roles` claim. For delegated tokens I would also check the `scp` claim, so a token issued for another app can't be used against this one.

**Policies** (`AuthPolicies.cs`) build on each other: Read accepts Read, Write or Admin; Write accepts Write or Admin; Admin only accepts Admin. Endpoints use the policy name, never the role strings directly. There is also a fallback policy that requires a logged-in user, so a new endpoint is secure by default and a public one has to say `AllowAnonymous` on purpose. Rules that depend on the data, like "only an Admin can cancel a paid order", are checked with `IAuthorizationService` inside the handler, because a route attribute can't see the order's status.

**Token lifetime:** Entra access tokens last about 60 to 90 minutes. The SPA renews them silently through MSAL, and the API never sees a refresh token. If faster revocation is needed, Continuous Access Evaluation can be used.

**Mock mode** (`Auth:Mode = "Mock"`) is for local development without a tenant. `POST /api/dev/token` issues tokens with the same claims, signed with a local key. That endpoint only exists in mock mode, and the API refuses to start in mock mode in Production.

**Other secure defaults:** HTTPS and HSTS outside development; security headers (`nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, a strict CSP on API responses); a CORS allow-list; validation on every write; correlation ids limited to safe characters so they can't be used for log injection; no exception details in responses; and only parameterised SQL.

### 8. RabbitMQ

**Exchange types:**
- *Direct* sends a message to queues whose binding key matches exactly. I use it for the retry exchange: `retry.1` goes to the 5-second queue.
- *Topic* matches patterns like `order.*`. The main `sadc.orders` exchange is a topic, so a new consumer can subscribe to `order.#` without changing the publisher.
- *Fanout* sends to every bound queue and ignores the key.
- *Headers* matches on header values. I rarely find it worth using.

**Durability:** a durable exchange and queue survive a broker restart, but the messages only survive if they are also marked **persistent**. In production I would use quorum queues, which are replicated.

**At-least-once delivery** needs work on both sides:
- *Publisher:* I use the **outbox pattern**. The order and its `OutboxMessages` row are saved in the same transaction. A background dispatcher publishes the row and waits for the broker's confirmation (publisher confirms). Only then does it mark the row as sent. If the app crashes in between, the message is sent again later. That gives a duplicate, but never a lost message.
- *Consumer:* manual acks, sent only after the work is saved. If the worker crashes before acking, RabbitMQ delivers the message again. `prefetch = 10` limits how many messages one consumer holds at a time.

**Idempotency:** because of the above, duplicates will happen, so the consumer must handle them. `OrderCreatedHandler` saves the `MessageId` in `ProcessedMessages` in the same save as the status change. A redelivered message is found there and skipped. If two consumers race, the primary key stops the second one. On top of that, an order that is already Fulfilled or Cancelled is left alone. The `MessageId` is the outbox row id, so it stays the same when the message is published again.

**Retry, backoff and dead letters** (`RabbitMqTopology.cs`, `OrderCreatedConsumer.cs`): when processing fails, the consumer republishes the message to `orders.created.retry.{n}` and increases `x-retry-count`. These retry queues have a time-to-live (5 s, 30 s, 120 s). When the time is up, the message goes back to the main exchange. I use one queue per delay because a single queue with different per-message TTLs blocks: a long-delay message at the front holds up the short ones behind it. After the last retry, or for a message that can't be read (bad JSON), the consumer rejects it without requeue. It then goes to `sadc.orders.dlx` and lands in `orders.created.dlq`, where the `x-death` header shows why. Someone can look at the DLQ and send the messages back once the problem is fixed. You can see this in the management UI by setting `Worker__SimulatedFailureRate=0.5` in docker-compose.

### 9. React + TypeScript: state management and type-safe API contracts

Most of the "state" in a CRUD app is really data from the server, and a tool like **SWR** (or RTK Query / TanStack Query) is built for that. It caches per key, removes duplicate requests, refreshes when you come back to the tab, and keeps the previous page on screen while the next one loads (`keepPreviousData`). After creating an order I refresh only the order lists with `mutate(key => key[0] === 'orders')`. I used SWR (`src/api/hooks.ts`).

I use **Context** for small app state that rarely changes, like the current role in `AuthProvider`. Context re-renders everything that uses it, so it is not good for state that changes often.

**Redux Toolkit** is worth it when there is a lot of client-side state with complex logic shared across the app. That is not the case here, so I didn't use it.

Filters, sort and page number live in the **URL** (`useSearchParams` in `OrdersPage`). You can bookmark or share a filtered list, and the Back button works.

**Type-safe contracts:** `src/api/schemas.ts` has **Zod** schemas that match the C# DTOs field by field. The TypeScript types come from them (`z.infer`), and every API response is checked at runtime in `apiRequest`. If the backend renames a field, the UI shows a clear "Unexpected response" error instead of quietly showing `undefined`. In a bigger project I would also generate the types from the OpenAPI document in CI (with `openapi-typescript` or Orval), so a mismatch fails the build.

### 10. Test pyramid and CI gates

From the bottom of the pyramid to the top:

- **Unit tests (most of them, fast, no database):** the rules that are easy to get wrong: totals and rounding, every status change, all 16 country and currency pairs, the CMA setting, and the validator error paths. Built with xUnit in `UnitTest/`. On the frontend, Vitest + React Testing Library test the forms, the lists and the idempotent retry.
- **Integration tests:** this is where most real bugs in this stack hide: EF mappings, real SQL Server behaviour (rowversion, unique indexes, race conditions) and the HTTP pipeline (auth policies, error format, 304s, headers). They use `WebApplicationFactory` with a **real SQL Server** in Docker (Testcontainers). I don't use the EF in-memory provider, because it has no rowversion, transactions or constraints, so tests could pass that would fail in production. The schema comes from the real migrations, so the migrations get tested as well.
- **E2E (only a few):** a short test of the main flow against the full Docker stack: create a customer, create an order, change the status. These tests are slow and can be flaky, so I keep them few. I planned this with Playwright but didn't get to it because of time.

Frontend component tests check what the user sees, using accessible queries like `getByRole` and `getByLabelText`, not implementation details.

**CI gates** (`.github/workflows/ci.yml`):
- *Backend:* restore, `dotnet format --verify-no-changes`, build, a check for vulnerable packages, unit tests, integration tests, a coverage report, a check that the model has no un-migrated changes, the migration script applied twice to an empty SQL Server, a rollback and re-apply of the migrations, and publishing.
- *Web:* `npm ci`, ESLint, `tsc`, `npm audit`, Vitest with coverage, and the build.
- *After both:* the Docker images are built, and pushed on `main`.

I would also add branch protection that requires these checks, a coverage rule on *changed* lines (a fixed overall % tends to produce useless tests), CodeQL, an image scan (Trivy) and secret scanning.

### 11. Performance, scalability and observability

**Write spikes:** the API only does the quick part. It validates, saves the order and the outbox row in one transaction, and returns 201. The slow work goes through RabbitMQ, which absorbs the spike. The consumer's `prefetch` and the number of workers control the pace, so the queue grows instead of the database being overloaded. Workers can be scaled out safely because the handler is idempotent. I would also add rate limiting (`AddRateLimiter`) and return 429 with `Retry-After` instead of accepting unlimited work.

**Indexes:** `IX_Orders_CustomerId_Status_CreatedAt INCLUDE (CurrencyCode, TotalAmount)` matches the main list query: the equality columns come first, then the sort column. `IX_Orders_Status_CreatedAt` covers lists without a customer filter, and a filtered index finds unprocessed outbox rows. I design indexes from the real WHERE and ORDER BY clauses and check them in the execution plan (see Q14 and Q15).

**Caching:** order details use the rowversion as an ETag, so an unchanged order returns 304 with no body. The list of countries is cached on the client for the session. Next steps would be output caching for popular public GETs, and Redis when there are several API instances.

**Database:** `AsNoTracking` projections, no N+1, stable paging, Read Committed Snapshot so readers don't block writers (Q17), automatic retry of temporary SQL errors (`EnableRetryOnFailure`), and partitioning when the data gets big (Q19).

**Correlation ids and tracing:** middleware accepts an `X-Correlation-ID` header or creates one, and returns it in the response. It is added to every log line, saved on the outbox row, sent with the RabbitMQ message and logged by the worker. So one id follows an order from the browser all the way to Fulfilled. In production I would add OpenTelemetry tracing across HTTP, SQL and RabbitMQ, exported to Azure Monitor or Jaeger.

**Metrics:** request count and duration per route (`RequestMetrics`), published on a .NET `Meter` and as JSON at `/metrics`. ASP.NET Core's built-in request metrics are also available. Health checks: `/healthz` (is the app running) and `/readiness` (can it reach SQL Server and RabbitMQ).

**SLOs I would suggest:**
- 99.9% of requests without a 5xx error over 30 days.
- 95% of `GET /api/orders` requests under 300 ms, and 95% of `POST /api/orders` requests under 500 ms.
- 99% of orders Fulfilled within 60 seconds of being created.

I would alert when the error budget is being used up too fast, not on every single spike, and also whenever there is a message in the DLQ.

---

## SQL section

The queries use the tables this repo creates (`dbo.Customers`, `dbo.Orders`, `dbo.OrderLineItems`). `Status` is stored as text.

### 12. Pagination query with a separate COUNT

```sql
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
```

From C# it works the same way: `new SqlParameter("@CustomerId", customerId)`, or EF's `FromSql($"... WHERE CustomerId = {customerId}")`, which turns the values into parameters and doesn't build a string. The API does this with LINQ in `OrderService.ListAsync` (`CountAsync()`, then `Skip/Take`), which produces the same SQL. Both queries use `IX_Orders_CustomerId_Status_CreatedAt` because `CustomerId` is its first column. For very deep pages I would switch to keyset paging: `WHERE o.CustomerId = @CustomerId AND (o.CreatedAt < @LastCreatedAt OR (o.CreatedAt = @LastCreatedAt AND o.Id < @LastId))`.

### 13. Top 10 spenders in the last 90 days, including customers with no orders

```sql
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
```

Two things matter here. First, the date filter has to be in the `ON` clause. If you put it in `WHERE`, customers with no orders disappear, and those are exactly the ones the question asks to keep. Second, the currency: adding ZAR and MWK amounts together means nothing. A correct report either ranks per currency, or converts everything to one currency with a table of daily exchange rates:

```sql
-- 13b. The same ranking in a common currency (ZAR) using a rates table (e.g. loaded daily from a rates feed).
--      CMA currencies (NAD, LSL, SZL) convert 1:1 to ZAR by definition.
-- CREATE TABLE dbo.FxRates (RateDate DATE, CurrencyCode CHAR(3), ZarPerUnit DECIMAL(18,6), PRIMARY KEY (RateDate, CurrencyCode));
-- SELECT TOP (10) c.Id, c.Name, COALESCE(SUM(o.TotalAmount * r.ZarPerUnit), 0) AS TotalSpendZar
-- FROM dbo.Customers c
-- LEFT JOIN dbo.Orders o ON o.CustomerId = c.Id AND o.CreatedAt >= @Since AND o.Status <> N'Cancelled'
-- LEFT JOIN dbo.FxRates r ON r.CurrencyCode = o.CurrencyCode AND r.RateDate = CAST(o.CreatedAt AS DATE)
-- GROUP BY c.Id, c.Name ORDER BY TotalSpendZar DESC;
```

### 14. Indexing Orders for (CustomerId, Status, CreatedAt)

```sql
-- 14. The listing index, as created by the InitialCreate migration.
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId_Status_CreatedAt
    ON dbo.Orders (CustomerId, Status, CreatedAt)
    INCLUDE (CurrencyCode, TotalAmount);
```

**Why this column order:** the columns compared with `=` come first (`CustomerId`, `Status`), then the column used for range and sorting (`CreatedAt`). A query like "customer X's Paid orders, newest first" becomes one seek to a block of rows that is already sorted, so SQL Server doesn't need a sort step (it reads backwards for DESC). The index also works for `WHERE CustomerId = @c` alone, and it doubles as the index for the `CustomerId` foreign key.

**Covering index:** the `INCLUDE` columns are stored only at the leaf level. The list query needs `Id, Status, CreatedAt, CurrencyCode, TotalAmount` (`Id` comes for free as the clustered key), so it can be answered from the index alone, with no key lookup per row. That means fewer reads, steady response times and less blocking. The cost is a bigger index, and every insert (and every update to an included column) also writes to it. `TotalAmount` never changes after an order is created, so that is cheap here. I only include columns that the busy queries actually select, so the index doesn't become a second copy of the table.

**Clustered index choice:** the primary key is a GUID, and EF makes it the clustered index. Random GUIDs cause page splits and fragmentation when there are many inserts. There are two options:

1. Use sequential GUIDs (`NEWSEQUENTIALID()`, UUIDv7 or EF's `SequentialGuidValueGenerator`), so new rows go at the end.
2. Make the primary key nonclustered and cluster on `(CreatedAt, Id)`. Inserts always go at the end, date-range reports get cheap, and it fits monthly partitioning (Q19). The downside is that every other index carries the wider clustered key, and finding a row by `Id` costs one extra seek.

For millions of rows I would choose option 2. For the amount of data in this assessment, the default is fine. I mention it in the README as a step for scaling up.

If the most common query is "a customer's orders, newest first, any status" (Q12), having `Status` in the middle of the index means a small sort per customer. That is cheap for a few hundred orders per customer. If it shows up in the execution plans, I would add `(CustomerId, CreatedAt DESC) INCLUDE (Status, CurrencyCode, TotalAmount)`. Every extra index makes writes slower, so it has to be worth it.

### 15. Finding and removing key lookups

```sql
-- 15. Orders + line items for a customer and status. Look at the actual execution plan.
DECLARE @Cust UNIQUEIDENTIFIER = (SELECT TOP (1) CustomerId FROM dbo.Orders GROUP BY CustomerId ORDER BY COUNT(*) DESC);
SET STATISTICS IO ON;
SELECT o.Id, o.CreatedAt, o.TotalAmount, li.ProductSku, li.Quantity, li.UnitPrice
FROM dbo.Orders AS o
JOIN dbo.OrderLineItems AS li ON li.OrderId = o.Id
WHERE o.CustomerId = @Cust AND o.Status = N'Fulfilled';
SET STATISTICS IO OFF;
```

**How to find it:** in the actual execution plan (Ctrl+M in SSMS), look for a **Key Lookup (Clustered)** operator inside a Nested Loops join, next to an Index Seek. Its Output List shows the columns the index was missing. Other signs are a high number of executions on the lookup (one per row) and high logical reads in `STATISTICS IO`. To find the worst cases across the whole system, Query Store's top queries by logical reads, or `sys.dm_exec_query_stats` with the plan XML searched for `Lookup="1"`, show where it matters.

**In this query** the Orders side is already covered by `IX_Orders_CustomerId_Status_CreatedAt`. The problem is the line items. `IX_OrderLineItems_OrderId` (the foreign key index EF creates) only has `OrderId` and `Id`, so every matching line needs a lookup to get `ProductSku`, `Quantity` and `UnitPrice`.

**How to remove it:** add the missing columns as INCLUDE columns, so the seek alone has everything the query needs:

```sql
CREATE NONCLUSTERED INDEX IX_OrderLineItems_OrderId_Covering
    ON dbo.OrderLineItems (OrderId)
    INCLUDE (ProductSku, Quantity, UnitPrice)
    WITH (ONLINE = ON);   -- Enterprise/Azure SQL; drop the old IX_OrderLineItems_OrderId afterwards (it's now redundant)
```

Other options: select fewer columns (often the real fix), or cluster `OrderLineItems` on `(OrderId, Id)`, so each order's lines are stored together. That is a strong option here, because line items are always read per order.

### 16. Optimistic concurrency with rowversion

The schema change (migration `20261116090000_AddOrderRowVersion`). Existing rows get a value automatically:

```sql
ALTER TABLE dbo.Orders ADD RowVersion ROWVERSION NOT NULL;
```

EF Core mapping (`OrderConfiguration.cs`):

```csharp
public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
// ...
b.Property(o => o.RowVersion).IsRowVersion();   // concurrency token, store-generated on add and update
```

Handling the conflict (simplified from `OrderService.UpdateStatusAsync`):

```csharp
var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("Order", id);
order.TransitionTo(target, now);                 // domain rule check
try
{
    await _db.SaveChangesAsync(ct);              // UPDATE ... WHERE Id = @id AND RowVersion = @original
}
catch (DbUpdateConcurrencyException)
{
    _db.ChangeTracker.Clear();                   // drop the stale tracked entity
    var current = await GetAsync(id, ct);        // re-read what's actually there now
    throw new ConflictException("concurrency_conflict",
        "The order was updated by another process. Review its current state and retry.",
        current: current.Order);                 // -> 409 Problem Details with "current": { ... }
}
```

**How it works:** EF remembers the `RowVersion` it read and adds it to the WHERE clause of the UPDATE. If someone else (another user, or the worker fulfilling the order) saved a change in between, SQL Server has already changed the rowversion. The UPDATE then changes 0 rows, and EF throws. I return 409 with the current state instead of retrying blindly, so the caller can decide if their change still makes sense. A test proves this works (`OrderStatusApiTests.RowVersion_detects_a_lost_update`). The rowversion is also the ETag, so clients can send `If-Match` and get a 412 before they even try to save old data.

### 17. Deadlocks: a reader/writer example and how to prevent it

**The scenario** (a classic key-lookup deadlock):

1. **Reader** (normal READ COMMITTED with locks): `SELECT Id, Status, TotalAmount, CurrencyCode FROM Orders WHERE Status = 'Pending' ORDER BY CreatedAt`. It seeks `IX_Orders_Status_CreatedAt` and takes a shared lock on the index key. Then it does a key lookup and asks for a shared lock on the clustered row, to read `TotalAmount` and `CurrencyCode`.
2. **Writer** (the worker): `UPDATE Orders SET Status = 'Paid' WHERE Id = @id`. It takes an exclusive lock on the clustered row first. Then it needs an exclusive lock on the index key, because `Status` is part of that index.
3. The reader holds the index key and waits for the row. The writer holds the row and waits for the index key. Neither can continue, so SQL Server kills one of them (error 1205).

The deadlock happens because the two statements lock the same two things in opposite order.

**What I used in this repo: Read Committed Snapshot Isolation** (migration `20261116110000_EnableReadCommittedSnapshot`):

```sql
-- 17. RCSI: readers see the last committed row version and take no shared locks.
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
-- Check: SELECT name, is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME();
```

With RCSI, readers read the last committed version of the row and don't take shared locks. So they can't be part of a reader/writer deadlock, and list pages never wait for writes. The costs are extra tempdb usage, and readers may see data that is a few milliseconds old, which is fine for lists. Writer-against-writer conflicts are still caught by rowversion (Q16).

**Other ways to prevent deadlocks:**
- Remove the lookup by adding the columns the reader needs to the index (`INCLUDE (TotalAmount, CurrencyCode)`). Then the reader never touches the clustered row.
- Keep transactions short, and never call RabbitMQ or an HTTP API inside one. That is one reason for the outbox.
- Always access tables in the same order, for example Orders before OrderLineItems.
- Retry automatically on 1205. EF's `EnableRetryOnFailure` treats it as a temporary error.
- Use locking hints only when there is a good reason. `WITH (UPDLOCK, ROWLOCK)` on a read-then-update stops two sessions from both reading and then both trying to write. I avoid `NOLOCK`, because it can return dirty or duplicate rows, and RCSI already gives non-blocking reads without that problem.

### 18. Running total per customer with a window function

```sql
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
```

Notes:
- I write `ROWS` explicitly. Without it, the default is `RANGE UNBOUNDED PRECEDING`, which gives orders with the same `CreatedAt` the same combined total, and it is slower because it uses an on-disk worktable. Adding `Id` to the ORDER BY makes the order fixed.
- Currency again: a customer in Zimbabwe can have orders in both ZWL and USD. For a total that makes sense, use `PARTITION BY o.CustomerId, o.CurrencyCode`, or convert to one currency first (Q13).
- An index on `(CustomerId, CreatedAt) INCLUDE (TotalAmount, Status)` lets SQL Server calculate this without a sort.

### 19. Partitioning millions of orders, and archiving with partition switching

**My choice: monthly partitions on `CreatedAt`, using RANGE RIGHT.**

```sql
-- 19. Monthly partitions (shown on a separate demo table; repartitioning dbo.Orders is a planned migration).
CREATE PARTITION FUNCTION pf_OrdersMonthly (DATETIME2(7))
    AS RANGE RIGHT FOR VALUES ('2026-01-01', '2026-02-01', '2026-03-01' /* ... one boundary per month, created ahead of time */);

CREATE PARTITION SCHEME ps_OrdersMonthly
    AS PARTITION pf_OrdersMonthly ALL TO ([PRIMARY]);   -- or one filegroup per year: old ones on cheaper storage / read-only

-- The partitioning column must be part of every unique index, including the clustered one:
-- PK becomes NONCLUSTERED (Id, CreatedAt); cluster on (CreatedAt, Id).
CREATE TABLE dbo.OrdersPartitioned (
    Id           UNIQUEIDENTIFIER NOT NULL,
    CustomerId   UNIQUEIDENTIFIER NOT NULL,
    Status       NVARCHAR(20)     NOT NULL,
    CreatedAt    DATETIME2(7)     NOT NULL,
    CurrencyCode CHAR(3)          NOT NULL,
    TotalAmount  DECIMAL(18,2)    NOT NULL,
    CONSTRAINT PK_OrdersPartitioned PRIMARY KEY NONCLUSTERED (Id, CreatedAt) ON ps_OrdersMonthly (CreatedAt)
);
CREATE CLUSTERED INDEX CIX_OrdersPartitioned_CreatedAt_Id ON dbo.OrdersPartitioned (CreatedAt, Id) ON ps_OrdersMonthly (CreatedAt);
CREATE INDEX IX_OrdersPartitioned_CustomerId_Status_CreatedAt
    ON dbo.OrdersPartitioned (CustomerId, Status, CreatedAt) INCLUDE (CurrencyCode, TotalAmount)
    ON ps_OrdersMonthly (CreatedAt);   -- aligned index, needed for SWITCH
```

**Why monthly with RANGE RIGHT:** each boundary is the first moment of a month, so all of a month's orders are in one partition and there are no end-of-day edge cases. Monthly gives about 12 partitions a year, which is easy to manage, and fits a typical rule like "keep 24 months of data online". Queries with a date range (reports, `sp_GetTransactionReport`) only read the partitions they need (partition elimination). Partitioning is mostly about *managing* the data (fast loading and archiving, rebuilding or compressing one partition at a time). It doesn't automatically make single-row queries faster.

**Archiving with partition switching (sliding window):**

```sql
-- 1) Ahead of time: always keep an empty partition at the leading edge.
ALTER PARTITION SCHEME ps_OrdersMonthly NEXT USED [PRIMARY];
ALTER PARTITION FUNCTION pf_OrdersMonthly() SPLIT RANGE ('2026-04-01');   -- splitting an EMPTY range is metadata-only

-- 2) Archive the oldest month: switch it out into an identical, empty staging table
--    (same columns, same indexes, same filegroup, plus a CHECK constraint matching the range).
-- CREATE TABLE dbo.OrdersArchive_Staging (...same definition...) ON [PRIMARY];
ALTER TABLE dbo.OrdersPartitioned SWITCH PARTITION 1 TO dbo.OrdersArchive_Staging;   -- metadata-only: milliseconds, not hours

-- 3) Remove the now-empty boundary.
ALTER PARTITION FUNCTION pf_OrdersMonthly() MERGE RANGE ('2026-01-01');

-- 4) Move the staging rows to cold storage at leisure (archive DB, BCP to Blob, or SWITCH into a partitioned archive table), then truncate staging.
```

`SWITCH` only changes metadata, so archiving a month takes milliseconds and doesn't fill the transaction log. A `DELETE ... WHERE CreatedAt < X` would take hours, lock a lot of rows and grow the log. The rules for switching: the tables need the same columns and indexes, the same filegroup and aligned indexes, and no foreign keys can point at the table being switched. That means `OrderLineItems` needs to be partitioned the same way. I would copy the order's `CreatedAt` onto the line items, partition them with the same function, and switch the line items out before the orders. On Azure SQL the same design works, and the archived data can be exported to Blob storage for cheap long-term keeping.

### 20. sp_GetTransactionReport

The full source is in [`db/sql/sp_GetTransactionReport.sql`](db/sql/sp_GetTransactionReport.sql). Migration `20261116100000_AddIdempotencyOutboxAndReportProc` deploys it, and CI runs it against an empty database.

How I built it:

- **Including the whole end date:** I use `CreatedAt >= @StartDate AND CreatedAt < DATEADD(DAY, 1, @EndDate)`. This doesn't miss orders later in the day on the end date, and it can still use the index because there is no function on the column.
- **Optional filters:** `@Status` and `@CustomerId` default to NULL and use `(@p IS NULL OR col = @p)` with `OPTION (RECOMPILE)`. Without RECOMPILE, one cached plan would be reused for every combination of parameters, for example a full-scan plan for a single-customer call. For a report, the cost of recompiling is small compared to that risk. The other option is dynamic SQL with `sp_executesql` that only adds the filters that are needed.
- **Matching orders go into a temp table first,** so the detail rows and the summary are based on exactly the same orders. Two separate queries could disagree if a new order came in between them.
- **The summary is calculated from the orders, not the detail rows.** Adding up `TotalAmount` over the detail rows would count each order once per line item. `TotalOrders` is `COUNT(*)` over the orders.
- **Result sets:** (1) the details: customer name, email and country; order id, status, date, currency and total; SKU, quantity and unit price. (2) `TotalOrders` and `GrandTotalAmount`, as asked. (3) An extra one with totals per currency, because a grand total across different currencies only makes sense when the report is filtered to one currency, or to the CMA currencies, which are at par.
- **Validation:** a NULL or backwards date range raises error 50001 instead of returning an empty report.
- **Performance:** a call for one customer seeks `IX_Orders_CustomerId_Status_CreatedAt`. Calls by status and date can use `IX_Orders_Status_CreatedAt`. The line item join benefits from the covering index in Q15. With a lot of data, monthly partitioning (Q19) means date-range reports only read the partitions they need.

```sql
-- 20. Usage
EXEC dbo.sp_GetTransactionReport @StartDate = '2026-08-01', @EndDate = '2026-11-30';
EXEC dbo.sp_GetTransactionReport @StartDate = '2026-01-01', @EndDate = '2026-12-31', @Status = N'Fulfilled';
```
