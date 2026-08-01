"""Shared database access for the Void Capital ETL pipeline.

D2: SQLAlchemy engine + bulk upsert into market_data.stocks.
The table has a composite primary key (symbol, date), which gives us
idempotency: re-running any script never creates duplicate rows.
"""

from __future__ import annotations

import functools
import logging
import os
import time
from typing import Any, Callable, Iterable, TypeVar

import pandas as pd
from dotenv import load_dotenv
from sqlalchemy import create_engine, text
from sqlalchemy.engine import Engine

logger = logging.getLogger("void-capital.db")

# Load .env from the project root (python/.env or one level up).
load_dotenv(os.path.join(os.path.dirname(__file__), ".env"))
load_dotenv(os.path.join(os.path.dirname(__file__), "..", ".env"))

T = TypeVar("T")


def timed(func: Callable[..., T]) -> Callable[..., T]:
    """Decorator: log a function's name and elapsed seconds (SWE: timing)."""

    @functools.wraps(func)
    def wrapper(*args: Any, **kwargs: Any) -> T:
        start = time.perf_counter()
        try:
            result = func(*args, **kwargs)
            elapsed = time.perf_counter() - start
            logger.info("%s completed in %.2fs", func.__name__, elapsed)
            return result
        except Exception:
            elapsed = time.perf_counter() - start
            logger.error("%s failed after %.2fs", func.__name__, elapsed)
            raise

    return wrapper


def get_engine() -> Engine:
    """Build a SQLAlchemy engine from env vars, matching appsettings.Development.json."""
    host = os.getenv("VC_DB_HOST", "localhost")
    port = os.getenv("VC_DB_PORT", "5432")
    db = os.getenv("VC_DB_NAME", "void_capital")
    user = os.getenv("VC_DB_USER", "vc_user")
    password = os.getenv("VC_DB_PASSWORD", "vc_pass")

    return create_engine(
        f"postgresql+psycopg2://{user}:{password}@{host}:{port}/{db}",
        pool_pre_ping=True,
    )


@timed
def upsert_stocks(engine: Engine, rows: Iterable[dict[str, Any]]) -> int:
    """Bulk upsert EOD bars into market_data.stocks.

    Idempotent: ON CONFLICT (symbol, date) DO UPDATE means running the same
    data twice yields the same table state. Returns the number of rows written.
    """
    df = pd.DataFrame(list(rows))
    if df.empty:
        logger.info("upsert_stocks: no rows to write")
        return 0

    # Normalize column names to the DB schema.
    df = df.rename(columns=lambda c: c.strip().lower())
    required = {"symbol", "date", "open", "high", "low", "close", "volume"}
    missing = required - set(df.columns)
    if missing:
        raise ValueError(f"DataFrame missing required columns: {sorted(missing)}")

    df = df[["symbol", "date", "open", "high", "low", "close", "volume"]]
    df["symbol"] = df["symbol"].astype(str).str.upper()
    df["date"] = pd.to_datetime(df["date"]).dt.date

    upsert_sql = text(
        """
        INSERT INTO market_data.stocks (symbol, date, open, high, low, close, volume)
        VALUES (:symbol, :date, :open, :high, :low, :close, :volume)
        ON CONFLICT (symbol, date) DO UPDATE SET
            open = EXCLUDED.open,
            high = EXCLUDED.high,
            low = EXCLUDED.low,
            close = EXCLUDED.close,
            volume = EXCLUDED.volume
        """
    )

    payload = df.to_dict("records")
    with engine.begin() as conn:
        conn.execute(upsert_sql, payload)

    logger.info("upsert_stocks: %d rows written", len(payload))
    return len(payload)
