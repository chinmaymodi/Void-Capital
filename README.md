# Void Capital

A stock market simulator for NSE (India) equities. No real money, no real
trading. It is a test lab for whether computer guesses about stock prices
beat a coin flip.

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

## Why it exists

Two reasons:

1. **Interview portfolio piece**: a full-stack system exercising C# / .NET,
   React, Python data engineering, ML, SQL, and Docker end to end
2. **Personal trading lab**: rigorously test whether predictive models add
   value over simple benchmarks before risking any real money

## Tech stack

| Layer | Technology |
|-------|-----------|
| Backend | C# / ASP.NET Core 10 Web API |
| Frontend | React + TypeScript + Vite |
| Data/ML | Python (pandas, scikit-learn, Prophet) |
| Database | PostgreSQL 16 (Docker) |
| Cache | Redis 7 (Docker) |
| Infra | Docker Compose |

## Status

Under active development.
