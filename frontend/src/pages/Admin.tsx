// Admin page (D7.2): signal generation control, per-system-user portfolio
// limits config, global settings, and manual square-off with confirmation.
// run-signals is stubbed on the backend (D9) so the button still exercises
// the API and shows the returned message.

import { useCallback, useEffect, useState } from 'react';
import { useToast } from '../components/useToast';
import { ErrorState, Spinner, StatCard } from '../components/ui';
import {
  getAdminSettings,
  getAdminStatus,
  runSignalGeneration,
  squareOff,
  updateAdminSettings,
  updateGlobalSettings,
} from '../services/api';
import type { AdminStatus, Settings, SquareOffResult } from '../types';

const CONFIG_USERS = [
  { userId: 2, label: 'System Portfolio' },
  { userId: 3, label: 'System-Reckless' },
];

const currency = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 0,
});

export function Admin() {
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
      ...CONFIG_USERS.map((u) => getAdminSettings(u.userId)),
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
  }, []);

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
      const message = await runSignalGeneration();
      setLastRun(new Date().toLocaleTimeString());
      showSuccess(message);
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
      showSuccess(`Saved ${CONFIG_USERS.find((u) => u.userId === userId)?.label} config`);
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Failed to save config');
    } finally {
      setSaving((s) => ({ ...s, [userId]: false }));
    }
  };

  const saveGlobal = async () => {
    setSavingGlobal(true);
    try {
      const symbols = watchlist
        .split(',')
        .map((s) => s.trim().toUpperCase())
        .filter(Boolean);
      const updated = await updateGlobalSettings(Number(minConfidence), symbols);
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
            <p className="modal-hint">Currently stubbed on the backend (wired in D9).</p>
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
        {CONFIG_USERS.map((u) => {
          const config = configs[u.userId];
          if (!config) return null;
          return (
            <div key={u.userId} className="admin-config-block" data-testid={`config-${u.userId}`}>
              <h3>{u.label} (user_id={u.userId})</h3>
              <label className="toggle-row">
                <span>
                  Auto-execute
                  <small>Approve and execute signals automatically</small>
                </span>
                <input
                  type="checkbox"
                  checked={config.autoExecute}
                  onChange={(e) => setConfigField(u.userId, { autoExecute: e.target.checked })}
                  data-testid={`auto-execute-${u.userId}`}
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
                    onChange={(e) => setConfigField(u.userId, { negativeLimit: Number(e.target.value) })}
                    data-testid={`negative-limit-${u.userId}`}
                  />
                </label>
                <label className="field">
                  Daily interest rate
                  <input
                    type="number"
                    min="0"
                    step="0.0001"
                    value={config.interestRate}
                    onChange={(e) => setConfigField(u.userId, { interestRate: Number(e.target.value) })}
                    data-testid={`interest-rate-${u.userId}`}
                  />
                </label>
              </div>
              <div className="admin-actions">
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() => saveUserConfig(u.userId)}
                  disabled={saving[u.userId]}
                  data-testid={`save-config-${u.userId}`}
                >
                  {saving[u.userId] ? 'Saving...' : 'Save'}
                </button>
                {u.userId === 3 && (
                  <button
                    type="button"
                    className="btn btn-sell"
                    onClick={() => confirmSquareOff(config)}
                    data-testid="square-off-reckless"
                  >
                    Square Off Reckless
                  </button>
                )}
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
              {squareOffResult ? 'Square Off Complete' : `Square Off ${CONFIG_USERS.find((u) => u.userId === squareOffTarget.userId)?.label}?`}
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
