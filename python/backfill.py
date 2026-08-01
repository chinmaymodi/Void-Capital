"""D2: Backfill 5 years of daily EOD data for the watchlist into PostgreSQL.

Pipeline stages (SWE: pipeline pattern, each testable in isolation):
  fetch_eod(symbol)      -> raw yfinance DataFrame
  parse_response(df)     -> cleaned DataFrame (OHLCV, no weekends)
  enrich_symbol(df, sym) -> adds the DB symbol (no .NS suffix)
  write_to_db(rows)      -> bulk upsert via db.upsert_stocks

Source note: the D2 ticket specified the OpenChart library, but NSE migrated
its charting site to TradingView in Jan 2026 and OpenChart's endpoints broke
(marketcalls/openchart issue #7, still unfixed). yfinance (Yahoo Finance's
.NS symbols) is the working replacement: same OHLCV shape, no auth, and
returns real NSE data. seed_fallback.py covers total API unavailability.

Idempotent: market_data.stocks has PRIMARY KEY (symbol, date), so re-running
this script never duplicates rows. Yahoo uses the .NS suffix for NSE
equities; the database stores symbols without it.
"""

from __future__ import annotations

import argparse
import logging
from datetime import datetime, timedelta

import pandas as pd

from db import get_engine, timed, upsert_stocks

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger("void-capital.backfill")

# Watchlist symbols (without the .NS suffix, matching the DB / API).
SYMBOLS = [
    "RELIANCE", "TCS", "HDFCBANK", "INFY", "ICICIBANK",
    "HINDUNILVR", "SBIN", "BHARTIARTL", "ITC", "WIPRO",
]

BACKFILL_DAYS = 365 * 5


def fetch_eod(symbol: str, start: datetime, end: datetime) -> pd.DataFrame:
    """Stage 1: fetch daily EOD data for one NSE equity via yfinance."""
    import yfinance as yf

    ticker = yf.Ticker(f"{symbol}.NS")
    df = ticker.history(start=start, end=end, interval="1d", auto_adjust=False)
    return df


def parse_response(df: pd.DataFrame) -> pd.DataFrame | None:
    """Stage 2: normalize the yfinance DataFrame to clean OHLCV rows.

    yfinance returns OHLCV (+ Dividends, Stock Splits) indexed by a
    timezone-aware DatetimeIndex. Keeps only the columns the DB stores,
    drops any non-business-day rows defensively.
    """
    if df is None or df.empty:
        return None

    parsed = df.copy()
    parsed.columns = [str(c).strip().lower() for c in parsed.columns]

    # yfinance can return a MultiIndex when a ticker history is fetched with
    # certain settings; the second level then holds the OHLCV field name.
    if isinstance(parsed.columns, pd.MultiIndex):
        parsed.columns = parsed.columns.get_level_values(-1)
        parsed.columns = [str(c).strip().lower() for c in parsed.columns]

    # The index is the trading timestamp (DatetimeIndex) -> plain column.
    if "date" not in parsed.columns:
        parsed["date"] = parsed.index

    keep = ["date", "open", "high", "low", "close", "volume"]
    parsed = parsed[keep].copy()
    parsed["date"] = pd.to_datetime(parsed["date"]).dt.tz_localize(None)
    parsed = parsed.dropna(subset=["date", "close", "volume"])
    # Weekends never trade on NSE; drop any Sat/Sun rows defensively.
    parsed = parsed[~parsed["date"].dt.dayofweek.isin([5, 6])]
    parsed["date"] = parsed["date"].dt.date
    return parsed


def enrich_symbol(df: pd.DataFrame, symbol: str) -> pd.DataFrame:
    """Stage 3: add the DB symbol column (no .NS suffix) to each row."""
    df = df.copy()
    df["symbol"] = symbol
    return df


@timed
def backfill_symbol(symbol: str, days: int) -> int:
    """Fetch + parse + enrich + write one symbol. Returns row count written."""
    end = datetime.now()
    start = end - timedelta(days=days)

    try:
        raw = fetch_eod(symbol, start, end)
    except Exception as exc:  # noqa: BLE001 - surface per-symbol failures
        logger.error("%s: fetch failed: %s", symbol, exc)
        return 0

    parsed = parse_response(raw)
    if parsed is None:
        logger.warning("%s: no data returned for range", symbol)
        return 0

    rows = enrich_symbol(parsed, symbol)
    written = upsert_stocks(get_engine(), rows.to_dict("records"))
    logger.info("%s: %d rows upserted", symbol, written)
    return written


def main() -> None:
    parser = argparse.ArgumentParser(description="Backfill NSE EOD data into PostgreSQL")
    parser.add_argument("--days", type=int, default=BACKFILL_DAYS,
                        help="days of history to fetch (default: 5 years)")
    parser.add_argument("--symbols", nargs="*", default=SYMBOLS,
                        help="symbols to backfill (default: watchlist)")
    args = parser.parse_args()

    total = 0
    for symbol in args.symbols:
        written = backfill_symbol(symbol, args.days)
        total += written

    logger.info("Backfill complete: %d total rows written", total)


if __name__ == "__main__":
    main()
