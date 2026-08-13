# Void Capital

A stock market simulator for NSE (India) equities. No real money, no real
trading. It is a test lab for whether computer guesses about stock prices
beat a coin flip.

## Project Pitch

Void Capital is a full-stack NSE stock market portfolio simulator that
combines C#/.NET backend engineering with Python ML infrastructure. It
demonstrates:

- **Full-stack engineering:** ASP.NET Core 10 Web API (Repository pattern,
  FluentMigrator, Serilog, BackgroundService, Redis cache-aside) + React/TS
  dashboard (Recharts, Vitest, code-split routes)
- **Quant finance:** Walk-forward backtesting, alpha factor library (20+
  features), event-driven simulation with slippage/commission models,
  VaR/Kelly risk management
- **ML infrastructure:** Strategy pattern for trading algorithms, ensemble
  signal voting, concept drift monitoring, batch inference pipeline
- **Engineering rigor:** TDD (red-green-refactor), structured logging,
  latency measurement at every pipeline stage, edge case test suite

All running locally with zero external dependencies beyond PostgreSQL + Redis
(infra-only Docker; free yfinance data API). See the comparative evaluation
in `reports/`.

## What it does

Every evening after the Indian market closes:

1. Fetches the day's real NSE price data for a watchlist of stocks
2. Computer models look at past price history and guess which stocks might go
   up or down in the coming days
3. The models produce suggestions: BUY / SELL / HOLD, each with a confidence
   score ("70% sure" vs "30% sure")
4. The app double-checks each suggestion. Does it have enough pretend money?
   Is the stock on the allowed list? Then it either executes the suggestion
   automatically or queues it for manual approval
5. A web dashboard shows everything: pretend money, owned stocks, wins and
   losses, and the full history of every pretend trade

## Three ways to play

Three portfolios run in parallel, all starting with the same Rs 1,00,000 and
watching the same suggestions:

| Portfolio | Behavior |
|-----------|----------|
| **Your Portfolio** | You approve or reject every suggestion yourself |
| **System** | A robot that blindly follows every suggestion, playing safe. It never spends money it does not have |
| **System-Reckless** | A robot that can spend money it does not have (simulated credit), pays daily interest on what it borrows, and is force-closed if it over-borrows |

Compare all three side by side to see whether caution or recklessness wins.

## Architecture

```
+-------------------+       +----------------------+       +-----------------+
|   React (TS)      | <-->  |  C# / ASP.NET Core   | <-->  |  Python scripts |
|   Dashboard       |       |  Void Capital API    |       |  Data pipeline  |
+-------------------+       +----------------------+       +-----------------+
                                    |                                 |
                                    v                                 v
                            +------------------+              +------------------+
                            |  PostgreSQL 16   | <------------|  PostgreSQL 16   |
                            |  (Docker)        |   writes to  |  (Docker)        |
                            +------------------+              +------------------+
```

Three-tier hybrid: React SPA -> .NET API -> PostgreSQL + Python data
pipeline. Docker runs only PostgreSQL + Redis; the API, frontend, and Python
run on the host.

Data flow:

1. **Python** fetches EOD data from free APIs (yfinance) and writes to the
   shared PostgreSQL database
2. **Python** runs strategies through a walk-forward gate and writes signals
   (BUY/SELL with confidence, entry/target/stop)
3. **C# backend** reads the signals, validates each trade through the
   portfolio rule engine, executes or queues for approval
4. **React frontend** displays portfolio state, signals, trade history, and
   performance metrics via the C# API

## Components

| Component | Stack | Responsibility |
|-----------|-------|----------------|
| API | C# ASP.NET Core 10 | Portfolio engine, signal ingestion, daily cycle |
| Frontend | React + TypeScript + Vite | Dashboard, holdings, signals, admin |
| Python | pandas, numpy, psycopg2 | Data pipeline, strategies, backtesting |
| Database | PostgreSQL 16 | 5 schemas, 8 tables |
| Cache | Redis 7 | Market data cache, pub/sub events |

## Quick Start

```powershell
docker compose up -d            # start PostgreSQL + Redis
start-api.bat                   # start the .NET API on port 5189
cd src/frontend && npm run dev  # start the React app
```

Then open http://localhost:5173.

First-time data setup (Python pipeline):

```powershell
cd python
.venv\Scripts\python backfill.py          # 5 years of EOD history for the watchlist
.venv\Scripts\python daily_update.py      # incremental daily sync
.venv\Scripts\python generate_signals.py  # run strategies, write signals
```

## Daily Operation

1. Data pipeline runs automatically at market close (daily cycle service)
2. Strategies generate signals (Sharpe > 0.5 walk-forward threshold)
3. Signals appear in UI for approval (or auto-execute)
4. System portfolio (user 2) blindly follows every signal
5. Reckless portfolio (user 3) trades with leverage
6. Check dashboards to compare performance

## Project Structure

```
void_capital/
  docker-compose.yml               -- PostgreSQL, Redis
  src/
    VoidCapital.Api/               -- ASP.NET Core Web API
      Migrations/                  -- FluentMigrator classes
      Controllers/                 -- Portfolio, Holdings, Signals, Admin
      Services/                    -- DailyCycleService, signal integration
      Shared/Repositories/         -- BaseRepository, CRUD per entity
    VoidCapital.Api.Tests/         -- xUnit + Moq tests
    frontend/                      -- React + TypeScript + Vite
      src/pages/                   -- Dashboard, Holdings, Trades, Signals
      src/components/              -- Layout, StatCard, Modal
      src/services/                -- api.ts (axios)
  python/                          -- Data pipeline + ML
    db.py                          -- Shared DB engine + retry/upsert helpers
    backfill.py                    -- Initial historical fetch
    daily_update.py                -- Daily EOD pull
    generate_signals.py            -- Run strategies, write signals
    strategies/                    -- Strategy ABC + implementations
    backtest.py                    -- Walk-forward validation
    backtester/                    -- Event-driven backtest engine
    factors/                       -- Alpha factor library
    risk/                          -- VaR, Kelly, correlation
    tests/                         -- pytest suite (incl. tests/hardening/)
  reports/                         -- Comparative evaluation output
  .gitignore
  .env.example
```

## Configuration

See `.env.example` and `appsettings.json`. Key settings:

- `min_confidence`: minimum signal confidence (default 0.50)
- `negative_limit`: System-Reckless credit line (default Rs 100,000)
- `interest_rate`: daily margin interest (default 0.05%)
- `VC_DB_*`: PostgreSQL connection (host, port, database, user, password)

## Key Decisions

- Modular monolith first, microservices later (Phase 4+)
- PostgreSQL schemas for module isolation
- No auth until public deployment
- Walk-forward validation required before live signals
- Python as a subprocess, not a server: .NET calls Python via
  `Process.Start()` when data needs fetching or signals need generating
- JSON signal file as IPC: Python writes signals to the DB, .NET reads them
- Models suggest, the portfolio engine decides: models cannot override
  position sizing, cash limits, or position existence checks

## Why it exists

Two reasons:

1. **Interview portfolio piece**: a full-stack system exercising C# / .NET,
   React, Python data engineering, ML, SQL, and Docker end to end
2. **Personal trading lab**: rigorously test whether predictive models add
   value over simple benchmarks before risking any real money

## Status

Under active development.