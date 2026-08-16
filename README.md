# Void Capital

A full-stack NSE (India) equity market simulator: a paper-trading platform
where algorithmic strategies generate BUY / SELL / HOLD signals and a
portfolio engine executes them against simulated money. No real trading, no
real money - a rigorous test lab for whether computer predictions about stock
prices beat a coin flip.

Built with ASP.NET Core 10, React 19 + TypeScript, PostgreSQL 16, and Redis 7.
The ML/data pipeline (Python) is a private component; this repository contains
the C# API, the React dashboard, and the infrastructure.

---

## What it does

Every evening after the Indian market closes, the system runs an automated
daily cycle:

1. Refreshes market data for a watchlist of NSE stocks
2. Runs trading strategies through a walk-forward validation gate
3. Writes signals (BUY / SELL / HOLD with confidence scores) to the database
4. The portfolio engine validates each signal against cash, holdings, and
   position rules, then executes it or queues it for manual approval
5. A React dashboard shows portfolio state, holdings, trade history, signal
   performance, and side-by-side agent comparison

Three portfolios run in parallel, all starting with the same Rs 1,00,000 and
watching the same signals:

| Portfolio | Behavior |
|-----------|----------|
| **Your Portfolio** | You approve or reject every suggestion yourself |
| **System** | A robot that blindly follows every signal, playing safe. It never spends money it does not have |
| **System-Reckless** | A robot that can spend money it does not have (simulated credit), pays daily interest on what it borrows, and is force-closed if it over-borrows |

The comparison view answers the core question: does caution or recklessness
win?

---

## Architecture

```
+-------------------+       +------------------------+       +------------------+
|   React 19 (TS)   | <-->  |  ASP.NET Core 10 API   | <-->  |  Python pipeline |
|   Dashboard       |  HTTP |  VoidCapital.Api       |  proc |  (private repo)  |
+-------------------+       +------------------------+       +------------------+
                                    |        |
                                    v        v
                            +----------------+  +----------------+
                            | PostgreSQL 16  |  | Redis 7        |
                            | (Docker)       |  | (cache-aside)  |
                            +----------------+  +----------------+
```

- **Docker runs only PostgreSQL + Redis.** The API, frontend, and Python
  pipeline run on the host.
- **Python is invoked as a subprocess** (`Process.Start`), not a server. It
  fetches EOD data, runs strategies, and writes signals to the shared
  PostgreSQL database. The C# API reads those signals and owns all execution
  decisions.
- **The API is deployable as a Windows service** (`UseWindowsService`) with a
  scheduled daily cycle and an intraday data-collection fallback.

### Data flow

1. Python fetches EOD price data and writes it to PostgreSQL
2. Python runs strategies through a walk-forward gate (out-of-sample
   Sharpe >= 0.5) and writes signals
3. C# reads signals, validates each trade through the portfolio rule engine
   (cash, holdings, position existence, min-confidence gate), and executes or
   queues for approval
4. React renders portfolio state, signals, trade history, and performance via
   the C# API

---

## Tech stack

| Layer | Technology | Notes |
|-------|-----------|-------|
| API | ASP.NET Core 10 (.NET 10) | Controllers, repository pattern, DI, background services |
| Data access | EF Core 10 + Npgsql | `IDbContextFactory` with short-lived contexts |
| Migrations | FluentMigrator 8 | 7 migrations, schema-per-module |
| Logging | Serilog | Console + rolling daily file (14 days retained) |
| Cache | StackExchange.Redis | Cache-aside for market data |
| Health | AspNetCore.HealthChecks | Npgsql + Redis probes at `/api/health` |
| Frontend | React 19 + TypeScript + Vite 8 | Recharts, axios, code-split lazy routes |
| Frontend tests | Vitest 4 | 13 test files, network-free |
| Backend tests | xUnit + Moq + FluentAssertions | 180 tests |
| Integration tests | Testcontainers | Real PostgreSQL 16 + Redis 7 containers |
| Database | PostgreSQL 16 | 6 schemas: `identity`, `portfolio`, `market_data`, `signals`, `ops`, `ml` |
| Cache | Redis 7 | Docker |
| Deployment | Windows Service | `VoidCapitalDailyCycle` + scheduled collector task |

---

## Engineering highlights

### Backend (C# / .NET)

- **Repository pattern with dependency inversion**: services depend on
  repository interfaces, never on EF/Npgsql directly. All repositories are
  scoped and registered in `Program.cs`.
