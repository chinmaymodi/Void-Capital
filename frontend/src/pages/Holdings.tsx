// Holdings page: sortable table with P&L coloring + buy/sell modal.
// Container component: owns data + mutation state, composes presentational parts.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { EmptyState, ErrorState, Spinner } from '../components/ui';
import { useToast } from '../components/useToast';
import { buyStock, getHoldings, sellStock } from '../services/api';
import type { Holding, TradeType } from '../types';

const currency = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 2,
});

type SortKey = 'symbol' | 'shares' | 'avgBuyPrice' | 'currentPrice' | 'unrealizedPnl' | 'percentOfPortfolio';
type SortDir = 'asc' | 'desc';

const SORTABLE_HEADERS: { key: SortKey; label: string; align?: string }[] = [
  { key: 'symbol', label: 'Symbol' },
  { key: 'shares', label: 'Shares', align: 'right' },
  { key: 'avgBuyPrice', label: 'Avg Buy', align: 'right' },
  { key: 'currentPrice', label: 'Current', align: 'right' },
  { key: 'unrealizedPnl', label: 'P&L', align: 'right' },
  { key: 'percentOfPortfolio', label: '% of Portfolio', align: 'right' },
];

export function Holdings() {
  const [holdings, setHoldings] = useState<Holding[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [sortKey, setSortKey] = useState<SortKey>('symbol');
  const [sortDir, setSortDir] = useState<SortDir>('asc');
  const [modal, setModal] = useState<{ symbol: string; type: TradeType } | null>(null);
  const { showError, showSuccess } = useToast();

  const fetchData = useCallback(() => {
    setLoading(true);
    setError(null);
    getHoldings()
      .then(setHoldings)
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load holdings'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const sorted = useMemo(() => {
    const dir = sortDir === 'asc' ? 1 : -1;
    return [...holdings].sort((a, b) => {
      const av = a[sortKey];
      const bv = b[sortKey];
      if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * dir;
      return String(av).localeCompare(String(bv)) * dir;
    });
  }, [holdings, sortKey, sortDir]);

  const toggleSort = (key: SortKey) => {
    if (key === sortKey) {
      setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortKey(key);
      setSortDir('asc');
    }
  };

  const handleTrade = useCallback(
    async (symbol: string, shares: number, type: TradeType) => {
      try {
        const trade =
          type === 'BUY'
            ? await buyStock({ symbol, shares })
            : await sellStock({ symbol, shares });
        showSuccess(`${type} ${shares} ${symbol} @ ${currency.format(trade.price)}`);
        setModal(null);
        fetchData();
      } catch (err) {
        showError(err instanceof Error ? err.message : 'Trade failed');
      }
    },
    [fetchData, showError, showSuccess],
  );

  if (loading) return <Spinner />;
  if (error) return <ErrorState message={error} onRetry={fetchData} />;

  return (
    <div className="holdings-page">
      <h1>Holdings</h1>
      {holdings.length === 0 ? (
        <EmptyState message="No holdings yet. Use Buy to open a position." />
      ) : (
        <div className="table-wrap">
          <table className="holdings-table" data-testid="holdings-table">
            <thead>
              <tr>
                {SORTABLE_HEADERS.map((h) => (
                  <th
                    key={h.key}
                    className={h.align === 'right' ? 'num' : ''}
                    aria-sort={sortKey === h.key ? (sortDir === 'asc' ? 'ascending' : 'descending') : undefined}
                  >
                    <button type="button" className="sort-btn" onClick={() => toggleSort(h.key)}>
                      {h.label}
                      {sortKey === h.key && <span className="sort-arrow">{sortDir === 'asc' ? ' ▲' : ' ▼'}</span>}
                    </button>
                  </th>
                ))}
                <th className="actions-col">Actions</th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((h) => (
                <tr key={h.id}>
                  <td className="symbol-cell">{h.symbol}</td>
                  <td className="num">{h.shares}</td>
                  <td className="num">{currency.format(h.avgBuyPrice)}</td>
                  <td className="num">{currency.format(h.currentPrice)}</td>
                  <td className={`num pnl ${h.unrealizedPnl >= 0 ? 'positive' : 'negative'}`}>
                    {currency.format(h.unrealizedPnl)}
                  </td>
                  <td className="num">{(h.percentOfPortfolio * 100).toFixed(1)}%</td>
                  <td className="actions-col">
                    <button type="button" className="btn btn-buy" onClick={() => setModal({ symbol: h.symbol, type: 'BUY' })}>
                      Buy
                    </button>
                    <button type="button" className="btn btn-sell" onClick={() => setModal({ symbol: h.symbol, type: 'SELL' })}>
                      Sell
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {modal && (
        <TradeModal
          symbol={modal.symbol}
          type={modal.type}
          currentHoldings={holdings.find((h) => h.symbol === modal.symbol)?.shares ?? 0}
          onClose={() => setModal(null)}
          onSubmit={handleTrade}
        />
      )}
    </div>
  );
}

// ---------- Presentational pieces ----------

interface TradeModalProps {
  symbol: string;
  type: TradeType;
  currentHoldings: number;
  onClose: () => void;
  onSubmit: (symbol: string, shares: number, type: TradeType) => Promise<void>;
}

function TradeModal({ symbol, type, currentHoldings, onClose, onSubmit }: TradeModalProps) {
  const [shares, setShares] = useState<string>('');
  const [submitting, setSubmitting] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);

  const isBuy = type === 'BUY';
  const parsed = Number.parseInt(shares, 10);

  const submit = async () => {
    if (!Number.isInteger(parsed) || parsed <= 0) {
      setLocalError('Enter a positive whole number of shares.');
      return;
    }
    if (!isBuy && parsed > currentHoldings) {
      setLocalError(`You only hold ${currentHoldings} shares of ${symbol}.`);
      return;
    }
    setSubmitting(true);
    setLocalError(null);
    try {
      await onSubmit(symbol, parsed, type);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={`${type} ${symbol}`} onClick={(e) => e.stopPropagation()}>
        <h2>
          {type} {symbol}
        </h2>
        {!isBuy && (
          <p className="modal-hint">
            Current holding: {currentHoldings} shares
          </p>
        )}
        <label className="field">
          Shares
          <input
            type="number"
            min="1"
            value={shares}
            onChange={(e) => setShares(e.target.value)}
            autoFocus
            data-testid="shares-input"
          />
        </label>
        {localError && <p className="form-error" role="alert">{localError}</p>}
        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button
            type="button"
            className={`btn ${isBuy ? 'btn-buy' : 'btn-sell'}`}
            onClick={submit}
            disabled={submitting}
            data-testid="submit-trade"
          >
            {submitting ? 'Submitting...' : `Confirm ${type}`}
          </button>
        </div>
      </div>
    </div>
  );
}

export default Holdings;
