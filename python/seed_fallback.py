"""D2: Synthetic seed data fallback.

Used when the NSE/OpenChart endpoint is unavailable, rate-limited, or
otherwise broken. Generates plausible OHLCV rows via a geometric random walk
from configurable parameters (start price, drift, volatility), then upserts
them the same way as real data so the rest of the stack cannot tell the
difference. Idempotent via the (symbol, date) primary key.
"""

from __future__ import annotations

import argparse
import logging
import random
from datetime import date, datetime, timedelta

import pandas as pd

from backfill import SYMBOLS
from db import get_engine, timed, upsert_stocks

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger("void-capital.seed-fallback")

# Baseline starting prices for the watchlist (approximate NSE levels).
DEFAULT_START_PRICES = {
    "RELIANCE": 2800.0, "TCS": 3800.0, "HDFCBANK": 1650.0, "INFY": 1500.0,
    "ICICIBANK": 1180.0, "HINDUNILVR": 2400.0, "SBIN": 800.0,
    "BHARTIARTL": 1500.0, "ITC": 440.0, "WIPRO": 520.0,
}

ANNUAL_DRIFT = 0.12   # ~12% annual drift, typical equity expectation
ANNUAL_VOL = 0.28     # ~28% annualized volatility


def business_days(start: date, end: date) -> list[date]:
    """All Mon-Fri dates in [start, end] (NSE does not trade weekends)."""
    days: list[date] = []
    day = start
    while day <= end:
        if day.weekday() < 5:  # 0=Mon .. 4=Fri
            days.append(day)
        day += timedelta(days=1)
    return days


def generate_rows(symbol: str, days: int, rng: random.Random) -> list[dict]:
    """Generate one OHLCV row per business day using a geometric random walk."""
    start_price = DEFAULT_START_PRICES.get(symbol, 1000.0)
    daily_drift = ANNUAL_DRIFT / 252.0
    daily_vol = ANNUAL_VOL / (252.0 ** 0.5)

    today = date.today()
    first = today - timedelta(days=days)
    rows: list[dict] = []

    price = start_price
    for day in business_days(first, today):
        shock = rng.gauss(0, daily_vol)
        open_price = price
        close_price = max(1.0, price * (1 + daily_drift + shock))
        intraday = abs(rng.gauss(0, daily_vol)) * 0.5
        high = max(open_price, close_price) * (1 + intraday)
        low = min(open_price, close_price) * (1 - intraday)
        volume = int(rng.randint(500_000, 5_000_000))

        rows.append({
            "symbol": symbol,
            "date": day,
            "open": round(open_price, 2),
            "high": round(high, 2),
            "low": round(low, 2),
            "close": round(close_price, 2),
            "volume": volume,
        })
        price = close_price

    return rows


@timed
def seed_symbol(symbol: str, days: int, rng: random.Random) -> int:
    """Generate + upsert synthetic rows for one symbol."""
    rows = generate_rows(symbol, days, rng)
    written = upsert_stocks(get_engine(), rows)
    logger.info("%s: %d synthetic rows upserted", symbol, written)
    return written


def main() -> None:
    parser = argparse.ArgumentParser(description="Synthetic NSE seed data (fallback)")
    parser.add_argument("--days", type=int, default=365 * 5,
                        help="days of history to generate (default: 5 years)")
    parser.add_argument("--seed", type=int, default=42,
                        help="RNG seed for reproducibility")
    parser.add_argument("--symbols", nargs="*", default=SYMBOLS,
                        help="symbols to generate (default: watchlist)")
    args = parser.parse_args()

    rng = random.Random(args.seed)
    total = 0
    for symbol in args.symbols:
        total += seed_symbol(symbol, args.days, rng)

    logger.info("Seed fallback complete: %d total rows written", total)


if __name__ == "__main__":
    main()
