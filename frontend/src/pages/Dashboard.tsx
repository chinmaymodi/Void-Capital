// Dashboard page: portfolio stats cards + portfolio value history chart.
// Container component: owns data fetching, composes presentational parts.

import { useCallback, useEffect, useState } from 'react';
import { Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { useUser } from '../context/useUser';
import { EmptyState, ErrorState, Spinner, StatCard } from '../components/ui';
import { getPortfolio, getPortfolioHistory } from '../services/api';
import type { PnlSnapshot, PortfolioState } from '../types';

const currency = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 0,
});

export function Dashboard() {
  const [state, setState] = useState<PortfolioState | null>(null);
  const [history, setHistory] = useState<PnlSnapshot[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const { currentUserId } = useUser();

  const fetchData = useCallback(() => {
    setLoading(true);
    setError(null);
    Promise.all([getPortfolio(currentUserId), getPortfolioHistory(currentUserId)])
      .then(([portfolio, snapshots]) => {
        setState(portfolio);
        setHistory(snapshots);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load portfolio'))
      .finally(() => setLoading(false));
  }, [currentUserId]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  if (loading) return <Spinner />;
  if (error) return <ErrorState message={error} onRetry={fetchData} />;
  if (!state) return <EmptyState message="No portfolio data available" />;

  const chartData = history.map((h) => ({ date: h.date, value: h.portfolioValue }));

  return (
    <div className="dashboard">
      <h1>Dashboard</h1>
      <div className="stat-grid">
        <StatCard label="Cash" value={currency.format(state.cash)} />
        <StatCard label="Holdings" value={currency.format(state.holdingsValue)} />
        <StatCard
          label="Total"
          value={currency.format(state.totalValue)}
          accent={state.holdingsValue >= 0 ? 'positive' : 'negative'}
        />
      </div>

      <section className="card chart-card">
        <h2>Portfolio Value History</h2>
        {chartData.length === 0 ? (
          <EmptyState message="No portfolio history recorded yet (daily snapshots start once scheduled)" />
        ) : (
          <div className="chart" data-testid="portfolio-chart">
            <ResponsiveContainer width="100%" height={320}>
              <LineChart data={chartData}>
                <XAxis dataKey="date" tick={{ fontSize: 11 }} minTickGap={24} />
                <YAxis
                  tick={{ fontSize: 11 }}
                  tickFormatter={(v: number) => currency.format(v)}
                  width={90}
                  domain={['auto', 'auto']}
                />
                <Tooltip
                  formatter={(value) => [currency.format(Number(value)), 'Total']}
                  labelStyle={{ fontSize: 12 }}
                />
                <Line
                  type="monotone"
                  dataKey="value"
                  stroke="#4f8ef7"
                  strokeWidth={2}
                  dot={false}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        )}
      </section>
    </div>
  );
}

export default Dashboard;
