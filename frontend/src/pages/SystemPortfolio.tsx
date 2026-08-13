// System Portfolio page: view any automated (agent) portfolio side by side.
// Header stats, holdings table, recent trade log, resolved-signal log (model
// filterable), and an overlay chart of all agents' portfolio value over time.
// The agent list derives from GET /users (all users except the demo human) so
// newly seeded agents appear automatically.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { useToast } from '../components/useToast';
import { EmptyState, ErrorState, Spinner } from '../components/ui';
import { useUser } from '../context/useUser';
import { CHART_COLORS } from '../constants/chartColors';
import {
  getAdminSettings,
  getComparison,
  getHoldings,
  getPortfolioHistory,
  getResolvedSignals,
  getTrades,
} from '../services/api';
import { USER_ID } from '../services/api';
import type {
  ComparisonPortfolio,
  Holding,
  PagedResolvedSignals,
  PagedTrades,
  PnlSnapshot,
  Settings,
} from '../types';

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

const dateTime = new Intl.DateTimeFormat('en-IN', { dateStyle: 'short', timeStyle: 'short' });
const pct = new Intl.NumberFormat('en-IN', { style: 'percent', maximumFractionDigits: 2 });

const OUTCOME_LABEL: Record<string, string> = {
  HIT_TARGET: 'Hit Target',
  HIT_STOP: 'Hit Stop',
  EXPIRED: 'Expired',
};

