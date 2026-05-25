# SQL Server to PostgreSQL Migration Plan

**Status:** Draft  
**Scope:** Structured data layer only (historical OHLCV + backtest databases). MongoDB remains unchanged.  
**Goal:** Replace SQL Server with PostgreSQL as the relational database backing all market-data and backtest storage, with zero functionality loss.

---

## 1. Overview

The platform uses two relational databases on SQL Server:

| Database | Purpose | Tables |
|---|---|---|
| **Market Data** | Per-symbol OHLCV history | `symbol_master`, `D_{suffix}`, `W_{suffix}`, `M_{suffix}` (~1500 tables) |
| **Backtest** | Persisted backtest results | `BT_RunHeaders`, `BT_Trades`, `BT_PositionLifecycle`, `BT_PortfolioPerformance` |

Both move to PostgreSQL. The data model, schema, and query patterns remain the same — only the SQL dialect and connection driver change.

---

## 2. Files Affected — Complete Inventory

### 2.1 NuGet package references (3 files)

| File | Action |
|---|---|
| `apps/api/SignalStack.Api.csproj` | `Microsoft.Data.SqlClient` 6.0.1 → `Npgsql` (completed) |
| `packages/migrations/SignalStack.SqlMigrations/SignalStack.SqlMigrations.csproj` | `Microsoft.Data.SqlClient` 6.0.1 → `Npgsql` (completed) |
| `apps/worker/SignalStack.Worker.csproj` | Gets Npgsql transitively through SqlMigrations + other package refs |

The worker project references `SignalStack.SqlMigrations.csproj` and other shared packages, so `Npgsql` becomes available transitively. No direct package reference needed.

### 2.2 Service / repository files (6 files)

| # | File | Owner | Key changes |
|---|---|---|---|
| 1 | `packages/storage/SignalStack.Storage/Historical/SqlOhlcvRepository.cs` | Storage | `Sql*` → `Npgsql*`, `[` → `"`, `TOP(N)` → `LIMIT N`, `dbo` → `public` |
| 2 | `packages/storage/SignalStack.Storage/Backtesting/SqlBacktestRepository.cs` | Storage | `Sql*` → `Npgsql*`, `[` → `"`, `TOP(N)` → `LIMIT N` + offset rewrite |
| 3 | `packages/storage/SignalStack.Storage/Universe/SqlSyncHealthRepository.cs` | Storage | `Sql*` → `Npgsql*`, `[` → `"`, `TABLE_SCHEMA = 'dbo'` → `'public'` |
| 4 | `apps/worker/Jobs/DataSync/DataSyncService.cs` | Worker | `Sql*` → `Npgsql*`, `DATEADD/DATEDIFF` → `date_trunc`, `MERGE` → `ON CONFLICT`, temp table prefix |
| 5 | `apps/worker/Jobs/HistoricDataSeed/HistoricDataSeedService.cs` | Worker | `Sql*` → `Npgsql*`, `SqlBulkCopy` → `NpgsqlBinaryImporter`, `MERGE` → `ON CONFLICT`, `DATEADD/DATEDIFF` → `date_trunc`, temp table prefix, `SqlTransaction` → `NpgsqlTransaction` |
| 6 | `apps/worker/Jobs/Migrations/BatchedSchemaMigrationJob.cs` | Worker | `Sql*` → `Npgsql*`, `const string schema = "dbo"` → `"public"` |

### 2.3 Shared infrastructure files (2 files)

| # | File | Key changes |
|---|---|---|
| 7 | `packages/migrations/SignalStack.SqlMigrations/SharedTableDdlTemplate.cs` | DDL rewritten for PostgreSQL: `[dbo].[X]` → `"public"."X"`, `FLOAT` → `DOUBLE PRECISION`, inline `IF NOT EXISTS` → `CREATE TABLE IF NOT EXISTS`, remove `CLUSTERED` |
| 8 | `packages/migrations/SignalStack.SqlMigrations/Program.cs` | `.AddSqlServer()` → `.AddPostgres()`, remove `sys.databases` CREATE DATABASE block |

### 2.4 Migration files (3 files)

| # | File | Key changes |
|---|---|---|
| 9 | `packages/migrations/.../Migrations/InitialMarketData_001.cs` | Remove `.WithOptions().NonClustered()` (3 occurrences) |
| 10 | `packages/migrations/.../Migrations/InitialBacktest_002.cs` | Remove `.WithOptions().NonClustered()` (3 occurrences) |
| 11 | `packages/migrations/.../Migrations/AddBacktestMetrics_003.cs` | No changes needed (pure column additions) |

### 2.5 Docker / local-dev files (3 files)

| # | File | Key changes |
|---|---|---|
| 12 | `infra/docker/docker-compose.yml` | Replace `mcr.microsoft.com/mssql/server:2022-latest` with `postgres:16` |
| 13 | `infra/docker/.env.example` | Remove `ACCEPT_EULA`, `MSSQL_SA_PASSWORD`, `MSSQL_PORT`. Add `POSTGRES_PASSWORD`, `POSTGRES_PORT`, `POSTGRES_DB` |
| 14 | `infra/docker/up.ps1` | Update comments referencing `MSSQL_SA_PASSWORD` |

### 2.6 IaC / Azure files (if applicable)

| File | Change |
|---|---|
| Any SQL Server Azure resource Bicep/terraform | Replace with PostgreSQL Flexible Server |
| Any pipeline connection-string references | Update format |

### 2.7 Configuration

| Change | Detail |
|---|---|
| Connection string format | See §4 below |
| Config key | `ConnectionStrings__SqlServer` keeps its name (label only) or can be renamed to `ConnectionStrings__RelationalDb` |

