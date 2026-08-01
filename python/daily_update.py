"""D2: Incremental daily EOD update.

Same pipeline as backfill.py but only fetches the last few days, so it can
be run daily (e.g. scheduled) without re-downloading 5 years of history.
Skips gracefully if the market was closed (no rows returned).
"""

from __future__ import annotations

import argparse
import logging
from datetime import datetime, timedelta

from backfill import SYMBOLS, enrich_symbol, fetch_eod, parse_response
from db import get_engine, timed, upsert_stocks

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
)
logger = logging.getLogger("void-capital.daily")

LOOKBACK_DAYS = 5  # covers a full trading week, including holidays


@timed
def update_symbol(symbol: str, days: int) -> int:
    """Fetch + parse + write one symbol for the recent window."""
    end = datetime.now()
    start = end - timedelta(days=days)

    try:
        raw = fetch_eod(symbol, start, end)
    except Exception as exc:  # noqa: BLE001 - per-symbol failure logging
        logger.error("%s: fetch failed: %s", symbol, exc)
        return 0

    parsed = parse_response(raw)
    if parsed is None:
        logger.info("%s: no new data (market closed or holiday)", symbol)
        return 0

    rows = enrich_symbol(parsed, symbol)
    written = upsert_stocks(get_engine(), rows.to_dict("records"))
    logger.info("%s: %d rows upserted", symbol, written)
    return written


def main() -> None:
    parser = argparse.ArgumentParser(description="Daily NSE EOD sync")
    parser.add_argument("--days", type=int, default=LOOKBACK_DAYS,
                        help="lookback window in days (default: 5)")
    parser.add_argument("--symbols", nargs="*", default=SYMBOLS,
                        help="symbols to update (default: watchlist)")
    args = parser.parse_args()

    total = 0
    for symbol in args.symbols:
        total += update_symbol(symbol, args.days)

    logger.info("Daily update complete: %d total rows written", total)


if __name__ == "__main__":
    main()
