# SADC Order Management

A small full-stack order management system for customers in the Southern African Development Community (SADC):

- **API:** ASP.NET Core 8 Minimal APIs, EF Core 8 (code-first) on SQL Server, Swagger, Microsoft Entra JWT (or mock), Problem Details, ETags, idempotent status updates.
- **Messaging:** a transactional outbox publishes `OrderCreated` to RabbitMQ. A worker consumes it with retry/backoff queues, a dead-letter queue and idempotent handling, and moves the order to `Fulfilled`.
- **Web:** React 19 + TypeScript (Vite), with SWR for caching and Zod-validated API contracts.
- **Ops:** Docker Compose (sqlserver, rabbitmq, api, worker, web), a GitHub Actions pipeline with migration checks, and `.http` request examples.

Written answers to the assessment questions are in **[ANSWERS.md](ANSWERS.md)**.

```
 Browser (React SPA) ──/api──► nginx ──► API (ASP.NET Core 8)
                                          │  1 transaction: INSERT Order + LineItems + OutboxMessage
                                          ▼
                                     SQL Server ◄────────────────────────────┐
                                          ▲                                  │ UPDATE status (rowversion)
                OutboxDispatcher (in API) │ poll unprocessed rows             │ + INSERT ProcessedMessages
                                          ▼                                  │
                     RabbitMQ  sadc.orders (topic) ─► orders.created ─► Worker (OrderCreatedHandler)
                                  ▲          retry.1/2/3 TTL queues ◄──┤ failure: republish with x-retry-count
                                  └──────────── dead-letter back ──────┘ retries used up: orders.created.dlq
```

---

## Quick start (Docker)

Requirements: Docker Desktop (or another Docker engine) with about 4 GB RAM for SQL Server.

```bash
docker compose up --build
```

| What | URL |
|---|---|
| Web UI | http://localhost:8080 |
| API + Swagger | http://localhost:5080/swagger |
| RabbitMQ management | http://localhost:15672 (guest / guest) |
| Health | http://localhost:5080/healthz, http://localhost:5080/readiness |
| Metrics (JSON) | http://localhost:5080/metrics |

On first start the API applies the EF migrations and seeds **200 customers and 1,200 orders** across all 16 SADC countries and their currencies. Create an order in the UI and refresh after a couple of seconds: the worker moves it `Pending → Paid → Fulfilled`.

**Auth in the UI:** use the *Acting as* selector (Reader / Writer / Admin). In mock mode the SPA gets a matching token from `POST /api/dev/token`. In Swagger, call that endpoint, copy `accessToken`, and click **Authorize**.

**See retries and the DLQ:** set `Worker__SimulatedFailureRate: "0.5"` on the worker in `docker-compose.yml`, run `docker compose up -d worker`, then watch the `orders.created.retry.*` and `orders.created.dlq` queues in the RabbitMQ UI.

## Running locally (Visual Studio / dotnet run)

In Development the API and worker use **SQL Server LocalDB** (`(localdb)\MSSQLLocalDB`, installed with Visual Studio), mock auth, and migrate + seed on startup. RabbitMQ runs in Docker:

```powershell
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management   # first time; afterwards: docker start rabbitmq

cd SadcOrdersAssessment
dotnet run --project SadcOrdersAssessment --launch-profile http   # http://localhost:5059/swagger
dotnet run --project SadcOrdersAssessment.Worker                  # second terminal

cd ..\SadcOrdersAssessmentFrontend
npm ci
npm run dev                                                       # http://localhost:5173 (proxies /api to :5059)
```

In Visual Studio: solution → *Configure Startup Projects* → *Multiple startup projects* → start both `SadcOrdersAssessment` and `SadcOrdersAssessment.Worker`.

If the API runs on another port, start Vite with `VITE_API_PROXY`, e.g. `$env:VITE_API_PROXY="https://localhost:7221"; npm run dev`.