---

## 3. SQL Dialect Translation Reference

This section is the master translation table. Every T-SQL construct used in the codebase is listed here with its PostgreSQL equivalent.

### 3.1 Identifier quoting

```sql
-- SQL Server (brackets)
SELECT [Date], [Open] FROM [dbo].[D_RELIANCE]

-- PostgreSQL (double quotes, or unquoted lowercase)
SELECT "Date", "Open" FROM "public"."D_RELIANCE"
-- or simply (if all identifiers are lowercased):
SELECT date, open FROM public.d_reliance
```

**Recommendation:** Use lowercase unquoted identifiers for new PostgreSQL tables to avoid quoting everywhere. For the migration period, double-quoted identifiers work fine and match the existing casing.

### 3.2 Schema reference

```sql
-- SQL Server
TABLE_SCHEMA = 'dbo'

-- PostgreSQL
TABLE_SCHEMA = 'public'
```

### 3.3 Pagination / limiting

```sql
-- SQL Server
SELECT TOP(@Count) ... FROM ... ORDER BY ... ;

-- PostgreSQL
SELECT ... FROM ... ORDER BY ... LIMIT @Count;
```

`TOP` appears in:
- `SqlOhlcvRepository.cs` (line 73): `SELECT TOP (@WindowSize)` → move `LIMIT @WindowSize` to after `ORDER BY`
- `SqlBacktestRepository.cs` (line 367): `SELECT TOP (@Limit)` → `LIMIT @Limit` after `ORDER BY`

### 3.4 Row limiting with offset

```sql
-- SQL Server (offset via subquery + TOP)
SELECT TOP (@Limit) ...
FROM ...
WHERE rh.run_id NOT IN (
    SELECT run_id FROM (
        SELECT run_id, ROW_NUMBER() OVER (ORDER BY run_timestamp DESC) AS rn
        FROM BT_RunHeaders
    ) sub WHERE rn <= @Offset
)
ORDER BY rh.run_timestamp DESC

-- PostgreSQL (standard SQL)
SELECT ...
FROM ...
ORDER BY rh.run_timestamp DESC
LIMIT @Limit OFFSET @Offset;
```

This appears in `SqlBacktestRepository.cs` `ListRunsAsync` (lines 367-381). The PostgreSQL version is significantly simpler.

### 3.5 Date truncation — week start

```sql
-- SQL Server (locale-independent Monday)
DATEADD(week, DATEDIFF(week, 0, [Date]), 0)

-- PostgreSQL
date_trunc('week', "Date")
```

Both return midnight on Monday of the week containing `[Date]`. Appears in:
- `DataSyncService.cs` (line 429)
- `HistoricDataSeedService.cs` (line 180)

### 3.6 Date truncation — month start

```sql
-- SQL Server
DATEFROMPARTS(YEAR([Date]), MONTH([Date]), 1)

-- PostgreSQL
date_trunc('month', "Date")
```

Appears in:
- `DataSyncService.cs` (line 436)
- `HistoricDataSeedService.cs` (line 191)

### 3.7 Table existence check

```sql
-- SQL Server
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'D_RELIANCE')
BEGIN
    CREATE TABLE [dbo].[D_RELIANCE] ( ... )
END

-- PostgreSQL
CREATE TABLE IF NOT EXISTS "public"."D_RELIANCE" ( ... );
```