- **Background services**: `DailyCycleService` runs the EOD cycle on a
  schedule with missed-slot catch-up on restart; `IntradayCycleService` is a
  market-hours fallback that checks data freshness and relaunches the
  collector only when stale. Both are unit-tested for scheduling math and
  edge cases (stale RUNNING runs, weekend suppression, catch-up suppression).
- **Global exception middleware**: `ExceptionMiddleware` maps domain
  exceptions to consistent `ApiResponse<T>` envelopes (400/404/500) with a
  trace id.
- **Async signal jobs**: `SignalJobService` runs the Python pipeline as a
  background job with polling status, because generation exceeds the
  frontend's HTTP timeout.
- **Windows service hardening**: content root pinned to the assembly
  directory (services run with CWD = System32), Serilog file sink for
  headless operation, SCM registration via `UseWindowsService`.
- **FluentMigrator at boot**: migrations run on startup with graceful
  failure so `/api/health` can still report the problem.

### Frontend (React / TypeScript)

- **Context-based user switching**: `UserProvider` + `useUser` drives every
  page; switching agents refetches all data.
- **Consistent data-fetch pattern**: loading -> error -> empty -> content on
  every page, with a shared toast system.
- **Code-splitting**: lazy routes with Suspense.
- **Meaningful tests**: 13 Vitest suites covering user-picker refetch, sort
  toggling, sell-over-holding rejection, batch-confirmation gating, and
  square-off result retention - all network-free with mocked services.

### Testing strategy

- **180 backend tests**: controller suites, service suites (portfolio,
  signals, daily cycle, intraday scheduler, signal jobs), repository tests,
  middleware tests, and a build-time guard (`NoRealBrokerIntegrationTests`)
  that scans the source tree to guarantee no live-broker order-placement code
  ever enters the repo.
- **Integration tests with Testcontainers**: real PostgreSQL 16 + Redis 7
  containers, full-stack assertions (HTTP -> real DB state, not
  status-code-only), per-test user isolation with parameterized cleanup.
- **Frontend**: 13 Vitest suites, network-free.

---

## API reference

All endpoints return a consistent `ApiResponse<T>` envelope
(`{ success, data, error, traceId }`). Errors are mapped by
`ExceptionMiddleware`: validation -> 400, not-found -> 404, everything else ->
500, each with a trace id for log correlation.

| Method | Route | Purpose |
|--------|-------|---------|
| GET | `/api/health` | Liveness + Npgsql/Redis probes |
| GET | `/api/v1/system/info` | Version info |
| GET | `/api/v1/users` | Agent roster (id + name) for the UI picker |
| GET | `/api/v1/portfolio/{userId}` | Cash, holdings value, total equity |
| GET | `/api/v1/portfolio/{userId}/history` | Daily PnL snapshot series |
| GET | `/api/v1/holdings/{userId}` | Holdings with live prices, unrealized PnL, position weight |
| POST | `/api/v1/holdings/{userId}/buy` | Manual buy (symbol, shares) |
| POST | `/api/v1/holdings/{userId}/sell` | Manual sell (symbol, shares) |
| GET | `/api/v1/market/{symbol}/price` | Latest price (cache-aside) |
| GET | `/api/v1/market/{symbol}/history` | Price history (cache-aside) |
| GET | `/api/v1/settings/{userId}` | Per-user settings |
| PUT | `/api/v1/settings/{userId}` | Update settings (watchlist syncs to DB) |
| GET | `/api/v1/signals/today/{userId}` | Today's PENDING signals |
| POST | `/api/v1/signals/{signalId}/approve` | Approve (executes if auto-execute on) |
| POST | `/api/v1/signals/{signalId}/reject` | Reject |
| POST | `/api/v1/signals/batch-approve` | Batch approve (per-signal result) |
| POST | `/api/v1/signals/batch-reject` | Batch reject (per-signal result) |
| GET | `/api/v1/trades/{userId}` | Paginated trade log |
| GET | `/api/v1/trades/{userId}/export` | CSV export |
| GET | `/api/v1/performance/models` | Per-model performance metrics |
| GET | `/api/v1/performance/signals` | Resolved signals with outcomes |
| GET | `/api/v1/performance/compare` | Side-by-side agent comparison |
| POST | `/api/v1/admin/ingest-signals` | Ingest signals from the Python pipeline |
| GET/PUT | `/api/v1/admin/settings/{userId}` | Admin read/update of any user's settings |
| PUT | `/api/v1/admin/settings/global` | Global settings defaults |
| POST | `/api/v1/admin/square-off/{userId}` | Force-close all holdings |
| GET | `/api/v1/admin/status` | Cycle status (last run, next slot) |
| POST | `/api/v1/admin/run-signals` | Start async signal-generation job |
| GET | `/api/v1/admin/run-signals/{jobId}` | Poll job status |
| POST | `/api/v1/admin/run-daily-cycle` | Trigger the daily cycle manually |

