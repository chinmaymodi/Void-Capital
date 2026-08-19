// Compare page (D7.3): side-by-side columns of every user's portfolio (the
// current user first, labeled "Your Portfolio", followed by all other users),
// with an overlay chart of all portfolio curves and a gap summary. The column
// list derives from GET /users so newly seeded users appear automatically.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { EmptyState, ErrorState, Spinner } from '../components/ui';
import { useUser } from '../context/useUser';
import { CHART_COLORS } from '../constants/chartColors';
import { getComparison, getPortfolioHistory } from '../services/api';
import type { ComparisonPortfolio, PnlSnapshot, PortfolioComparison } from '../types';

const currency = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 0,
});

const currency2 = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 2,
});

const pct = new Intl.NumberFormat('en-IN', { style: 'percent', maximumFractionDigits: 2 });

// Risk metrics from a daily portfolio-value series (D17 overlay).
// CAGR: annualized growth over the series span. Sharpe: mean/std of daily
// returns annualized by sqrt(252). Max drawdown: worst peak-to-trough.
// Trading-day convention: the daily cycle records one snapshot per trading
// day, so values.length - 1 is the trading-day span. This matches the
// sqrt(252) Sharpe annualization used across the system (metrics.py, F1-F7)
// and avoids Infinity when the series spans a single calendar day.
export function riskMetrics(snaps: PnlSnapshot[]) {
  if (snaps.length < 2) return null;
  const sorted = [...snaps].sort((a, b) => String(a.date).localeCompare(String(b.date)));
  const values = sorted.map((s) => s.portfolioValue);
  const start = values[0];
  const end = values[values.length - 1];
  const tradingDays = Math.max(1, values.length - 1);
  const cagr = start > 0 ? Math.pow(end / start, 252 / tradingDays) - 1 : -1;
  const returns: number[] = [];
  for (let i = 1; i < values.length; i++) {
    if (values[i - 1] > 0) returns.push(values[i] / values[i - 1] - 1);
  }
  const mean = returns.reduce((a, b) => a + b, 0) / Math.max(1, returns.length);
  const variance = returns.reduce((a, b) => a + (b - mean) ** 2, 0) / Math.max(1, returns.length);
  const std = Math.sqrt(variance);
  const sharpe = std > 0 ? (mean / std) * Math.sqrt(252) : 0;
  let peak = values[0];
  let maxDd = 0;
  for (const v of values) {
    if (v > peak) peak = v;
    if (peak > 0) maxDd = Math.min(maxDd, v / peak - 1);
  }
  return { cagr, sharpe, maxDrawdown: maxDd };
}