Requirements: .NET 8 SDK (the `.slnx` solution file needs the .NET 9 SDK 9.0.200+ or Visual Studio 17.13+ to open/build; projects still target net8.0), Node 22.13+ (Vite 8 and Vitest need it), and Docker for RabbitMQ and the integration tests.

## Verify before submitting

```powershell
cd SadcOrdersAssessment
dotnet format SadcOrdersAssessment.slnx            # formats the code so CI's `--verify-no-changes` lint passes
dotnet build SadcOrdersAssessment.slnx
dotnet test SadcOrdersAssessment.slnx              # unit + integration tests (Docker must be running)
dotnet ef migrations has-pending-model-changes --project DataLayer --startup-project SadcOrdersAssessment

cd ..\SadcOrdersAssessmentFrontend
npm ci; npm run lint; npm run typecheck; npm test; npm run build
```

`has-pending-model-changes` must report *no changes*. It confirms the migrations and the model snapshot match the entity configuration exactly.

---

## Repository layout

```
SadcOrdersAssessment/                    .NET solution (SadcOrdersAssessment.slnx)
  Domain/                                Entities and rules: Order (total, transitions), Customer, SADC country↔currency table. No dependencies.
  Core/                                  DTOs, interfaces (IOrderService, ICustomerService, repositories, IMessagePublisher), app exceptions.
  LogicLayer/                            Application services: CustomerService, OrderService (rules that need data, idempotency, concurrency), metrics.
  DataLayer/                             EF Core DbContext, configurations, migrations, seeder, repositories; RabbitMQ topology/publisher; outbox.
  SadcOrdersAssessment/                  The API: Minimal API endpoints (Controllers/), validation & auth (Helpers/, Services/), middleware, Problem Details, health.
  SadcOrdersAssessment.Worker/           RabbitMQ consumer (retry/DLQ) + idempotent OrderCreated handler.
  UnitTest/                              Domain rules, SADC/currency matrix, validators.
  SadcOrdersAssessment.IntegrationTests/ WebApplicationFactory + SQL Server (Testcontainers): endpoints, migrations, idempotency, ETag, auth, outbox, worker.
SadcOrdersAssessmentFrontend/            React + TS SPA (Vite, SWR, Zod, Vitest + Testing Library).
db/sql/                                  sp_GetTransactionReport source + assessment SQL (ANSWERS 12–20).
SadcOrdersAssessment/SadcOrdersAssessment/SadcOrdersAssessment.http   Example requests (Visual Studio / VS Code REST Client / Rider).
docker-compose.yml                       Full stack: sqlserver, rabbitmq, api, worker, web.
.github/workflows/ci.yml                 CI pipeline.
```

Dependencies point inward: API and Worker → LogicLayer / DataLayer → Core → Domain. The Domain has no framework references. Endpoints depend on `ICustomerService` and `IOrderService`, services depend on repository interfaces and `IMessagePublisher` from Core, so tests can swap implementations (the integration tests replace RabbitMQ with an in-memory publisher).

## API

| Method & route | Policy | Notes |
|---|---|---|
| `POST /api/customers` | Orders.Write | Country must be an SADC ISO 3166-1 alpha-2 code. Email is unique (409 on a duplicate). |
| `GET /api/customers/{id}` | Orders.Read | |
| `GET /api/customers?search=&page=&pageSize=` | Orders.Read | Searches name or email. `pageSize` ≤ 100. |
| `POST /api/orders` | Orders.Write | Validates the customer and the currency for the customer's country; computes the total on the server; writes OrderCreated to the outbox. 201 + ETag. |
| `GET /api/orders/{id}` | Orders.Read | Includes line items and `allowedTransitions`. ETag; `If-None-Match` → 304. |
| `GET /api/orders?customerId=&status=&page=&pageSize=&sort=` | Orders.Read | `sort` ∈ `createdAt`, `-createdAt` (default), `totalAmount`, `-totalAmount`. |
| `PUT /api/orders/{id}/status` | Orders.Write | Body `{ "status": "Paid" }`. **`Idempotency-Key` header required.** Optional `If-Match`. Cancelling a *Paid* order needs **Orders.Admin**. |
| `GET /api/reference/sadc-countries` | anonymous | Countries and their allowed currencies (the UI uses it, so client and server share one table). |
| `POST /api/dev/token` | anonymous, **mock mode only** | `{ "roles": [...] }` → JWT. |
| `GET /healthz`, `GET /readiness`, `GET /metrics` | anonymous | Liveness (no dependencies), readiness (SQL + RabbitMQ), request metrics. |