---

## Signal lifecycle

Signals live in `signals.model_predictions` with a string `status` column.
`PENDING` is the only mutable state; the rest are terminal.

```
                    approve (auto-execute OFF)
PENDING  -------------------------------------->  APPROVED
    |
    | approve (auto-execute ON)
    |   -> portfolio engine executes the trade
    |   -> success: EXECUTED
    |   -> failure: FAILED (reason recorded)
    |
    | reject  ->  REJECTED
```

- **Approve with auto-execute off**: marks the signal APPROVED (a recorded
  decision; nothing executes).
- **Approve with auto-execute on**: runs the trade immediately through the
  portfolio engine. EQ fills at the live quote; options fill at the
  pipeline-reconstructed premium (BUY) or current settle (SELL). Success
  marks EXECUTED, any engine rejection marks FAILED with the reason.
- **Reject**: marks REJECTED.
- **Batch operations** return one result per signal so a single bad signal
  never fails the whole batch.

---

## Portfolio engine

`PortfolioService` is the single execution gate. Every trade - manual or
signal-driven - passes through the same rules:

**Validation (`CanBuy` / `CanSell`)**
- `CanBuy`: shares and price must be positive; the trade must not exceed
  available cash (hard limit), or may dip into a credit line down to
  `-negativeLimit` (soft limit, used by the reckless agent).
- `CanSell`: the holding must exist and hold at least the requested quantity.

**Execution flow (EQ)**
1. Validate quantity, normalize symbol, load user
2. Price from market data (live quote)
3. `CanBuy` / `CanSell` gate
4. Cash update (debit/credit)
5. Holding upsert (adds to existing position, weighted-average cost) or
   delete when fully sold
6. Trade log insert (BUY/SELL, price, total value, reason)

**Options path**: cash instruments - the cost basis is the premium paid.
Holdings are contract-keyed (symbol + instrument type + expiry + strike), so
a CE and a PE on the same underlying are distinct positions. Sizing is capped
at 10% of cash per idea in whole lots by the pipeline.

**Known limitation (documented in code)**: cash update, holding update, and
trade log insert are separate writes without a wrapping DB transaction. A
crash mid-trade can leave partial state. Acceptable for the research scope;
a transaction-aware path is on the roadmap.

---

## Daily cycle pipeline

`DailyCycleRunner` executes one full EOD cycle (scheduled at 18:30 IST, or
triggered manually via the admin endpoint):