export function SystemPortfolio() {
  const { users } = useUser();
  // Agents = every user except the demo human. Derived from the API roster so
  // newly seeded agents show up automatically.
  const agents = useMemo(
    () => [...users].filter((u) => u.id !== USER_ID).sort((a, b) => a.id - b.id),
    [users],
  );
  const [userId, setUserId] = useState(2);
  const [comparison, setComparison] = useState<ComparisonPortfolio | null>(null);
  const [settings, setSettings] = useState<Settings | null>(null);
  const [holdings, setHoldings] = useState<Holding[]>([]);
  const [trades, setTrades] = useState<PagedTrades | null>(null);
  const [resolved, setResolved] = useState<PagedResolvedSignals | null>(null);
  const [histories, setHistories] = useState<Record<number, PnlSnapshot[]>>({});
  const [modelFilter, setModelFilter] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const { showError } = useToast();

  const fetchUserData = useCallback(
    (uid: number) => {
      setLoading(true);
      setError(null);
      Promise.all([
        getComparison(),
        getAdminSettings(uid),
        getHoldings(uid),
        getTrades({ page: 1, pageSize: 10 }, uid),
        getResolvedSignals({ userId: uid, page: 1, pageSize: 10 }),
      ])
        .then(([comp, cfg, hold, tr, res]) => {
          setComparison(comp.portfolios.find((p) => p.userId === uid) ?? null);
          setSettings(cfg);
          setHoldings(hold);
          setTrades(tr);
          setResolved(res);
        })
        .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load system portfolio'))
        .finally(() => setLoading(false));
    },
    [],
  );

  // Fetch every agent's history once for the overlay chart.
  useEffect(() => {
    if (agents.length === 0) return;
    Promise.all(agents.map((a) => getPortfolioHistory(a.userId)))
      .then((all) => {
        const map: Record<number, PnlSnapshot[]> = {};
        agents.forEach((a, i) => {
          map[a.userId] = all[i];
        });
        setHistories(map);
      })
      .catch(() => {
        /* chart data is optional; page body handles its own errors */
      });
  }, [agents]);

  useEffect(() => {
    fetchUserData(userId);
  }, [userId, fetchUserData]);

  const applyModelFilter = () => {
    setResolved(null);
    getResolvedSignals({ userId, model: modelFilter || undefined, page: 1, pageSize: 10 })
      .then(setResolved)
      .catch((err) => showError(err instanceof Error ? err.message : 'Failed to filter signals'));
  };

  const chartData = useCallback(() => {
    const byDate = new Map<string, Record<string, number | undefined>>();
    for (const a of agents) {
      for (const s of histories[a.userId] ?? []) {
        const row = byDate.get(s.date) ?? { date: s.date };
        row[`user${a.userId}`] = s.portfolioValue;
        byDate.set(s.date, row);
      }
    }
    return [...byDate.values()].sort((a, b) => a.date.localeCompare(b.date));
  }, [histories, agents]);

  if (loading && !comparison) return <Spinner />;
  if (error && !comparison) return <ErrorState message={error} onRetry={() => fetchUserData(userId)} />;

  const startingBudget = comparison ? comparison.totalValue - comparison.totalReturn : 0;

  return (
    <div className="system-portfolio-page">
      <div className="page-header">
        <h1>System Portfolio</h1>
        <div className="segmented" data-testid="system-user-selector">
          {agents.map((u) => (
            <button
              key={u.id}
              type="button"
              className={`segmented-btn${userId === u.id ? ' active' : ''}`}
              onClick={() => setUserId(u.id)}
              data-testid={`system-tab-${u.id}`}
            >
              {u.name}
            </button>
          ))}
        </div>
      </div>

      {comparison ? (
        <>
          <div className="stat-grid">
            <div className="stat-card accent-default">
              <div className="stat-label">Starting Budget</div>
              <div className="stat-value">{currency.format(startingBudget)}</div>
            </div>
            <div className="stat-card accent-default">
              <div className="stat-label">Current Cash</div>
              <div className="stat-value">{currency.format(comparison.cash)}</div>
            </div>
            {settings && (
              <div className="stat-card accent-default">
                <div className="stat-label">Negative Limit</div>
                <div className="stat-value">{currency.format(settings.negativeLimit)}</div>
              </div>
            )}
            <div className="stat-card accent-default">
              <div className="stat-label">Total Value</div>
              <div className="stat-value">{currency.format(comparison.totalValue)}</div>
            </div>
            <div className={`stat-card accent-${comparison.totalReturn >= 0 ? 'positive' : 'negative'}`}>
              <div className="stat-label">Total Return</div>
              <div className="stat-value">
                {currency2.format(comparison.totalReturn)} ({pct.format(comparison.totalReturnPercent)})
              </div>
            </div>
          </div>

          <section className="card chart-card">
            <h2>Portfolio Value Over Time</h2>
            {agents.every((a) => (histories[a.userId]?.length ?? 0) === 0) ? (
              <EmptyState message="No portfolio history recorded yet (daily snapshots start once scheduled)" />
            ) : (
              <div className="chart" data-testid="system-chart">
                <ResponsiveContainer width="100%" height={320}>
                  <LineChart data={chartData()}>
                    <XAxis dataKey="date" tick={{ fontSize: 11 }} minTickGap={24} />
                    <YAxis
                      tick={{ fontSize: 11 }}
                      tickFormatter={(v: number) => currency.format(v)}
                      width={90}
                      domain={['auto', 'auto']}
                    />
                    <Tooltip labelStyle={{ fontSize: 12 }} />
                    {agents.map((a, i) => (
                      <Line
                        key={a.id}
                        type="monotone"
                        dataKey={`user${a.id}`}
                        name={a.name}
                        stroke={CHART_COLORS[i % CHART_COLORS.length]}
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
            <h2>Holdings</h2>
            {holdings.length === 0 ? (
              <EmptyState message="No holdings for this system user." />
            ) : (
              <div className="table-wrap">
                <table className="holdings-table" data-testid="system-holdings-table">
                  <thead>
                    <tr>
                      <th>Symbol</th>
                      <th className="num">Quantity</th>
                      <th className="num">Avg Price</th>
                      <th className="num">Current</th>
                      <th className="num">Unrealized P&L</th>
                    </tr>
                  </thead>
                  <tbody>
                    {holdings.map((h) => (
                      <tr key={h.id}>
                        <td className="symbol-cell">{h.symbol}</td>
                        <td className="num">{h.shares}</td>
                        <td className="num">{currency.format(h.avgBuyPrice)}</td>
                        <td className="num">{currency.format(h.currentPrice)}</td>
                        <td className={`num pnl ${h.unrealizedPnl >= 0 ? 'positive' : 'negative'}`}>
                          {currency2.format(h.unrealizedPnl)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="card">
            <h2>Recent Trades</h2>
            {!trades || trades.items.length === 0 ? (
              <EmptyState message="No trades for this system user yet." />
            ) : (
              <div className="table-wrap">
                <table className="holdings-table" data-testid="system-trades-table">
                  <thead>
                    <tr>
                      <th>Date</th>
                      <th>Symbol</th>
                      <th>Type</th>
                      <th className="num">Quantity</th>
                      <th className="num">Price</th>
                      <th className="num">Total</th>
                    </tr>
                  </thead>
                  <tbody>
                    {trades.items.map((t) => (
                      <tr key={t.id}>
                        <td>{dateTime.format(new Date(t.timestamp))}</td>
                        <td className="symbol-cell">{t.symbol}</td>
                        <td>
                          <span className={`badge badge-${t.type.toLowerCase()}`}>{t.type}</span>
                        </td>
                        <td className="num">{t.shares}</td>
                        <td className="num">{currency2.format(t.price)}</td>
                        <td className="num">{currency2.format(t.total)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="card">
            <h2>Signal Resolution Log</h2>
            <div className="filter-bar">
              <label className="field-inline">
                Model
                <select
                  value={modelFilter}
                  onChange={(e) => setModelFilter(e.target.value)}
                  data-testid="resolution-model-filter"
                >
                  <option value="">All models</option>
                  <option value="sma">sma</option>
                  <option value="rsi">rsi</option>
                  <option value="ensemble">ensemble</option>
                </select>
              </label>
              <button type="button" className="btn" onClick={applyModelFilter} data-testid="apply-resolution-filter">
                Apply
              </button>
            </div>
            {!resolved || resolved.items.length === 0 ? (
              <EmptyState message="No resolved signals for this system user." />
            ) : (
              <div className="table-wrap">
                <table className="holdings-table" data-testid="system-resolved-table">
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
                        <td className="num">{currency2.format(r.entryPrice)}</td>
                        <td className="num">{r.exitPrice != null ? currency2.format(r.exitPrice) : '--'}</td>
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
      ) : (
        <ErrorState message={error ?? 'No comparison data available'} onRetry={() => fetchUserData(userId)} />
      )}
    </div>
  );
}

export default SystemPortfolio;