**Status machine:** `Pending → Paid → Fulfilled`; `Pending → Cancelled`; `Paid → Cancelled`. Fulfilled and Cancelled are terminal. An invalid transition returns **409** with `currentStatus` and `allowedTransitions`.

**Idempotency (`PUT /status`):** the first successful response is stored in `IdempotencyRecords` **in the same transaction as the status change**, keyed by `order + Idempotency-Key`. A retry with the same key returns the stored body unchanged with `Idempotent-Replayed: true`. The same key with a different body returns **422**. Concurrent duplicates are settled by the primary key and the rowversion, so the change is applied exactly once and every caller gets the same 200 (an integration test fires 5 in parallel). Only successes are stored, so a failed attempt can be retried with the same key. The SPA keeps one key per intended change and reuses it after transient failures.

**Errors:** every error is `application/problem+json` with a stable `code`, field-level `errors` (camelCase paths such as `lineItems[0].unitPrice`), `traceId` and `correlationId`. See ANSWERS.md, Q5.

### Authentication & authorization

`Auth:Mode` selects the scheme:

- **`Entra`** (default in `appsettings.json`): Microsoft.Identity.Web validates real Entra ID access tokens. Setup:
  1. Register an app for the API → *Expose an API* → Application ID URI `api://<client-id>`.
  2. *App roles*: add `Orders.Read`, `Orders.Write`, `Orders.Admin` (allowed member types: Users/Groups and Applications).
  3. Enterprise application → *Users and groups*: assign roles. For daemons, grant them as application permissions.
  4. Set `AzureAd:TenantId`, `AzureAd:ClientId`, `AzureAd:Audience` (user secrets or environment variables; never commit them).
  5. The SPA would use MSAL (`@azure/msal-browser`) and plug `acquireTokenSilent` into `setTokenProvider` in `SadcOrdersAssessmentFrontend/src/api/client.ts`.
- **`Mock`** (Development and Compose): tokens with the same claims (`roles`, `name`, `oid`), signed with a local HMAC key. **The API refuses to start in mock mode when the environment is Production.**

Policies are hierarchical (Admin ⊇ Write ⊇ Read). A fallback policy requires authentication on every endpoint unless it explicitly allows anonymous access.

## Messaging & reliability

- **Outbox:** `POST /api/orders` writes `Orders`, `OrderLineItems` and an `OutboxMessages` row in **one** `SaveChanges` (one transaction). `OutboxDispatcher` (a hosted service in the API) publishes pending rows with publisher confirms and marks them processed only after the broker confirms. If RabbitMQ is down, events wait in the table. Nothing is lost and order creation keeps working.
- **Topology** (`RabbitMqTopology.cs`): durable topic exchange `sadc.orders` → durable queue `orders.created` (persistent messages). Retry exchange → `orders.created.retry.{1,2,3}` TTL queues (5 s, 30 s, 120 s) that dead-letter back to the main exchange. DLX → `orders.created.dlq`.
- **Worker:** manual acks, `prefetch = 10` (back-pressure), and a correlation id taken from the message and added to every log line. On failure it republishes to the next retry queue (confirmed) and acks the original. Once retries are used up, or for a poison message, it nacks without requeue, so the message lands in the DLQ with `x-death` details.
- **Idempotent consumer:** `ProcessedMessages(MessageId)` is inserted in the same `SaveChanges` as the status change, so redeliveries are skipped. The handler also leaves orders that are already Fulfilled or Cancelled alone. A concurrent edit trips `RowVersion`, which triggers a retry that sees the new state.

