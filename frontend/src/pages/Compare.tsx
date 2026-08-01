// Compare page (D7.3): three-column comparison of Your Portfolio (user 1),
// System (user 2), and System-Reckless (user 3), with an overlay chart of all
// three portfolio curves and a gap summary. Data comes from
// GET /performance/compare + the three portfolio history endpoints.

import { useCallback, useEffect, useState } from 'react';
import { Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { EmptyState, ErrorState, Spinner } from '../components/ui';
import { getComparison, getPortfolioHistory } from '../services/api';
import type { ComparisonPortfolio, PnlSnapshot, PortfolioComparison } from '../types';

const COLUMNS = [
  { key: 'your', label: 'Your Portfolio', userId: 1 },
  { key: 'system', label: 'System', userId: 2 },
  { key: 'reckless', label: 'System-Reckless', userId: 3 },
] as const;

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

const SERIES: Record<string, { stroke: string; label: string }> = {
  user1: { stroke: '#4f8ef7', label: 'Your Portfolio' },
  user2: { stroke: '#3dbf7d', label: 'System' },
  user3: { stroke: '#e8a13a', label: 'System-Reckless' },
};

export function Compare() {
  const [comparison, setComparison] = useState<PortfolioComparison | null>(null);
  const [histories, setHistories] = useState<Record<number, PnlSnapshot[]>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(() => {
    setLoading(true);
    setError(null);
    Promise.all([
      getComparison(),
      ...COLUMNS.map((c) => getPortfolioHistory(c.userId)),
    ])
      .then(([comp, ...history]) => {
        setComparison(comp);
        const map: Record<number, PnlSnapshot[]> = {};
        COLUMNS.forEach((c, i) => {
          map[c.userId] = history[i];
        });
        setHistories(map);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load comparison'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const portfolioFor = (userId: number): ComparisonPortfolio | undefined =>
    comparison?.portfolios.find((p) => p.userId === userId);

  // Merge the three histories into rows keyed by date.
  const chartData = useCallback(() => {
    const byDate = new Map<string, Record<string, number | string>>();
    for (const { userId } of COLUMNS) {
      for (const snap of histories[userId] ?? []) {
        const row = byDate.get(snap.date) ?? { date: snap.date };
        row[`user${userId}`] = snap.portfolioValue;
        byDate.set(snap.date, row);
      }
    }
    return [...byDate.values()].sort((a, b) => String(a.date).localeCompare(String(b.date)));
  }, [histories]);

  const hasAnyHistory = COLUMNS.some((c) => (histories[c.userId]?.length ?? 0) > 0);

  if (loading) return <Spinner />;
  if (error) return <ErrorState message={error} onRetry={fetchData} />;

  return (
    <div className="compare-page">
      <div className="page-header">
        <h1>Portfolio Comparison</h1>
      </div>

      <div className="compare-grid" data-testid="compare-grid">
        {COLUMNS.map((c) => {
          const p = portfolioFor(c.userId);
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
                {COLUMNS.map((c) => (
                  <Line
                    key={c.userId}
                    type="monotone"
                    dataKey={`user${c.userId}`}
                    name={c.label}
                    stroke={SERIES[`user${c.userId}`].stroke}
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
