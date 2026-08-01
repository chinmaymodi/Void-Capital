// Signal Performance page (D7.3): per-model summary cards (win rate, avg
// return, best/worst), a resolved-signal table with outcome badges, and
// charts: win-rate bar comparison + cumulative return per model over time.

import { useCallback, useEffect, useState } from 'react';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { EmptyState, ErrorState, Spinner } from '../components/ui';
import { getModelPerformance, getResolvedSignals } from '../services/api';
import type { ModelPerformance, PagedResolvedSignals, ResolvedSignal } from '../types';

const currency = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 2,
});

const pct = new Intl.NumberFormat('en-IN', { style: 'percent', maximumFractionDigits: 2 });

const OUTCOME_LABEL: Record<string, string> = {
  HIT_TARGET: 'Hit Target',
  HIT_STOP: 'Hit Stop',
  EXPIRED: 'Expired',
};

// Compute per-model cumulative return series from resolved signals.
function buildCumulativeSeries(resolved: ResolvedSignal[]) {
  const byModel = new Map<string, { date: string; cumulative: number }[]>();
  for (const r of resolved) {
    if (r.actualReturn == null) continue;
    const points = byModel.get(r.modelName) ?? [];
    points.push({ date: r.date, cumulative: r.actualReturn });
    byModel.set(r.modelName, points);
  }

  // Flatten to { date, [modelName]: cumulative } rows for recharts.
  const rows: Record<string, string | number>[] = [];
  const add = (model: string, points: { date: string; cumulative: number }[]) => {
    let running = 0;
    for (const p of points.sort((a, b) => a.date.localeCompare(b.date))) {
      running += p.cumulative;
      const row = rows.find((r) => r.date === p.date);
      if (row) row[model] = running;
      else rows.push({ date: p.date, [model]: running });
    }
  };
  for (const [model, points] of byModel) add(model, points);
  return rows.sort((a, b) => String(a.date).localeCompare(String(b.date)));
}

const MODEL_COLORS = ['#4f8ef7', '#3dbf7d', '#e8a13a', '#b05ce6'];

export function SignalPerformance() {
  const [models, setModels] = useState<ModelPerformance[]>([]);
  const [resolved, setResolved] = useState<PagedResolvedSignals | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(() => {
    setLoading(true);
    setError(null);
    Promise.all([getModelPerformance(), getResolvedSignals({ pageSize: 100 })])
      .then(([m, r]) => {
        setModels(m);
        setResolved(r);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load performance'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  if (loading) return <Spinner />;
  if (error) return <ErrorState message={error} onRetry={fetchData} />;

  const winRateData = models.map((m) => ({
    name: m.modelName,
    winRate: m.winRate,
  }));
  const cumulativeData = buildCumulativeSeries(resolved?.items ?? []);

  return (
    <div className="performance-page">
      <div className="page-header">
        <h1>Signal Performance</h1>
      </div>

      {models.length === 0 ? (
        <EmptyState message="No model performance data yet. Signals resolve after their evaluation window." />
      ) : (
        <>
          <div className="stat-grid">
            {models.map((m) => (
              <div key={m.modelName} className="stat-card accent-default">
                <div className="stat-label">{m.modelName}</div>
                <div className="stat-value">
                  {m.resolvedSignals}/{m.totalSignals} resolved
                </div>
                <div className="stat-sub" data-testid={`winrate-${m.modelName}`}>
                  Win rate {pct.format(m.winRate)} · Avg {pct.format(m.avgReturn)}
                </div>
              </div>
            ))}
          </div>

          <section className="card chart-card">
            <h2>Win Rate by Model</h2>
            <div className="chart" data-testid="winrate-chart">
              <ResponsiveContainer width="100%" height={280}>
                <BarChart data={winRateData}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="name" tick={{ fontSize: 11 }} />
                  <YAxis
                    tick={{ fontSize: 11 }}
                    tickFormatter={(v: number) => pct.format(v)}
                    domain={[0, 1]}
                  />
                  <Tooltip formatter={(value) => [pct.format(Number(value)), 'Win rate']} />
                  <Bar dataKey="winRate" fill="#3dbf7d" radius={[4, 4, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          </section>

          <section className="card chart-card">
            <h2>Cumulative Return by Model</h2>
            {cumulativeData.length === 0 ? (
              <EmptyState message="No resolved signals with returns yet." />
            ) : (
              <div className="chart" data-testid="cumulative-chart">
                <ResponsiveContainer width="100%" height={300}>
                  <LineChart data={cumulativeData}>
                    <XAxis dataKey="date" tick={{ fontSize: 11 }} minTickGap={24} />
                    <YAxis
                      tick={{ fontSize: 11 }}
                      tickFormatter={(v: number) => pct.format(v)}
                      domain={['auto', 'auto']}
                    />
                    <Tooltip
                      formatter={(value, name) => [pct.format(Number(value)), String(name)]}
                    />
                    {models.map((m, i) => (
                      <Line
                        key={m.modelName}
                        type="monotone"
                        dataKey={m.modelName}
                        stroke={MODEL_COLORS[i % MODEL_COLORS.length]}
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
            <h2>Recent Resolved Signals</h2>
            {!resolved || resolved.items.length === 0 ? (
              <EmptyState message="No resolved signals yet." />
            ) : (
              <div className="table-wrap">
                <table className="holdings-table" data-testid="resolved-table">
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Symbol</th>
                      <th>Action</th>
                      <th>Model</th>
                      <th className="num">Entry</th>
                      <th className="num">Exit</th>
                      <th>Outcome</th>
                      <th className="num">Return</th>
                    </tr>
                  </thead>
                  <tbody>
                    {resolved.items.map((r) => (
                      <tr key={r.signalId}>
                        <td>{r.date}</td>
                        <td className="symbol-cell">{r.symbol}</td>
                        <td>
                          <span className={`badge badge-${r.action.toLowerCase()}`}>{r.action}</span>
                        </td>
                        <td>{r.modelName}</td>
                        <td className="num">{currency.format(r.entryPrice)}</td>
                        <td className="num">{r.exitPrice != null ? currency.format(r.exitPrice) : '--'}</td>
                        <td>
                          <span className={`badge badge-outcome badge-${r.outcome.toLowerCase()}`}>
                            {OUTCOME_LABEL[r.outcome]}
                          </span>
                        </td>
                        <td className={`num pnl ${(r.actualReturn ?? 0) >= 0 ? 'positive' : 'negative'}`}>
                          {r.actualReturn != null ? pct.format(r.actualReturn) : '--'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}
    </div>
  );
}

export default SignalPerformance;