export function Compare() {
  const { users, currentUserId } = useUser();
  const [comparison, setComparison] = useState<PortfolioComparison | null>(null);
  const [histories, setHistories] = useState<Record<number, PnlSnapshot[]>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Column list derives from the user roster: current user first ("Your
  // Portfolio"), then everyone else by name. Colors cycle the palette.
  const columns = useMemo(
    () =>
      [...users]
        .sort((a, b) => a.id - b.id)
        .sort((a, b) => (a.id === currentUserId ? -1 : 0) - (b.id === currentUserId ? -1 : 0))
        .map((u, i) => ({
          key: `user${u.id}`,
          label: u.id === currentUserId ? 'Your Portfolio' : u.name,
          userId: u.id,
          stroke: CHART_COLORS[i % CHART_COLORS.length],
        })),
    [users, currentUserId],
  );

  const fetchData = useCallback(() => {
    setLoading(true);
    setError(null);
    Promise.all([
      getComparison(),
      ...columns.map((c) => getPortfolioHistory(c.userId)),
    ])
      .then(([comp, ...history]) => {
        setComparison(comp);
        const map: Record<number, PnlSnapshot[]> = {};
        columns.forEach((c, i) => {
          map[c.userId] = history[i];
        });
        setHistories(map);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load comparison'))
      .finally(() => setLoading(false));
  }, [columns]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const portfolioFor = (userId: number): ComparisonPortfolio | undefined =>
    comparison?.portfolios.find((p) => p.userId === userId);

  // Merge the histories into rows keyed by date.
  const chartData = useCallback(() => {
    const byDate = new Map<string, Record<string, number | string>>();
    for (const { userId } of columns) {
      for (const snap of histories[userId] ?? []) {
        const row = byDate.get(snap.date) ?? { date: snap.date };
        row[`user${userId}`] = snap.portfolioValue;
        byDate.set(snap.date, row);
      }
    }
    return [...byDate.values()].sort((a, b) => String(a.date).localeCompare(String(b.date)));
  }, [histories, columns]);

  const hasAnyHistory = columns.some((c) => (histories[c.userId]?.length ?? 0) > 0);

  if (loading) return <Spinner />;
  if (error) return <ErrorState message={error} onRetry={fetchData} />;

  return (
    <div className="compare-page">
      <div className="page-header">
        <h1>Portfolio Comparison</h1>
      </div>

      <div className="compare-grid" data-testid="compare-grid">
        {columns.map((c) => {
          const p = portfolioFor(c.userId);
          const m = riskMetrics(histories[c.userId] ?? []);
          return (
            <div key={c.key} className={`compare-column compare-${c.key}`} data-testid={`column-${c.key}`}>
              <h2>{c.label}</h2>
              {p ? (
                <>
                  <dl className="compare-stats">
                    <div>
                      <dt>Cash</dt>
                      <dd>{currency.format(p.cash)}</dd>
                    </div>
                    <div>
                      <dt>Holdings</dt>
                      <dd>{currency.format(p.holdingsValue)}</dd>
                    </div>
                    <div>
                      <dt>Total Value</dt>
                      <dd className="strong">{currency.format(p.totalValue)}</dd>
                    </div>
                    <div>
                      <dt>Total Return</dt>
                      <dd className={`pnl ${p.totalReturn >= 0 ? 'positive' : 'negative'}`}>
                        {currency2.format(p.totalReturn)} ({pct.format(p.totalReturnPercent)})
                      </dd>
                    </div>
                    {m && (
                      <>
                        <div>
                          <dt>CAGR</dt>
                          <dd className={`pnl ${m.cagr >= 0 ? 'positive' : 'negative'}`}>{pct.format(m.cagr)}</dd>
                        </div>
                        <div>
                          <dt>Sharpe</dt>
                          <dd className={`pnl ${m.sharpe >= 0 ? 'positive' : 'negative'}`}>{m.sharpe.toFixed(2)}</dd>
                        </div>
                        <div>
                          <dt>Max Drawdown</dt>
                          <dd className="pnl negative">{pct.format(m.maxDrawdown)}</dd>
                        </div>
                      </>
                    )}
                  </dl>
                </>
              ) : (
                <EmptyState message="No data" />
              )}
            </div>
          );
        })}
      </div>

      <section className="card chart-card">
        <h2>Portfolio Value Over Time</h2>
        {!hasAnyHistory ? (
          <EmptyState message="No portfolio history recorded yet (daily snapshots start once scheduled)" />
        ) : (
          <div className="chart" data-testid="compare-chart">
            <ResponsiveContainer width="100%" height={340}>
              <LineChart data={chartData()}>
                <XAxis dataKey="date" tick={{ fontSize: 11 }} minTickGap={24} />
                <YAxis
                  tick={{ fontSize: 11 }}
                  tickFormatter={(v: number) => currency.format(v)}
                  width={90}
                  domain={['auto', 'auto']}
                />
                <Tooltip labelStyle={{ fontSize: 12 }} />
                <Legend />
                {columns.map((c) => (
                  <Line
                    key={c.userId}
                    type="monotone"
                    dataKey={`user${c.userId}`}
                    name={c.label}
                    stroke={c.stroke}
                    strokeWidth={2}
                    dot={false}
                  />
                ))}
              </LineChart>
            </ResponsiveContainer>
          </div>
        )}
      </section>

      <section className="card">
        <h2>Gap Summary</h2>
        {!comparison || comparison.gaps.length === 0 ? (
          <EmptyState message="Not enough portfolios to compare." />
        ) : (
          <div className="table-wrap">
            <table className="holdings-table" data-testid="gap-table">
              <thead>
                <tr>
                  <th>Leader</th>
                  <th>Trailing</th>
                  <th className="num">Gap (Rs)</th>
                  <th className="num">Gap (%)</th>
                </tr>
              </thead>
              <tbody>
                {comparison.gaps.map((g, i) => (
                  <tr key={i}>
                    <td className="symbol-cell">{g.leader}</td>
                    <td className="symbol-cell">{g.trailer}</td>
                    <td className="num">{currency.format(g.gapRupees)}</td>
                    <td className="num">{pct.format(g.gapPercent)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

export default Compare;
