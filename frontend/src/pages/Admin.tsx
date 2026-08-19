// Admin page (D7.2): signal generation control, per-agent portfolio limits
// config, global settings, and manual square-off with confirmation. The agent
// list derives from GET /users (all users except the demo human) so newly
// seeded agents appear automatically. run-signals runs as a background job
// (the Python pipeline takes minutes); the page polls until it completes.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useToast } from '../components/useToast';
import { ErrorState, Spinner, StatCard } from '../components/ui';
import { useUser } from '../context/useUser';
import {
  getAdminSettings,
  getAdminStatus,
  runSignalGenerationAndWait,
  squareOff,
  updateAdminSettings,
  updateGlobalSettings,
} from '../services/api';
import { USER_ID } from '../services/api';
import type { AdminStatus, Settings, SquareOffResult } from '../types';

const currency = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 0,
});

export function Admin() {
  const { users } = useUser();
  // Agents = every user except the demo human. Derived from the API roster so
  // newly seeded agents show up automatically.
  const agents = useMemo(
    () => [...users].filter((u) => u.id !== USER_ID).sort((a, b) => a.id - b.id),
    [users],
  );
  const agentLabel = useCallback(
    (userId: number) => agents.find((u) => u.id === userId)?.name ?? `User ${userId}`,
    [agents],
  );
  const [status, setStatus] = useState<AdminStatus | null>(null);
  const [configs, setConfigs] = useState<Record<number, Settings>>({});
  const [minConfidence, setMinConfidence] = useState('0.50');
  const [watchlist, setWatchlist] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [lastRun, setLastRun] = useState<string | null>(null);
  const [saving, setSaving] = useState<Record<number, boolean>>({});
  const [savingGlobal, setSavingGlobal] = useState(false);
  const [squareOffTarget, setSquareOffTarget] = useState<Settings | null>(null);
  const [squareOffBusy, setSquareOffBusy] = useState(false);
  const [squareOffResult, setSquareOffResult] = useState<SquareOffResult | null>(null);
  const { showError, showSuccess } = useToast();

  const fetchData = useCallback(() => {
    setLoading(true);
    setError(null);
    Promise.all([
      getAdminStatus(),
      ...agents.map((u) => getAdminSettings(u.id)),
    ])
      .then(([st, ...cfg]) => {
        setStatus(st);
        const map: Record<number, Settings> = {};
        cfg.forEach((c) => {
          map[c.userId] = c;
        });
        setConfigs(map);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load admin data'))
      .finally(() => setLoading(false));
  }, [agents]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const setConfigField = (userId: number, patch: Partial<Settings>) => {
    setConfigs((current) => ({
      ...current,
      [userId]: current[userId] ? { ...current[userId], ...patch } : current[userId],
    }));
  };

  const handleRunSignals = async () => {
    setRunning(true);
    try {
      const job = await runSignalGenerationAndWait();
      setLastRun(new Date().toLocaleTimeString());
      if (job.status === 'SUCCEEDED') {
        showSuccess(job.message ?? 'Signal generation complete');
      } else {
        showError(job.message ?? 'Signal generation failed');
      }
      fetchData(); // refresh pending count
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Failed to trigger signal generation');
    } finally {
      setRunning(false);
    }
  };

  const saveUserConfig = async (userId: number) => {
    const config = configs[userId];
    if (!config) return;
    setSaving((s) => ({ ...s, [userId]: true }));
    try {
      const updated = await updateAdminSettings(userId, config);
      setConfigs((current) => ({ ...current, [userId]: updated }));
      showSuccess(`Saved ${agentLabel(userId)} config`);
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Failed to save config');
    } finally {
      setSaving((s) => ({ ...s, [userId]: false }));
    }
  };

  const saveGlobal = async () => {
    const mc = Number(minConfidence);
    if (!Number.isFinite(mc) || mc < 0 || mc > 1) {
      showError('Min confidence must be between 0 and 1');
      setSavingGlobal(false);
      return;
    }
    setSavingGlobal(true);
    try {
      const symbols = watchlist
        .split(',')
        .map((s) => s.trim().toUpperCase())
        .filter(Boolean);
      const updated = await updateGlobalSettings(mc, symbols);
      setConfigs(
        updated.reduce<Record<number, Settings>>((acc, s) => {
          acc[s.userId] = s;
          return acc;
        }, {}),
      );
      showSuccess('Global settings saved');
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Failed to save global settings');
    } finally {
      setSavingGlobal(false);
    }
  };

  const confirmSquareOff = (config: Settings) => {
    setSquareOffResult(null);
    setSquareOffTarget(config);
  };

  const runSquareOff = async () => {
    if (!squareOffTarget) return;
    setSquareOffBusy(true);
    try {
      const result = await squareOff(squareOffTarget.userId);
      setSquareOffResult(result);
      showSuccess(`Squared off ${result.positionsSold} position(s) for proceeds ${currency.format(result.proceeds)}`);
      fetchData();
      // Keep the modal open so the result stays visible; close via Done.
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Failed to square off');
      setSquareOffTarget(null);
    } finally {
      setSquareOffBusy(false);
    }
  };

  if (loading) return <Spinner />;
  if (error) return <ErrorState message={error} onRetry={fetchData} />;

  return (
    <div className="admin-page">
      <h1>Admin</h1>

      {status && (
        <div className="stat-grid">
          <StatCard label="Pending Signals" value={String(status.pendingSignalCount)} />
          {status.users.map((u) => (
            <StatCard
              key={u.userId}
              label={`${u.name} Return`}
              value={`${currency.format(u.totalReturn)} (${(u.totalReturnPercent * 100).toFixed(2)}%)`}
              accent={u.totalReturn >= 0 ? 'positive' : 'negative'}
            />
          ))}
        </div>
      )}

      <section className="card admin-section">
        <h2>Signal Generation</h2>
        <div className="admin-row">
          <div>
            <p className="modal-hint">
              Triggers the Python pipeline to generate today's signals.
              {lastRun && (
                <span data-testid="last-run"> Last run: {lastRun}</span>
              )}
            </p>
            <p className="modal-hint">Runs as a background job; the page polls until it completes.</p>
          </div>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleRunSignals}
            disabled={running}
            data-testid="run-signals"
          >
            {running ? 'Running...' : 'Run Signal Generation'}
          </button>
        </div>
      </section>

      <section className="card admin-section">
        <h2>Portfolio Limits Configuration</h2>
        {agents.map((u) => {
          const config = configs[u.id];
          if (!config) return null;
          return (
            <div key={u.id} className="admin-config-block" data-testid={`config-${u.id}`}>
              <h3>{u.name} (user_id={u.id})</h3>
              <label className="toggle-row">
                <span>
                  Auto-execute
                  <small>Approve and execute signals automatically</small>
                </span>
                <input
                  type="checkbox"
                  checked={config.autoExecute}
                  onChange={(e) => setConfigField(u.id, { autoExecute: e.target.checked })}
                  data-testid={`auto-execute-${u.id}`}
                />
              </label>
              <div className="config-fields">
                <label className="field">
                  Negative limit (credit line)
                  <input
                    type="number"
                    min="0"
                    step="1000"
                    value={config.negativeLimit}
                    onChange={(e) => setConfigField(u.id, { negativeLimit: Number(e.target.value) })}
                    data-testid={`negative-limit-${u.id}`}
                  />
                </label>
                <label className="field">
                  Daily interest rate
                  <input
                    type="number"
                    min="0"
                    step="0.0001"
                    value={config.interestRate}
                    onChange={(e) => setConfigField(u.id, { interestRate: Number(e.target.value) })}
                    data-testid={`interest-rate-${u.id}`}
                  />
                </label>
              </div>
              <div className="admin-actions">
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() => saveUserConfig(u.id)}
                  disabled={saving[u.id]}
                  data-testid={`save-config-${u.id}`}
                >
                  {saving[u.id] ? 'Saving...' : 'Save'}
                </button>
                <button
                  type="button"
                  className="btn btn-sell"
                  onClick={() => confirmSquareOff(config)}
                  data-testid={`square-off-${u.id}`}
                >
                  Square Off {u.name}
                </button>
              </div>
            </div>
          );
        })}
      </section>

      <section className="card admin-section">
        <h2>Global Settings</h2>
        <div className="config-fields">
          <label className="field">
            Min confidence threshold
            <input
              type="number"
              min="0"
              max="1"
              step="0.01"
              value={minConfidence}
              onChange={(e) => setMinConfidence(e.target.value)}
              data-testid="global-min-confidence"
            />
          </label>
          <label className="field">
            Default watchlist (comma-separated)
            <input
              type="text"
              value={watchlist}
              onChange={(e) => setWatchlist(e.target.value)}
              placeholder="RELIANCE, TCS, INFY"
              data-testid="global-watchlist"
            />
          </label>
        </div>
        <div className="admin-actions">
          <button
            type="button"
            className="btn btn-primary"
            onClick={saveGlobal}
            disabled={savingGlobal}
            data-testid="save-global"
          >
            {savingGlobal ? 'Saving...' : 'Save Global Settings'}
          </button>
        </div>
      </section>

      {squareOffTarget && (
        <div className="modal-overlay" data-testid="square-off-confirm">
          <div className="modal">
            <h2>
              {squareOffResult ? 'Square Off Complete' : `Square Off ${agentLabel(squareOffTarget.userId)}?`}
            </h2>
            {squareOffResult ? (
              <p className="modal-hint" data-testid="square-off-result">
                Sold {squareOffResult.positionsSold} position(s) for proceeds{' '}
                {currency.format(squareOffResult.proceeds)}. Remaining cash{' '}
                {currency.format(squareOffResult.remainingCash)}.
              </p>
            ) : (
              <p className="modal-hint">
                Sells all holdings at market price and repays any credit balance
                (negative limit: {currency.format(squareOffTarget.negativeLimit)}).
              </p>
            )}
            <div className="modal-actions">
              <button
                type="button"
                className="btn"
                disabled={squareOffBusy}
                onClick={() => setSquareOffTarget(null)}
                data-testid="square-off-cancel"
              >
                {squareOffResult ? 'Done' : 'Cancel'}
              </button>
              {!squareOffResult && (
                <button
                  type="button"
                  className="btn btn-primary"
                  disabled={squareOffBusy}
                  onClick={runSquareOff}
                  data-testid="square-off-confirm-button"
                >
                  {squareOffBusy ? 'Working...' : 'Confirm Square Off'}
                </button>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default Admin;