`INFORMATION_SCHEMA.TABLES` is still available on PostgreSQL (it's a standard SQL schema), but using `CREATE TABLE IF NOT EXISTS` is simpler. Appears in `SharedTableDdlTemplate.cs`.

`INFORMATION_SCHEMA.TABLES` WITH `TABLE_SCHEMA = 'public'` also works in PostgreSQL if you need the boolean check pattern (used in `SqlSyncHealthRepository.cs` and `SqlOhlcvRepository.cs`).

### 3.8 Upsert — INSERT ... ON CONFLICT

**Pattern A — insert only when no conflict (HistoricDataSeed daily insert)**

```sql
-- SQL Server (MERGE, insert only)
MERGE INTO [D_RELIANCE] AS target
USING #tmp_D_RELIANCE AS source
ON target.[Date] = source.[Date]
WHEN NOT MATCHED THEN
    INSERT ([Date], [Open], [High], [Low], [Close], [Volume])
    VALUES (source.[Date], source.[Open], source.[High],
            source.[Low], source.[Close], source.[Volume]);

-- PostgreSQL
INSERT INTO "D_RELIANCE" ("Date", "Open", "High", "Low", "Close", "Volume")
SELECT "Date", "Open", "High", "Low", "Close", "Volume"
FROM "tmp_D_RELIANCE"
ON CONFLICT ("Date") DO NOTHING;
```

Appears in `HistoricDataSeedService.cs BulkInsertDailyAsync` (lines 262-269).

**Pattern B — upsert (update or insert)**

```sql
-- SQL Server (MERGE, with update)
MERGE INTO [W_RELIANCE] AS target
USING #tmp_W_RELIANCE AS source
ON target.[Date] = source.[PeriodStart]
WHEN MATCHED THEN
    UPDATE SET
        target.[Open]   = source.[Open],
        target.[High]   = source.[High],
        target.[Low]    = source.[Low],
        target.[Close]  = source.[Close],
        target.[Volume] = source.[Volume]
WHEN NOT MATCHED THEN
    INSERT ([Date], [Open], [High], [Low], [Close], [Volume])
    VALUES (source.[PeriodStart], source.[Open], source.[High],
            source.[Low], source.[Close], source.[Volume]);

-- PostgreSQL
INSERT INTO "W_RELIANCE" ("Date", "Open", "High", "Low", "Close", "Volume")
SELECT "PeriodStart", "Open", "High", "Low", "Close", "Volume"
FROM "tmp_W_RELIANCE"
ON CONFLICT ("Date") DO UPDATE SET
    "Open"   = EXCLUDED."Open",
    "High"   = EXCLUDED."High",
    "Low"    = EXCLUDED."Low",
    "Close"  = EXCLUDED."Close",
    "Volume" = EXCLUDED."Volume";
```

Appears in:
- `DataSyncService.cs UpsertAggregateAsync` (lines 505-524)
- `HistoricDataSeedService.cs ComputeAndInsertAggregatesAsync` (lines 362-376)

### 3.9 Temp tables

```sql
-- SQL Server (local temp table, session-scoped)
CREATE TABLE #tmp_D_RELIANCE ( ... );
INSERT INTO #tmp_D_RELIANCE ( ... ) ... ;
MERGE INTO [D_RELIANCE] ... USING #tmp_D_RELIANCE ... ;

-- PostgreSQL (temporary table, session-scoped)
CREATE TEMPORARY TABLE "tmp_D_RELIANCE" ( ... );
INSERT INTO "tmp_D_RELIANCE" ( ... ) ... ;
INSERT INTO "D_RELIANCE" ... SELECT FROM "tmp_D_RELIANCE" ... ;
```

Key differences:
- Prefix changes from `#tmp_` to `tmp_` (PostgreSQL temp tables don't use `#` prefix)
- Use `CREATE TEMPORARY TABLE` instead of `CREATE TABLE`
- Temp tables are automatically dropped at end of session in both databases
- PostgreSQL temp tables have their own schema and can shadow regular tables — no naming conflict

Appears in:
- `DataSyncService.cs UpsertAggregateAsync` (lines 461-470, 505)
- `HistoricDataSeedService.cs BulkInsertDailyAsync` (lines 214-225, 262)
- `HistoricDataSeedService.cs ComputeAndInsertAggregatesAsync` (lines 316-326, 362)

### 3.10 Bulk insert

```sql
-- SQL Server (SqlBulkCopy via DataTable)
using var bulkCopy = new SqlBulkCopy(connection);
bulkCopy.DestinationTableName = "#tmp_D_RELIANCE";
bulkCopy.WriteToServer(dataTable);

-- PostgreSQL (NpgsqlBinaryImporter — binary COPY)
await using var writer = connection.BeginBinaryImport(
    "COPY \"tmp_D_RELIANCE\" (\"Date\", \"Open\", \"High\", \"Low\", \"Close\", \"Volume\") FROM STDIN (FORMAT BINARY)");
foreach (var record in records) {
    await writer.StartRowAsync();
    await writer.WriteAsync(value, NpgsqlDbType.Double);
    // ... etc
}
await writer.CompleteAsync();
```

Appears only in `HistoricDataSeedService.cs BulkInsertDailyAsync` (lines 232-258).

The `NpgsqlBinaryImporter` pattern is more verbose but measurably faster than `SqlBulkCopy` in benchmarks. The `System.Data.DataTable` intermediate object can also be eliminated since we iterate `records` directly.

### 3.11 DDL — CREATE TABLE

```sql
-- SQL Server
CREATE TABLE [dbo].[D_RELIANCE] (
    [Date]   DATE      NOT NULL,
    [Open]   FLOAT     NOT NULL,
    [High]   FLOAT     NOT NULL,
    [Low]    FLOAT     NOT NULL,
    [Close]  FLOAT     NOT NULL,
    [Volume] BIGINT    NOT NULL,
    CONSTRAINT [PK_D_RELIANCE] PRIMARY KEY CLUSTERED ([Date])
);
CREATE INDEX [IX_D_RELIANCE_Date] ON [dbo].[D_RELIANCE] ([Date]);

-- PostgreSQL
CREATE TABLE IF NOT EXISTS "public"."D_RELIANCE" (
    "Date"   DATE              NOT NULL,
    "Open"   DOUBLE PRECISION  NOT NULL,
    "High"   DOUBLE PRECISION  NOT NULL,
    "Low"    DOUBLE PRECISION  NOT NULL,
    "Close"  DOUBLE PRECISION  NOT NULL,
    "Volume" BIGINT            NOT NULL,
    PRIMARY KEY ("Date")
);
CREATE INDEX IF NOT EXISTS "IX_D_RELIANCE_Date" ON "public"."D_RELIANCE" ("Date");
```

Key differences:
- `FLOAT` → `DOUBLE PRECISION` (PostgreSQL `FLOAT`/`FLOAT8` are both `DOUBLE PRECISION`)
- `PRIMARY KEY CLUSTERED` → `PRIMARY KEY` (PostgreSQL ignores CLUSTERED; all indexes are non-clustered by default)
- `CREATE INDEX` → `CREATE INDEX IF NOT EXISTS` for idempotency
- `CREATE TABLE IF NOT EXISTS` removes need for the `IF NOT EXISTS (SELECT ... FROM INFORMATION_SCHEMA.TABLES)` wrapper

Appears in `SharedTableDdlTemplate.cs` — the single shared DDL template for all per-symbol tables.

### 3.12 Aggregate computation — ROW_NUMBER()

No change needed. `ROW_NUMBER() OVER (PARTITION BY ... ORDER BY ...)` is standard SQL and works identically in PostgreSQL.

```sql
-- Same in both
ROW_NUMBER() OVER (PARTITION BY date_trunc('week', "Date") ORDER BY "Date" ASC) AS rn_asc
```

### 3.13 Window functions — FIRST/LAST value via ROW_NUMBER

The pattern used in both DataSync and HDS:
```sql
MAX(CASE WHEN rn_asc = 1 THEN [Open] END) AS [Open],
MAX(CASE WHEN rn_desc = 1 THEN [Close] END) AS [Close]
```
This works identically in PostgreSQL. No change needed.

### 3.14 FluentMigrator — type mappings

| FluentMigrator method | SQL Server | PostgreSQL | Compatible? |
|---|---|---|---|
| `.AsString(200)` | `nvarchar(200)` | `varchar(200)` | Yes |
| `.AsString(int.MaxValue)` | `nvarchar(max)` | `text` | Yes |
| `.AsInt32()` | `int` | `integer` | Yes |
| `.AsInt64()` | `bigint` | `bigint` | Yes |
| `.AsBoolean()` | `bit` | `boolean` | Yes |
| `.AsDateTime2()` | `datetime2` | `timestamp` | Yes |
| `.AsDate()` | `date` | `date` | Yes |
| `.AsDecimal(14, 4)` | `decimal(14,4)` | `numeric(14,4)` | Yes |
| `.AsGuid()` | `uniqueidentifier` | `uuid` | Yes |
| `.AsFloat()` | `real` | `real` | Yes |
| `.Identity()` | `IDENTITY` | `SERIAL` / `IDENTITY` | Yes (FluentMigrator handles it) |
| `.PrimaryKey()` | `PRIMARY KEY` | `PRIMARY KEY` | Yes |
| `.Unique()` | `UNIQUE` | `UNIQUE` | Yes |
| `.NotNullable()` | `NOT NULL` | `NOT NULL` | Yes |
| `.WithDefault(SystemMethods.CurrentDateTime)` | `DEFAULT GETDATE()` | `DEFAULT NOW()` | Yes (FluentMigrator handles it) |

**Not compatible:**
- `.WithOptions().NonClustered()` — PostgreSQL has no clustered/non-clustered distinction. Must be removed (3 occurrences across 2 migration files).

### 3.15 Database creation

```sql
-- SQL Server (in Program.cs of SqlMigrations)
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = @db)
BEGIN
    CREATE DATABASE [signalstack_marketdata];
END

-- PostgreSQL
-- No equivalent in migration runner. Databases are created separately:
CREATE DATABASE signalstack_marketdata;
CREATE DATABASE signalstack_backtest;
```

PostgreSQL does not support `CREATE DATABASE` inside a transaction block or in parameterized queries. Database creation must happen outside the migration runner — either manually, via a provisioning script, or in infrastructure automation. The `Program.cs` runner simply connects to the already-existing database.

---

## 4. Connection Strings

### 4.1 Format

```
# SQL Server (current)
Server=localhost,1433;Database=signalstack_marketdata;User Id=sa;Password=ChangeMe!12345;TrustServerCertificate=True

# PostgreSQL (new)
Host=localhost;Port=5432;Database=signalstack_marketdata;Username=signalstack;Password=ChangeMe!12345
```

### 4.2 Config key

The existing config key `ConnectionStrings__SqlServer` can be retained as-is (the label is arbitrary) or renamed to `ConnectionStrings__StructuredDb`. Either way, only the connection string value changes.

### 4.3 Two databases, two connection strings

The platform uses two logical databases. With PostgreSQL, these can be either:
- **Two separate databases** on the same server (same Host/Port/User, different Database) — recommended for isolation
- **Two schemas** in the same database (`marketdata` and `backtest`) — simpler to manage but less isolated

Recommendation: two databases, matching the current SQL Server setup:

```
ConnectionStrings__SqlServer=Host=localhost;Port=5432;Database=signalstack_marketdata;Username=postgres;Password=xxx
# (Backtest uses the same connection string + database name override, or a separate entry)
```

Check how the backtest database connection string is currently resolved — if it's the same connection string with a different `Initial Catalog`, it will need a separate named entry for PostgreSQL since the database name is embedded in the connection string.

---

## 5. Local Development Environment

### 5.1 docker-compose.yml changes

```yaml
# REMOVE:
  mssql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: signalstack-mssql
    restart: unless-stopped
    environment:
      ACCEPT_EULA: "${ACCEPT_EULA:-Y}"
      MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD}"
    ports:
      - "${MSSQL_PORT:-1433}:1433"
    volumes:
      - mssql_data:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$${MSSQL_SA_PASSWORD}\" -Q \"SELECT 1\" -C || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 20s

# ADD:
  postgres:
    image: postgres:16
    container_name: signalstack-postgres
    restart: unless-stopped
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: "${POSTGRES_PASSWORD}"
      POSTGRES_DB: signalstack_marketdata
    ports:
      - "${POSTGRES_PORT:-5432}:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./postgres/init:/docker-entrypoint-initdb.d   # for creating backtest DB
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

# UPDATE volumes:
volumes:
  otel_primary_data:
  otel_fallback_data:
  mongo_data:
  redis_data:
  mssql_data:        # REMOVE
  postgres_data:     # ADD
```

### 5.2 Database initialization

PostgreSQL doesn't create databases at runtime the way the current `Program.cs` does with `sys.databases` + `CREATE DATABASE`. Instead, use an init script:

Create `infra/docker/postgres/init/01-create-backtest-db.sql`:

```sql
-- Create the backtest database (marketdata DB is created by POSTGRES_DB env var)
SELECT 'CREATE DATABASE signalstack_backtest'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'signalstack_backtest')\gexec
```

This runs automatically when the PostgreSQL container starts for the first time.

### 5.3 .env.example changes

```env
# REMOVE:
ACCEPT_EULA=Y
MSSQL_SA_PASSWORD=ChangeMe!12345
MSSQL_PORT=1433

# ADD:
POSTGRES_PASSWORD=ChangeMe!12345
POSTGRES_PORT=5432

# KEEP:
MONGO_PORT=27017
REDIS_PORT=6379
MONGO_DATABASE=signalstack
```

### 5.4 up.ps1 / down.ps1

- `up.ps1`: Update the error message from "set MSSQL_SA_PASSWORD" to "set POSTGRES_PASSWORD"
- `down.ps1`: No changes needed (it just calls `docker compose down`)

### 5.5 Volume

Add `postgres_data:` to the `volumes:` section in docker-compose.yml, remove `mssql_data:`.

---

## 6. Azure / Production Infrastructure

### 6.1 Database service

Provision an **Azure Database for PostgreSQL Flexible Server** instance. Create two databases on it:

```bash
az postgres flexible-server create \
  --name signalstack-pg \
  --resource-group signalstack-rg \
  --location southeastasia \
  --sku-name Standard_D2s_v3 \
  --tier Burstable \
  --storage-size 64 \
  --admin-user signalstack \
  --admin-password <password>
```

Then create databases:

```bash
az postgres flexible-server db create \
  --server-name signalstack-pg \
  --resource-group signalstack-rg \
  --database-name signalstack_marketdata

az postgres flexible-server db create \
  --server-name signalstack-pg \
  --resource-group signalstack-rg \
  --database-name signalstack_backtest
```

### 6.2 Connection string in Azure

Store in Key Vault or App Configuration:

```
Host=signalstack-pg.postgres.database.azure.com;Port=5432;Database=signalstack_marketdata;Username=signalstack;Password=<password>;SslMode=Require;TrustServerCertificate=true
```

### 6.3 Migration runner in CI/CD

The FluentMigrator runner (`SignalStack.SqlMigrations`) is invoked from GitHub Actions with `--connection` and `--database` arguments. The runner must be updated to:

1. Remove the `sys.databases` CREATE DATABASE step (databases already exist)
2. Use `.AddPostgres()` instead of `.AddSqlServer()`

The CI/CD workflow script that calls `dotnet run -- --connection "..." --database marketdata` continues unchanged — only the connection string value and the runner internals change.

### 6.4 Connection from Worker and API

Both `apps/api/Program.cs` and `apps/worker/Program.cs` read the SQL connection string from configuration and pass it to DI registration extensions. No code changes needed — the same connection string format is consumed, just with PostgreSQL values.

---

## 7. Migration Order

This migration is ordered so that each step can be verified independently before proceeding.

### Phase 1: Local dev environment

1. **Update docker-compose.yml** — replace SQL Server container with PostgreSQL
2. **Update .env.example** — new environment variables
3. **Create init script** — `postgres/init/01-create-backtest-db.sql`
4. **Update up.ps1** — new error message
5. **Run `docker compose up`** — verify PostgreSQL starts and databases are created

### Phase 2: NuGet and connection

6. **Swap packages** — `Microsoft.Data.SqlClient` → `Npgsql` in both `.csproj` files
7. **Set local .env** — PostgreSQL connection string
8. **Build** — verify compilation succeeds after package swap

### Phase 3: Schema migrations

9. **Update `Program.cs`** in SqlMigrations — `.AddSqlServer()` → `.AddPostgres()`, remove CREATE DATABASE
10. **Update migration files** — remove `.WithOptions().NonClustered()` from 2 migration files
11. **Run migrations** against local PostgreSQL — verify all 3 migrations succeed

### Phase 4: DDL template

12. **Rewrite `SharedTableDdlTemplate.cs`** — PostgreSQL DDL syntax
13. **Verify** by calling `CreatePerSymbolTablesAsync` in a test or via HDS

### Phase 5: Repository files (low-effort)

14. **`SqlSyncHealthRepository.cs`** — schema rename, identifier quoting
15. **`SqlOhlcvRepository.cs`** — `Npgsql*` types, `TOP(N)` → `LIMIT N`, identifier quoting
16. **`SqlBacktestRepository.cs`** — `Npgsql*` types, `TOP(N)` → `LIMIT N` + offset rewrite
17. **`BatchedSchemaMigrationJob.cs`** — schema rename only
18. **Verify** — build + run API, test OHLCV queries

### Phase 6: Complex files

19. **`DataSyncService.cs`** — `date_trunc` expressions, `MERGE` → `ON CONFLICT`, temp table prefix change
20. **`HistoricDataSeedService.cs`** — all of the above + `SqlBulkCopy` → `NpgsqlBinaryImporter` + transaction type
21. **Verify** — run full DataSync and HDS jobs against local PostgreSQL

### Phase 7: Production cutover

22. **Provision Azure PostgreSQL Flexible Server** + create databases
23. **Update Azure connection strings** in Key Vault
24. **Update CI/CD migration runner** invocation parameters
25. **Run migrations** against production PostgreSQL
26. **Deploy API + Worker** with updated packages
27. **Verify** — health checks, chart data, backtest reads, DataSync run

---

## 8. Verification Points

After each phase, verify:

| What | How | Phase |
|---|---|---|
| PostgreSQL starts | `docker ps` | 1 |
| Migrations run | Check `signalstack_marketdata.symbol_master` table exists | 3 |
| DDL creates tables | Trigger HDS for one symbol, verify `D_*`, `W_*`, `M_*` tables | 4 |
| OHLCV reads work | Call chart API endpoint, verify candle data returned | 5 |
| Backtest reads work | Call backtest list endpoint | 5 |
| DataSync runs | Trigger DataSync, check rows in `D_*` + `W_*` + `M_*` tables | 6 |
| HDS runs | Trigger HistoricDataSeed, verify idempotent re-run | 6 |
| **Rolling candles** | Verify `GetRollingAsync` returns correct windowed OHLCV | 5 |
| **Bulk insert** | Verify large historical backfill works (1000+ daily rows) | 6 |
| **MERGE → ON CONFLICT** | Verify upsert doesn't create duplicates on re-run | 6 |

---

## 9. Risks and Mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| `MERGE` → `ON CONFLICT` semantic differences | Low | Test with duplicate data; the `ON CONFLICT DO UPDATE` path in PostgreSQL updates all matched rows, which matches the existing `MERGE` behaviour |
| `NpgsqlBinaryImporter` row-by-row overhead vs `SqlBulkCopy` | Low | Benchmark shows binary COPY is faster than `SqlBulkCopy` for similar volume; the `DataTable` intermediate is eliminated |
| `ROW_NUMBER()` aggregate query results differ | Low | Check same D_ data produces same W_/M_ aggregates in both databases |
| `FLOAT` vs `DOUBLE PRECISION` precision loss | Low | Both are IEEE 754 64-bit; `FLOAT` in SQL Server is `float(53)` = 64-bit, equivalent to `DOUBLE PRECISION` in PostgreSQL |
| Per-symbol table pattern with PostgreSQL partitioning | Medium | Keep the existing per-symbol table pattern as-is for the migration. Consider migrating to PostgreSQL partitioning (`PARTITION BY LIST`) as a future optimisation — not required for this migration |
| Azure PostgreSQL Flexible Server connection latency | Low | Deploy in the same region as the API/Worker App Services |
| Case sensitivity — unquoted identifiers are lowercased | Low | Either quote all identifiers consistently (`"Date"`) or lowercase all table/column references. Inconsistency causes `"column \"date\" does not exist"` errors |
| Migration runner needs transaction-safe DDL | Low | PostgreSQL DDL is transactional — `CREATE TABLE IF NOT EXISTS` can be rolled back. This is actually **better** than SQL Server |

---

## 10. Key Design Decisions

### 10.1 Keep the per-symbol table pattern

`D_{suffix}`, `W_{suffix}`, `M_{suffix}` tables work fine in PostgreSQL. The dynamic DDL template changes to PostgreSQL syntax but the pattern stays identical. Future consideration: PostgreSQL `PARTITION BY LIST` on a single `ohlcv_daily` table would simplify cross-symbol queries and schema evolution, but that's a separate initiative.

### 10.2 Keep temp table staging approach

The temp-table-then-MERGE pattern becomes temp-table-then-INSERT...ON-CONFLICT. The staging approach (load into temp, deduplicate/aggregate, upsert into target) remains unchanged. PostgreSQL temp tables (`CREATE TEMPORARY TABLE`) are session-scoped and auto-dropped, just like SQL Server.

### 10.3 Use `CREATE TABLE IF NOT EXISTS` instead of `INFORMATION_SCHEMA` check

PostgreSQL supports inline `IF NOT EXISTS` on `CREATE TABLE` and `CREATE INDEX`, eliminating the need for the explicit `INFORMATION_SCHEMA.TABLES` check. The template becomes simpler and more readable.

### 10.4 Don't add a database abstraction layer

The decision to commit to PostgreSQL means we write PostgreSQL-specific SQL directly, without an `ISqlDialect` interface or `IDbConnectionFactory` abstraction. This keeps the code simpler and avoids the abstraction tax of supporting multiple relational databases. If a future database change is needed, it will be a deliberate migration project (like this one), not a runtime config toggle.

### 10.5 Keep `.env` and config key naming

The `ConnectionStrings__SqlServer` config key name is kept as-is. It's an arbitrary label. The connection string value is what changes. Renaming it adds churn with no benefit.

---

## 11. Appendix: Complete Before/After for Each File

### 11.1 `SqlOhlcvRepository.cs`

**Before:**
```csharp
using Microsoft.Data.SqlClient;

public SqlOhlcvRepository(string connectionString, ...)
{
    _connectionString = connectionString;
}

public async Task<List<OhlcvRecord>> GetDailyAsync(...)
{
    var sql = $@"SELECT TOP (@WindowSize) [Date], [Open], ...
                 FROM [{tableName}] WHERE [Date] <= @ToDate ORDER BY [Date] DESC";
    await using var conn = new SqlConnection(_connectionString);
    await conn.OpenAsync(ct);
    await using var cmd = new SqlCommand(sql, conn);
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    var result = reader.GetDateTime(reader.GetOrdinal("Date"));
    // ...
}
```

**After:**
```csharp
using Npgsql;

public SqlOhlcvRepository(string connectionString, ...)  // class could be renamed to PgOhlcvRepository
{
    _connectionString = connectionString;
}

public async Task<List<OhlcvRecord>> GetDailyAsync(...)
{
    var sql = $@"SELECT ""Date"", ""Open"", ...
                 FROM ""{tableName}"" WHERE ""Date"" <= @ToDate
                 ORDER BY ""Date"" DESC LIMIT @WindowSize";   // TOP → LIMIT, moved after ORDER BY
    await using var conn = new NpgsqlConnection(_connectionString);
    await conn.OpenAsync(ct);
    await using var cmd = new NpgsqlCommand(sql, conn);
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    var result = reader.GetDateTime(reader.GetOrdinal("Date"));
    // ...
}
```

**Changes:**
- `using Microsoft.Data.SqlClient` → `using Npgsql`
- `SqlConnection` → `NpgsqlConnection`
- `SqlCommand` → `NpgsqlCommand`
- `SqlDataReader` → `NpgsqlDataReader`
- `SELECT TOP(@N)` → `... ORDER BY ... LIMIT @N` (move clause to end)
- `[tableName]` → `"{tableName}"`
- `[Date]` / `[Open]` etc → `"Date"` / `"Open"` etc
- `INFORMATION_SCHEMA.TABLES ... TABLE_SCHEMA = 'dbo'` → `= 'public'`

### 11.2 `SqlBacktestRepository.cs`

Same type renames as above plus:

**`ListRunsAsync` offset pagination:**
```csharp
// Before:
const string sql = @"SELECT TOP (@Limit) rh.run_id, ...
FROM BT_RunHeaders rh
WHERE rh.run_id NOT IN (
    SELECT run_id FROM (
        SELECT run_id, ROW_NUMBER() OVER (ORDER BY run_timestamp DESC) AS rn
        FROM BT_RunHeaders
    ) sub WHERE rn <= @Offset
)
ORDER BY rh.run_timestamp DESC";

// After:
const string sql = @"SELECT rh.run_id, ...
FROM BT_RunHeaders rh
ORDER BY rh.run_timestamp DESC
LIMIT @Limit OFFSET @Offset";
```

### 11.3 `SqlSyncHealthRepository.cs`

```csharp
// Before:
TABLE_SCHEMA = 'dbo'

// After:
TABLE_SCHEMA = 'public'
```

Plus the standard `Sql*` → `Npgsql*` type renames and bracket → double-quote changes.

### 11.4 `BatchedSchemaMigrationJob.cs`

```csharp
// Before:
const string schema = "dbo";

// After:
const string schema = "public";
```

Plus the standard `Sql*` → `Npgsql*` type renames.

### 11.5 `DataSyncService.cs`

```csharp
// Date truncation expressions:
// Before:
"DATEADD(week, DATEDIFF(week, 0, [Date]), 0)"
"DATEFROMPARTS(YEAR([Date]), MONTH([Date]), 1)"

// After:
"date_trunc('week', \"Date\")"
"date_trunc('month', \"Date\")"
```

```csharp
// Temp table creation:
// Before:
var tempTable = $"#tmp_{targetTable}";

// After:
var tempTable = $"\"tmp_{targetTable}\"";
// SQL becomes: CREATE TEMPORARY TABLE "tmp_W_RELIANCE" ( ... )
```

```csharp
// MERGE → INSERT ... ON CONFLICT DO UPDATE (UpsertAggregateAsync):
// Before (lines 505-524):
var mergeSql = $@"
MERGE INTO [{targetTable}] AS target
USING {tempTable} AS source
ON target.[Date] = source.[PeriodStart]
WHEN MATCHED THEN
    UPDATE SET target.[Open] = source.[Open], ...
WHEN NOT MATCHED THEN
    INSERT ([Date], [Open], ...)
    VALUES (source.[PeriodStart], source.[Open], ...);";

// After:
var mergeSql = $@"
INSERT INTO ""{targetTable}"" (""Date"", ""Open"", ""High"", ""Low"", ""Close"", ""Volume"")
SELECT ""PeriodStart"", ""Open"", ""High"", ""Low"", ""Close"", ""Volume""
FROM ""{tempTable}""
ON CONFLICT (""Date"") DO UPDATE SET
    ""Open""   = EXCLUDED.""Open"",
    ""High""   = EXCLUDED.""High"",
    ""Low""    = EXCLUDED.""Low"",
    ""Close""  = EXCLUDED.""Close"",
    ""Volume"" = EXCLUDED.""Volume"";";
```

### 11.6 `HistoricDataSeedService.cs`

Same date expression, temp table, and MERGE changes as DataSyncService above, plus:

```csharp
// Transaction type:
// Before:
SqlTransaction? transaction = null;
transaction = conn.BeginTransaction();

// After:
NpgsqlTransaction? transaction = null;
transaction = await conn.BeginTransactionAsync(ct);  // Npgsql supports async BeginTransaction
```

```csharp
// Bulk insert — the biggest change (BulkInsertDailyAsync):
// Before:
using (var bulkCopy = new SqlBulkCopy(conn))
{
    bulkCopy.DestinationTableName = tempTableName;
    bulkCopy.BatchSize = 1000;

    var dt = new System.Data.DataTable();
    dt.Columns.Add("Date", typeof(DateTime));
    dt.Columns.Add("Open", typeof(double));
    // ... populate DataTable from records ...
    await bulkCopy.WriteToServerAsync(dt);
}

// After:
await using var writer = conn.BeginBinaryImport(
    $"COPY \"{tempTableName}\" (\"Date\", \"Open\", \"High\", \"Low\", \"Close\", \"Volume\") FROM STDIN (FORMAT BINARY)");
foreach (var record in records)
{
    await writer.StartRowAsync(ct);
    await writer.WriteAsync(record.Date.ToDateTime(TimeOnly.MinValue), NpgsqlTypes.NpgsqlDbType.Date, ct);
    await writer.WriteAsync((double)record.Open, NpgsqlTypes.NpgsqlDbType.Double, ct);
    await writer.WriteAsync((double)record.High, NpgsqlTypes.NpgsqlDbType.Double, ct);
    await writer.WriteAsync((double)record.Low, NpgsqlTypes.NpgsqlDbType.Double, ct);
    await writer.WriteAsync((double)record.Close, NpgsqlTypes.NpgsqlDbType.Double, ct);
    await writer.WriteAsync(record.Volume, NpgsqlTypes.NpgsqlDbType.Bigint, ct);
}
await writer.CompleteAsync(ct);
```

The `NpgsqlTypes.NpgsqlDbType` enum is used for per-column type specification in binary COPY. Available from the `Npgsql` package.

### 11.7 `SharedTableDdlTemplate.cs`

```csharp
// Before:
public static string CreateTableIfNotExists(string tableName)
{
    return $@"
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = '{tableName}')
BEGIN
    CREATE TABLE [dbo].[{tableName}] (
        [Date]   DATE      NOT NULL,
        [Open]   FLOAT     NOT NULL,
        [High]   FLOAT     NOT NULL,
        [Low]    FLOAT     NOT NULL,
        [Close]  FLOAT     NOT NULL,
        [Volume] BIGINT    NOT NULL,
        CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED ([Date])
    );
    CREATE INDEX [IX_{tableName}_Date] ON [dbo].[{tableName}] ([Date]);
END";
}

// After:
public static string CreateTableIfNotExists(string tableName)
{
    return $@"
CREATE TABLE IF NOT EXISTS ""public"".""{tableName}"" (
    ""Date""   DATE              NOT NULL,
    ""Open""   DOUBLE PRECISION  NOT NULL,
    ""High""   DOUBLE PRECISION  NOT NULL,
    ""Low""    DOUBLE PRECISION  NOT NULL,
    ""Close""  DOUBLE PRECISION  NOT NULL,
    ""Volume"" BIGINT            NOT NULL,
    PRIMARY KEY (""Date"")
);

CREATE INDEX IF NOT EXISTS ""IX_{tableName}_Date"" ON ""public"".""{tableName}"" (""Date"");";
}
```

Note: The `CreatePerSymbolTablesAsync` method signature changes from `SqlConnection`/`SqlTransaction` to `NpgsqlConnection`/`NpgsqlTransaction` (or the method can be made connection-type-agnostic since it only calls `ExecuteNonQueryAsync` on the command object).

### 11.8 `Program.cs` (SqlMigrations)

```csharp
// Before:
await using (var conn = new SqlConnection(masterConnection))
{
    await conn.OpenAsync();
    var cmd = conn.CreateCommand();
    cmd.CommandText = $@"
        IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = @db)
        BEGIN
            CREATE DATABASE [{databaseName}];
            PRINT 'Created database: {databaseName}';
        END";
    cmd.Parameters.AddWithValue("@db", databaseName);
    await cmd.ExecuteNonQueryAsync();
}

var services = new ServiceCollection()
    .AddFluentMigratorCore()
    .ConfigureRunner(runner => runner
        .AddSqlServer()
        .WithGlobalConnectionString(builder.ConnectionString)
        .ScanIn(typeof(Program).Assembly).For.Migrations())
    // ...

// After:
// (Remove the entire sys.databases CREATE DATABASE block — databases are pre-provisioned)

var services = new ServiceCollection()
    .AddFluentMigratorCore()
    .ConfigureRunner(runner => runner
        .AddPostgres()                          // ← changed
        .WithGlobalConnectionString(connectionString)  // ← use the raw connection string, no InitialCatalog manipulation needed
        .ScanIn(typeof(Program).Assembly).For.Migrations())
    // ...
```

### 11.9 Migration files

**`InitialMarketData_001.cs`:**
```csharp
// Before:
Create.Index("ix_symbol_master_active")
    .OnTable("symbol_master")
    .OnColumn("is_active")
    .Ascending()
    .WithOptions().NonClustered();    // ← remove this

// After:
Create.Index("ix_symbol_master_active")
    .OnTable("symbol_master")
    .OnColumn("is_active")
    .Ascending();
```

Same change in `InitialBacktest_002.cs` for 3 index definitions (lines 47-49, 62-64, 81-83).

`AddBacktestMetrics_003.cs`: No changes needed.

---

## 12. Rollback Plan

If the PostgreSQL migration needs to be rolled back:

1. **Code rollback**: Revert the NuGet package changes, type renames, and SQL dialect changes via `git revert`
2. **Database rollback**: Keep the existing SQL Server instance running during the transition period. The migration runner targets one database at a time — point it back at SQL Server
3. **Data**: If data was migrated (not just schema), the SQL Server data remains intact as long as the SQL Server container/database hasn't been decommissioned

The two-week parallel-run window (tentative) would look like:
- Week 1: Schema migration + data migration to PostgreSQL. Both databases running.
- Week 2: Application pointed at PostgreSQL. SQL Server still accessible for fallback.
- Day 14: Decommission SQL Server after confirming parity.

---

## 13. Summary Statistics

| Metric | Value |
|---|---|
| Files changed | ~14 |
| NuGet packages swapped | `Microsoft.Data.SqlClient` → `Npgsql` (2 `.csproj` files) |
| `MERGE` blocks rewritten | 3 (2 in DataSyncService, 1 in HistoricDataSeedService — the ON CONFLICT DO NOTHING variant is simpler) |
| `SqlBulkCopy` → binary import | 1 rewrite |
| `DATEADD/DATEFROMPARTS` → `date_trunc` | 4 string literals swapped |
| `TOP(N)` → `LIMIT N` | 2 occurrences |
| `WITH OPTIONS.NonClustered()` removed | 6 calls across 2 migration files |
| Type renames (`Sql*` → `Npgsql*`) | ~60-80 individual renames across 6 files |
| `[` bracket → `"` double-quote | ~100+ occurrences across all files |
| `dbo` → `public` | ~6 occurrences |
| Infrastructure files changed | 3 (docker-compose, .env.example, up.ps1) |
| Estimated implementation time | 2-3 focused sessions |