## SADC countries, currencies and the Common Monetary Area

Validation uses the brief's table (`SadcCurrencyRules.cs`): AO→AOA, BW→BWP, KM→KMF, CD→CDF, SZ→SZL, LS→LSL, MG→MGA, MW→MWK, MU→MUR, MZ→MZN, NA→NAD, SC→SCR, ZA→ZAR, TZ→TZS, ZM→ZMW, ZW→ZWL **and** USD. The country decides which currencies an order may use. The rule is checked against the customer's stored country, never a country sent by the client.

- **Zimbabwe:** ZWL was replaced by the ZiG (ISO 4217 `ZWG`) in April 2024, so `ZWG` is also accepted. ZWL stays valid as the brief requires.
- **Common Monetary Area (ZAR, NAD, LSL, SZL):** the four currencies are pegged **1:1**, and the rand is **legal tender** in Namibia, Lesotho and Eswatini (not the other way round). Practical implications:
  - **Acceptance:** an NA, LS or SZ customer may legitimately want to pay in ZAR. `Sadc:AllowRandInCommonMonetaryArea=true` allows ZAR for those three countries. It is **off by default** so behaviour matches the brief's table exactly. The reference endpoint and the UI pick up the setting automatically.
  - **Reporting:** CMA amounts can be added up at par for ZAR-equivalent reporting without an FX feed. Mixing any *other* currencies in a total needs dated exchange rates (see ANSWERS.md, Q13b and Q20). That's why `sp_GetTransactionReport` also returns per-currency totals.
  - **Settlement:** at par is not the same as interchangeable everywhere. NAD, LSL and SZL are not legal tender in South Africa, so refunds and settlement should go back in the currency of the original order. Storing `CurrencyCode` per order, rather than per customer, keeps that correct.
- **Currency conversion** is out of scope: totals are always in the order's own currency.

---

## EF Core migrations & database lifecycle

### Migrations

Location: `SadcOrdersAssessment/DataLayer/Context/Migrations/`. A design-time factory (`DesignTimeDbContextFactory`) lets the `dotnet ef` tools run without starting the app. The target database comes from the `SADC_ORDERS_DB` environment variable, and defaults to the Compose SQL Server (`localhost,1433`). To target LocalDB: `$env:SADC_ORDERS_DB="Server=(localdb)\MSSQLLocalDB;Database=SadcOrders;Trusted_Connection=True;TrustServerCertificate=True"`.

| Migration | What | Zero-downtime safe? |
|---|---|---|
| `20261116080000_InitialCreate` | Customers, Orders, OrderLineItems; PKs, FKs (Orders→Customers **Restrict**, LineItems→Orders **Cascade**), `CHECK (Quantity > 0)`, `CHECK (UnitPrice >= 0)`, unique `IX_Customers_Email`, `IX_Orders_CustomerId_Status_CreatedAt INCLUDE (CurrencyCode, TotalAmount)`, `IX_Orders_Status_CreatedAt`; money as `decimal(18,2)` (`HasPrecision(18,2)`). | n/a |
| `20261116090000_AddOrderRowVersion` | Evolution 1: adds `Orders.RowVersion rowversion` (optimistic concurrency and ETag) and the optional `Orders.UpdatedAt`. | Yes: additive, and SQL Server fills rowversion itself. |
| `20261116100000_AddIdempotencyOutboxAndReportProc` | Evolution 2: `IdempotencyRecords`, `OutboxMessages` (filtered index on unprocessed rows), `ProcessedMessages`, and `dbo.sp_GetTransactionReport`. | Yes: new objects only. |
| `20261116110000_EnableReadCommittedSnapshot` | Turns on RCSI (non-blocking reads; see ANSWERS.md, Q17). Runs outside a transaction (`suppressTransaction`). | Yes, but `ROLLBACK IMMEDIATE` briefly drops other sessions, so run it in a quiet window. |

