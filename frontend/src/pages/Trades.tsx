// Trade log page: paginated table with filters (symbol, type, date range)
// and CSV export. Container component owning fetch + filter state.

import { useCallback, useEffect, useState } from 'react';
import { EmptyState, ErrorState, Spinner } from '../components/ui';
import { getTrades } from '../services/api';
import type { PagedTrades, TradeFilters, TradeType } from '../types';

const PAGE_SIZES = [10, 20, 50];

const currency = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 2,
});

const dateTime = new Intl.DateTimeFormat('en-IN', {
  dateStyle: 'short',
  timeStyle: 'short',
});

export function Trades() {
  const [data, setData] = useState<PagedTrades | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [symbol, setSymbol] = useState('');
  const [type, setType] = useState<TradeType | ''>('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  // Drafts for the filter bar; applied only on "Apply".
  const [draftSymbol, setDraftSymbol] = useState('');
  const [draftType, setDraftType] = useState<TradeType | ''>('');
  const [draftFrom, setDraftFrom] = useState('');
  const [draftTo, setDraftTo] = useState('');

  const fetchData = useCallback(() => {
    setLoading(true);
    setError(null);
    const filters: TradeFilters = {
      page,
      pageSize,
      symbol: symbol || undefined,
      type: type || undefined,
      from: from || undefined,
      to: to || undefined,
    };
    getTrades(filters)
      .then(setData)
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load trades'))
      .finally(() => setLoading(false));
  }, [page, pageSize, symbol, type, from, to]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const applyFilters = () => {
    setSymbol(draftSymbol.trim().toUpperCase());
    setType(draftType);
    setFrom(draftFrom);
    setTo(draftTo);
    setPage(1);
  };

  const resetFilters = () => {
    setDraftSymbol('');
    setDraftType('');
    setDraftFrom('');
    setDraftTo('');
    setSymbol('');
    setType('');
    setFrom('');
    setTo('');
    setPage(1);
  };

  const exportCsv = () => {
    const params = new URLSearchParams();
    if (symbol) params.set('symbol', symbol);
    if (type) params.set('type', type);
    if (from) params.set('from', from);
    if (to) params.set('to', to);
    window.open(`/api/v1/trades/1/export?${params.toString()}`, '_blank');
  };

  const totalPages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1;

  if (loading) return <Spinner />;
  if (error) return <ErrorState message={error} onRetry={fetchData} />;

  return (
    <div className="trades-page">
      <div className="page-header">
        <h1>Trade Log</h1>
        <button type="button" className="btn" onClick={exportCsv} data-testid="export-csv">
          Export CSV
        </button>
      </div>

      <div className="filter-bar">
        <label className="field-inline">
          Symbol
          <input
            type="text"
            value={draftSymbol}
            onChange={(e) => setDraftSymbol(e.target.value)}
            placeholder="e.g. RELIANCE"
            data-testid="filter-symbol"
          />
        </label>
        <label className="field-inline">
          Type
          <select value={draftType} onChange={(e) => setDraftType(e.target.value as TradeType | '')} data-testid="filter-type">
            <option value="">All</option>
            <option value="BUY">BUY</option>
            <option value="SELL">SELL</option>
          </select>
        </label>
        <label className="field-inline">
          From
          <input type="date" value={draftFrom} onChange={(e) => setDraftFrom(e.target.value)} data-testid="filter-from" />
        </label>
        <label className="field-inline">
          To
          <input type="date" value={draftTo} onChange={(e) => setDraftTo(e.target.value)} data-testid="filter-to" />
        </label>
        <button type="button" className="btn" onClick={applyFilters} data-testid="apply-filters">
          Apply
        </button>
        <button type="button" className="btn" onClick={resetFilters}>
          Reset
        </button>
      </div>

      {data && data.items.length === 0 ? (
        <EmptyState message="No trades match these filters." />
      ) : (
        <>
          <div className="table-wrap">
            <table className="trades-table" data-testid="trades-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Symbol</th>
                  <th>Type</th>
                  <th className="num">Shares</th>
                  <th className="num">Price</th>
                  <th className="num">Total</th>
                  <th>Reason</th>
                </tr>
              </thead>
              <tbody>
                {data?.items.map((t) => (
                  <tr key={t.id}>
                    <td>{dateTime.format(new Date(t.timestamp))}</td>
                    <td className="symbol-cell">{t.symbol}</td>
                    <td>
                      <span className={`badge badge-${t.type.toLowerCase()}`}>{t.type}</span>
                    </td>
                    <td className="num">{t.shares}</td>
                    <td className="num">{currency.format(t.price)}</td>
                    <td className="num">{currency.format(t.total)}</td>
                    <td>{t.reason ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="pagination-bar">
            <span>
              {data?.total ?? 0} trades · page {data?.page ?? 1} of {totalPages}
            </span>
            <div className="pagination-controls">
              <label className="field-inline">
                Rows
                <select
                  value={pageSize}
                  onChange={(e) => {
                    setPageSize(Number(e.target.value));
                    setPage(1);
                  }}
                  data-testid="page-size"
                >
                  {PAGE_SIZES.map((n) => (
                    <option key={n} value={n}>
                      {n}
                    </option>
                  ))}
                </select>
              </label>
              <button type="button" className="btn" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                Prev
              </button>
              <button type="button" className="btn" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
                Next
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

export default Trades;