| Step | Work |
|------|------|
| 0 | Refresh daily features (Python subprocess; failure logs and continues on yesterday's features) |
| 1 | Signal generation for every user (Python subprocess per user) |
| 2 | Auto-execute signals for auto-execute users, min-confidence gated |
| 3 | Resolve pending signal performance (target / stop / expiry) |
| 4 | Charge daily interest on negative cash (rate / 365) |
| 5 | Margin call: square off holdings when cash breaches `-negativeLimit` (post-interest cash) |
| 6 | Record daily PnL snapshots for all users |
| 7 | Record the run in `ops.cycle_runs` (RUNNING -> SUCCEEDED / FAILED) |

Resilience rules: a per-user failure never aborts the remaining users; a
failed feature refresh never kills the cycle; the run is marked FAILED
honestly when the Python pipeline reports errors.

---

## AI & ML pipeline

The decision-making brain is a companion Python research pipeline (kept in a
separate private repository) that produces every signal the API executes. It
is deliberately classical ML + quantitative finance rather than LLM-based:
the domain rewards statistical rigor and reproducibility over language
understanding, and every step is auditable.

- **Feature engineering**: 27+ cross-sectional features across momentum,
  volatility, volume, and correlation families; intraday session-aware
  factors that reset at the 09:15 IST open and never leak across the
  overnight gap
- **Walk-forward validation**: every strategy must clear an out-of-sample
  Sharpe gate (>= 0.5) before its signals become eligible for execution.
  The gate is enforced in code, so only validated strategies ever reach the
  execution path
- **Ensemble voting**: multiple independent strategies - a mix of momentum
  and mean-reversion (SMA crossover, RSI oscillator, basis, put-call ratio,
  IV-RV) - vote per symbol; the production feature is a 3-factor average
  (basis + PCR + IV-RV) that survived walk-forward
- **Overfitting controls**: deflated Sharpe analysis, hysteresis bands,
  full-sample lead-lag checks, and a strict no-forward-fill data policy
- **Data engineering**: 5.4M+ options rows backfilled across 1,234 trading
  days x 11 symbols, corporate-action re-basing, and NSE bhavcopy format
  migration handling
- **Backtesting**: event-driven backtester with fill models (mid / spread /
  IV), lot-size sweeps, and explicit cost modeling

---

## Database schema

Six schemas, one per module boundary (FluentMigrator owns the schema;
EF Core maps the read/write models):

| Schema | Tables | Owned by |
|--------|--------|----------|
| `identity` | `users`, `settings` | C# |
| `portfolio` | `holdings`, `trade_log`, `pnl_snapshots`, `watchlist` | C# |
| `market_data` | `stocks` | C# (reads), Python (writes) |
| `signals` | `model_predictions`, `signal_performance` | C# (reads), Python (writes) |
| `ops` | `cycle_runs` | C# |
| `ml` | (reserved for ML artifacts) | Python |

Key relationships: `holdings` and `trade_log` reference `identity.users`;
`signal_performance` is 1:1 with `model_predictions`; `watchlist` is synced
from `identity.settings.watchlist` on every settings update.

---

## Design decisions

- **Repository pattern + DIP**: services depend on repository interfaces,
  never on EF/Npgsql. Repositories create short-lived `AppDbContext`
  instances via `IDbContextFactory`, avoiding the classic long-lived-context
  pitfalls in background services.
- **FluentMigrator for schema, EF Core for queries**: migrations are explicit,
  versioned SQL (001-008) run at boot; EF Core is used purely as a query
  mapper. This keeps schema evolution auditable and the query layer thin.
- **Python as a subprocess, not a service**: the ML pipeline is invoked via
  `Process.Start` with timeouts, never as a long-running server. The C# API
  owns all execution decisions; Python only produces data and signals.
- **Cache-aside with Redis**: market-data reads go through Redis with a short
  TTL; fresh-price paths bypass the cache. Health checks probe both stores.
- **Schema-per-module**: PostgreSQL schemas enforce module boundaries at the
  database level, mirroring the C# module folders.
- **Windows service deployment**: the API runs headless as a Windows service
  with Serilog file sinks, content root pinned to the assembly directory, and
  SCM registration - the details that make `sc start` actually work.

---

## Testing deep dive

**180 backend tests** across four layers:

| Layer | What is covered |
|-------|-----------------|
| Controller suites | Route wiring, validation, response envelopes, auth-adjacent behavior |
| Service suites | Portfolio rules, signal approval/execution, daily cycle steps, intraday scheduler math, signal-job concurrency |
| Repository tests | Settings round-trip, cycle-run timestamptz handling |
| Middleware | Exception -> status-code mapping |

**Integration tests (Testcontainers)**: `IntegrationFactory` boots a real
`WebApplicationFactory` against fresh disposable PostgreSQL 16 + Redis 7
containers (schema created by FluentMigrator on app boot). Tests assert
HTTP -> real DB state, not status codes alone. The Python bridge is stubbed
so integration tests stay fast and environment-independent; real Python
execution is covered separately with a mocked process runner. Tests isolate
themselves with unique users/symbols rather than truncating shared tables.

**Safety guard**: `NoRealBrokerIntegrationTests` scans the entire source tree
at build time for broker order-placement API markers. Any code that could
place a real order fails the build - a permanent guarantee that this stays a
paper-trading system.

**Frontend**: 13 Vitest suites, all network-free with mocked services,
covering user-switch refetch, sort toggling, sell-over-holding rejection,
batch-confirmation gating, and square-off result retention.

---

## Built with agentic AI workflows

This system was developed end-to-end using an agentic coding workflow - a
team of specialized AI coding agents working under human direction, with
explicit review gates at every stage. The workflow itself was a first-class
part of the engineering process, not an afterthought.

**How the workflow was structured:**

- **Specialized agents, not one general assistant**: the build used a
  rotating cast of purpose-built agents, each with a narrow role - planning
  agents (architecture and sequencing before any code), review agents
  (auditing every logical feature and ~500-line milestone against a
  multi-section quality checklist), debugging agents (root-cause analysis
  before fixes), and domain research agents (finance, data-source, and
  framework research). Narrow roles meant each agent stayed in its lane and
  its output was predictable.
- **Lazy-loaded skills**: domain knowledge (code-quality standards, testing
  conventions, deployment checklists) was packaged as skills that load only
  when relevant, keeping agent context small and focused instead of dumping
  everything into every session.
- **Parallel development streams**: the public API repo, the private ML
  pipeline, and research documentation ran as separate workstreams managed
  concurrently - multiple agents and multiple streams in flight at once,
  coordinated through a shared ticket system.
- **Human-in-the-loop control points**: every architectural decision, every
  trade-off call, and every merge stayed with a human. Agents produced
  drafts, implementations, and reviews; the human owned correctness, scope,
  and direction.
- **Verification as the safety net**: 180 backend tests, 13 frontend suites,
  and a build-time guard against real broker order placement made
  agent-driven iteration safe - agents could move fast because the test
  suite caught regressions before they landed.
- **Judgment about when NOT to use AI**: the ML pipeline itself is
  deterministic Python with explicit statistical validation - no LLM in the
  loop, because the domain needs reproducible, auditable math. Choosing
  where automation helps and where it hurts was a deliberate part of the
  design.

**What this demonstrates**: the ability to direct AI coding agents on
multi-stream projects, review and correct their output, enforce quality
gates, and keep a human accountable for the result - the same skills an
agentic development team needs.

---

## From ambiguous problem to scalable solution

The project started as an open-ended question: "can I test whether trading
strategies actually work, without risking real money?" No requirements, no
spec, no existing system. The path from that question to this codebase is a
deliberate exercise in problem decomposition:

1. **Separate the three concerns**: research (does the edge exist?), data
   (where does it come from, how is it kept clean?), and execution (how does
   a validated idea become a trade?). Each became a distinct subsystem with
   its own ownership boundary - the ML pipeline, the data layer, and the
   API/portfolio engine.
2. **Make validation a first-class gate**: instead of trusting any strategy,
   the system requires walk-forward out-of-sample evidence before a signal
   is even eligible. The gate is enforced in code, not by convention.
3. **Design for safety by construction**: paper-trading only, a build-time
   guard that fails compilation if real broker order-placement code ever
   appears, and per-user risk limits (credit lines, interest, margin calls)
   enforced by the portfolio engine.
4. **Ship incrementally**: the daily cycle first, then intraday collection,
   then options execution - each slice independently testable, each with its
   own test suite, so the system grew without a big-bang rewrite.

The result is a system where an ambiguous question became a set of
well-bounded, independently testable subsystems - each with a clear owner,
a clear contract, and a clear reason to exist.

---

## Project structure

```
void-capital/
  docker-compose.yml               -- PostgreSQL 16 + Redis 7 (infra only)
  start-api.bat                    -- Launch the API locally (port 5189)
  stop-all.bat
  global.json                      -- Pinned .NET SDK 10.0.204
  src/
    VoidCapital.Api/               -- ASP.NET Core Web API (all production code)
      Controllers/                 -- 11 controllers (Portfolio, Holdings, Signals,
      |                                Trades, Admin, Settings, Performance, ...)
      Modules/                     -- Feature modules: Portfolio, Signals, MarketData
      Services/                    -- DailyCycleService, IntradayCycleService,
      |                                SignalJobService, DailyCycleRunner
      Shared/Repositories/         -- Repository interfaces + implementations
      Data/                        -- AppDbContext (EF Core)
      Migrations/                  -- FluentMigrator migrations (001-008)
      Middleware/                  -- ExceptionMiddleware
    VoidCapital.Api.Tests/         -- xUnit: 180 unit + integration tests
    VoidCapital.Core/              -- (placeholder for future domain layer)
    VoidCapital.Infrastructure/    -- (placeholder for future infra layer)
  frontend/                        -- React 19 + TypeScript + Vite
    src/pages/                     -- Dashboard, Holdings, Trades, Signals,
    |                                  SignalPerformance, Compare, SystemPortfolio,
    |                                  Settings, Admin
    src/context/                   -- UserProvider, ToastProvider
    src/services/                  -- api.ts (axios client)
  db/seeds/                        -- Demo seed data (SQL)
  Reports/                         -- Research notes
  scripts/                         -- Environment verification
  .env.example
```

---

## Quick start

Prerequisites: .NET SDK 10, Node.js, Docker Desktop.

```powershell
# 1. Start infrastructure (PostgreSQL + Redis)
docker compose up -d

# 2. Start the API (port 5189)
start-api.bat

# 3. Start the frontend
cd frontend
npm install
npm run dev
```

Open http://localhost:5173.

Seed demo data (optional, after migrations have applied):

```powershell
Get-Content db\seeds\seed_demo_data.sql | docker compose exec -T postgres psql -U vc_user -d void_capital
```

> The Python data pipeline (EOD fetch, strategy execution, signal generation)
> lives in a private repository. The seed data populates price history,
> holdings, and trade history so the dashboard is fully functional; signal
> generation requires the private pipeline (or manual signal ingestion via
> the API).

---

## Configuration

Environment variables (see `.env.example`) and `appsettings.json`:

| Setting | Purpose |
|---------|---------|
| `ConnectionStrings:Postgres` | PostgreSQL connection string |
| `ConnectionStrings:Redis` | Redis connection string |
| `PythonSettings:ScriptPath` | Path to the signal-generation script |
| `PythonSettings:CollectLiveScriptPath` | Path to the live intraday collector |
| `PythonSettings:NotificationScriptPath` | Optional desktop-notification script |

Per-user settings (editable in the UI): `AutoExecute`, `MinConfidence`,
`NegativeLimit` (credit line), `InterestRate` (daily margin interest),
`Watchlist`.

---

## Deployment

The API runs as a Windows service (`VoidCapitalDailyCycle`):

- **Daily cycle**: runs once per day at 18:30 IST (12:30 UTC) after market
  close, with missed-slot catch-up on restart
- **Intraday collection**: a scheduled task (`VoidCapitalLiveCollector`) runs
  the live collector every minute during market hours (Mon-Fri, 09:15-15:15
  IST); the API's `IntradayCycleService` is an in-process fallback that
  relaunches the collector only when data is stale
- **Weekend handling**: collection is suppressed on Saturday and Sunday (IST)
  at every layer - the scheduled task, the collector script, and the
  in-process fallback

---

## Operations

**Logs**: Serilog writes to `logs/voidcapital-YYYYMMDD.log` (rolling, 14-day
retention) next to the service binary, plus the console. Every request
carries a `traceId` that is echoed in the response envelope and the log line,
so a failed API call can be traced end-to-end.

**Health**: `GET /api/health` probes PostgreSQL and Redis connectivity and
returns per-store status. The service is registered with SCM failure
restart (5s / 10s / 30s backoff).

**Useful commands**:

```powershell
# Service state
sc query VoidCapitalDailyCycle
sc start VoidCapitalDailyCycle
sc stop VoidCapitalDailyCycle

# Scheduled task state
schtasks /query /tn VoidCapitalLiveCollector /v /fo LIST

# Live logs
Get-Content "C:\Program Files\VoidCapital\logs\voidcapital-$(Get-Date -Format yyyyMMdd).log" -Tail 50 -Wait
```

**Troubleshooting**:
- Service fails to start (error 1053): confirm the content root is pinned to
  the assembly directory and `Urls` is present in `appsettings.json` - both
  are required because services run with CWD = System32 and
  `launchSettings.json` is not read.
- Stale intraday data: check the scheduled task ran during market hours and
  that `IntradayCycleService` did not need to relaunch the collector (it only
  acts when data is older than 5 minutes).
- Migrations not applied: the service applies FluentMigrator at boot; check
  the log for the migration summary line.

---

## Scope notes (honest)

- **No authentication**: the API is currently unauthenticated with permissive
  CORS. This is a deliberate scope decision for a local research tool; auth
  (JWT + role-based access) is the next planned addition before any public
  deployment.
- **Python pipeline is private**: the ML/data code is not in this repository.
  The C# API is fully functional against pre-existing signal data.
- **Research-first**: all strategies must clear a walk-forward validation
  gate before their signals are eligible for execution. The system is a test
  lab, not a trading system.

---

## Status

Under active development. See `Reports/` for research notes.

---

## Roadmap

- **Authentication**: JWT + role-based access (admin vs. read-only) - the
  first item before any public deployment
- **Transactional trade execution**: wrap cash update, holding update, and
  trade log insert in a single DB transaction
- **Options execution**: wire the completed options execution path into the
  daily cycle (currently code-complete, deployment pending)
- **Live broker integration**: paper-trading fills against real market data
  with the existing build-time guard against real order placement
- **ML artifacts**: populate the reserved `ml` schema with model metadata and
  feature snapshots