### Commands

```bash
dotnet tool install --global dotnet-ef --version 8.*
cd SadcOrdersAssessment

# Shorthand used below (bash). On PowerShell write the flags out in full.
EF="--project DataLayer --startup-project SadcOrdersAssessment"

dotnet ef migrations list $EF
dotnet ef migrations add <Name> $EF --output-dir Context/Migrations
dotnet ef migrations has-pending-model-changes $EF              # CI gate: model == snapshot

dotnet ef database update $EF                                   # apply all
dotnet ef database update AddIdempotencyOutboxAndReportProc $EF # roll back to a named migration (runs Down())
dotnet ef database update 0 $EF                                 # roll everything back

dotnet ef migrations script --idempotent $EF -o artifacts/sql/migrate-idempotent.sql   # for DBAs and pipelines
dotnet ef migrations script AddOrderRowVersion AddIdempotencyOutboxAndReportProc $EF -o artifacts/sql/delta.sql
dotnet ef migrations script AddIdempotencyOutboxAndReportProc AddOrderRowVersion $EF -o artifacts/sql/rollback.sql  # a "down" script
dotnet ef migrations bundle $EF --self-contained -r linux-x64 -o artifacts/efbundle     # one executable for deployment jobs
```

On PowerShell, write `--project DataLayer --startup-project SadcOrdersAssessment` out in full instead of using `$EF`.

**SQL scripts:** CI generates `migrate-idempotent.sql` on every run and publishes it as the `migration-sql` build artifact, so a DBA can review exactly what will run. It is idempotent: each migration is guarded by a check against `__EFMigrationsHistory`, so it can be run against a database at any version. The stored procedure's readable source is `db/sql/sp_GetTransactionReport.sql`.

**Where migrations run:** in Development and Compose the API runs `Database.MigrateAsync()` at startup (`Database:MigrateOnStartup`). It is **off in production**. There, migrations run as a separate pipeline step before the new app version is deployed: the reviewed idempotent script via `sqlcmd`, or the EF bundle as a deployment job. Running them from the app is risky with several replicas (they race each other), and the app's identity would need DDL rights.

### Zero-downtime migration plan

During a rolling deployment, old and new versions of the app run against the same database at the same time, so every schema change must work with **both**. I follow **expand → migrate → contract** across separate releases. **Expand:** make only additive, backwards-compatible changes first: new nullable columns, or columns with defaults, new tables, new indexes (`ONLINE = ON` where the edition supports it). Deploy that migration before the code that uses it; old code ignores the new columns. **Migrate:** deploy code that writes both old and new shapes, then **backfill** existing rows in small batches (for example, `UPDATE TOP (5000) ... WHERE NewCol IS NULL` in a loop, with short transactions so the log and locks stay small). Only once the backfill is complete and new writes always fill the column, **enforce** it (`ALTER COLUMN ... NOT NULL`, add the FK or CHECK `WITH NOCHECK` and then validate it).

**Contract** comes last, in a later release: remove the old column or table only when no running version reads it. **Renames are never done in place.** A rename is "add new → dual-write → backfill → switch reads → drop old". Code and schema are deployed separately (schema first, for additive changes) and each step can be rolled back on its own. `AddOrderRowVersion` is a small example: an additive column that SQL Server fills itself (`rowversion`), and an optional column (`UpdatedAt`) that old code simply never writes.

### Rollback strategy

- **Before every production migration:** take a backup or a restore point (on Azure SQL, point-in-time restore is automatic; note the timestamp), and check the `Down()` path. CI applies all migrations to an empty database, rolls back to `AddOrderRowVersion`, then to `0`, then applies everything again, so every `Down()` is exercised on every build.
- **Reversible changes** (everything in this repo): `dotnet ef database update <PreviousMigration>`, or run the reverse script generated with `migrations script <Current> <Previous>`. Roll back the **application first**, then the schema, and only if the old code can't run against the new schema. With expand/contract it usually can, so the schema can often stay.
- **Irreversible changes** (dropping a column or table, narrowing a type, destructive data fixes): `Down()` can recreate the structure but **not the data**. For these: (1) do them only in a *contract* release, after the data has been unused for at least one release; (2) copy the affected data to a backup table, or take a full or copy-only backup, before running; (3) if they must be undone, restore from that backup (point-in-time restore to a new database, then copy the rows back) rather than relying on `Down()`. Mark such migrations with a comment and make `Down()` throw a clear `NotSupportedException` rather than pretending to work.

### Indexes, constraints and concurrency

- `IX_Orders_CustomerId_Status_CreatedAt` (with `INCLUDE (CurrencyCode, TotalAmount)` so list queries are covering), `IX_Orders_Status_CreatedAt`, unique `IX_Customers_Email`, a filtered `IX_OutboxMessages_OccurredAt WHERE ProcessedAt IS NULL`. Reasoning in ANSWERS.md, Q14/Q15.
- Money is `decimal(18,2)` everywhere. Unit prices with more than 2 decimal places are rejected rather than silently rounded. `TotalAmount` is always computed on the server.
- `Orders.RowVersion` is used for optimistic concurrency (`DbUpdateConcurrencyException` → 409 with the current state) and as the ETag.
- Cascade only where children can't exist without the parent (line items). Customers → orders is `Restrict`.

---

## Testing

| Suite | How to run | Covers |
|---|---|---|
| Backend unit (xUnit) | `dotnet test SadcOrdersAssessment/UnitTest` | Order totals and rounding, every allowed and forbidden status transition, all 16 country↔currency pairs, CMA flag, validators (camelCase error paths, pageSize ≤ 100, 2-dp prices, sort/status whitelists). |
| Backend integration (xUnit + WebApplicationFactory + Testcontainers SQL Server) | `dotnet test SadcOrdersAssessment/SadcOrdersAssessment.IntegrationTests` (Docker running) | Real migrations applied to an empty SQL Server; customers (201/400/409/404, search, paging limits, 401/403); orders (server-side totals, outbox row + correlation id, currency↔country rule, ETag/304, filters and sort); **idempotent status update** (replay, 5 concurrent duplicates → exactly one change, key reuse 422, missing key 400); invalid transition 409; admin-only cancel of paid orders; stale If-Match 412; rowversion lost-update detection; **message publish** (outbox processor publishes once and marks processed); worker handler (fulfils, dedups redelivery, leaves cancelled orders, failed attempt leaves no partial state); health, security headers, correlation id echo, metrics, malformed JSON → Problem Details. |
| Web (Vitest + React Testing Library) | `cd SadcOrdersAssessmentFrontend && npm test` (`npm run test:coverage` for coverage) | API client (token, correlation id, Problem Details → field errors, contract mismatch, network errors); customer form (client and server validation, accessible error wiring); customer list (loading/empty states, debounced search, paging); orders list (URL filters, status/sort/paging, error alert); create order (currencies limited by the customer's country, total preview, payload, server field errors on line items); order details (line items and total, **Idempotency-Key reused on retry after a transient failure, new key after a definite rejection**, reader can't act, terminal states). |

**Test results:**
- Web (`npm run test:coverage`, 2026-11-20): **24 tests in 6 files passing; statements 82.3%, branches 79.5%, functions 74.2%, lines 83.4%.** ESLint, `tsc`, `vite build` and `npm audit --audit-level=high` are clean.
- Backend: <!-- TODO: paste the summary lines from `dotnet test SadcOrdersAssessment.slnx` here before submitting -->

CI collects backend coverage with coverlet and publishes a ReportGenerator summary on the run page; the web job uploads the lcov and cobertura reports.

**E2E (optional bonus):** not included because of the timebox. The next step would be one Playwright spec against `docker compose up`: create customer → create order → mark paid → wait for the worker → see Fulfilled.

## CI/CD

`.github/workflows/ci.yml`:

1. **backend:** restore → `dotnet format --verify-no-changes` (lint) → build → vulnerable-package check → unit tests → integration tests (Testcontainers) → coverage report → **EF `has-pending-model-changes`** → **`migrations script --idempotent`, applied twice to an empty SQL Server service container** (also runs the stored procedure) → **rollback round-trip** (`update` → `update AddOrderRowVersion` → `update 0` → `update`) → publish API and worker. Artifacts: test results and coverage, `migration-sql`, published binaries.
2. **frontend:** `npm ci` → ESLint → `tsc` → `npm audit --audit-level=high` → Vitest with coverage → `vite build`. Artifacts: `web-dist`, coverage.
3. **docker:** builds the api, worker and web images. On `main`, pushes them to GHCR tagged with the commit SHA.

Git workflow: feature branches → PR → CI must pass → squash-merge to `main`.

## Observability

- **Log files:** besides the console, the API and the worker write rolling log files with Serilog (`Program.cs` in each project). One file per day, kept for 14 days, and a new file after 10 MB. Every line includes the scope values (`CorrelationId`, `MessageId`, `OrderId`), so you can search one file for one correlation id.
  - Visual Studio / `dotnet run`: `SadcOrdersAssessment/SadcOrdersAssessment/logs/api-YYYYMMDD.log` and `SadcOrdersAssessment/SadcOrdersAssessment.Worker/logs/worker-YYYYMMDD.log`.
  - Docker Compose: `logs/api/` and `logs/worker/` in the repo root (bind-mounted volumes).
  - Settings: `FileLogging:Enabled` (default `true`) and `FileLogging:Path` (optional, overrides the location). The integration tests switch it off. Levels still come from `Logging:LogLevel`.
- JSON console logs (`AddJsonConsole` with scopes). Every line carries `CorrelationId`. The API accepts or creates `X-Correlation-ID`, stores it on the outbox row and sends it as the AMQP `correlation_id`, and the worker logs it with `MessageId` and `OrderId`.
- Metrics: request count and duration per route template on a `Meter` (`SadcOrders`) plus a `/metrics` JSON snapshot. ASP.NET Core's built-in `http.server.*` metrics are also available, e.g. `dotnet-counters monitor -n SadcOrdersAssessment --counters SadcOrders,Microsoft.AspNetCore.Hosting`.
- Health: `/healthz` (liveness), `/readiness` (SQL Server + RabbitMQ).
- Next steps: OpenTelemetry traces across HTTP → SQL → RabbitMQ → worker, exported to Azure Monitor; SLOs and alerts as described in ANSWERS.md, Q11.

## Assumptions and trade-offs

- **Worker transitions:** on `OrderCreated` the worker simulates payment capture and allocation, and moves the order `Pending → Paid → Fulfilled` through valid transitions, rather than jumping from Pending to Fulfilled. An order that is cancelled before the worker runs is left cancelled.
- **Search** uses `LIKE '%term%'`, which is fine at this size. At scale I'd switch to prefix search or full-text indexing (ANSWERS.md, Q6).
- **Clustered key:** GUID PKs are clustered (EF's default). At millions of rows I'd move to sequential GUIDs or cluster on `(CreatedAt, Id)` (ANSWERS.md, Q14/Q19).
- **Mock-auth secrets** in `appsettings.Development.json` and `docker-compose.yml` are for local development only. Real deployments use Entra, with secrets in Key Vault or user secrets.
- **Skipped for the timebox:** the optional GraphQL endpoint, Playwright E2E, currency conversion, and a cleanup job for old `IdempotencyRecords` and processed outbox rows (both have `CreatedAt`/`ProcessedAt` indexes ready for it).
