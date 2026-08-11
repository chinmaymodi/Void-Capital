// Settings page: watchlist chips, auto-execute toggle, budget display.
// Fetches settings on mount, saves via PUT, shows toasts on result.

import { useCallback, useEffect, useState } from 'react';
import { useToast } from '../components/useToast';
import { ErrorState, Spinner } from '../components/ui';
import { useUser } from '../context/useUser';
import { getSettings, updateSettings } from '../services/api';
import type { Settings } from '../types';

export function SettingsPage() {
  const [settings, setSettings] = useState<Settings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [newSymbol, setNewSymbol] = useState('');
  const { showError, showSuccess } = useToast();
  const { currentUserId } = useUser();

  const fetchData = useCallback(() => {
    setLoading(true);
    setError(null);
    getSettings(currentUserId)
      .then(setSettings)
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load settings'))
      .finally(() => setLoading(false));
  }, [currentUserId]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const addSymbol = () => {
    const symbol = newSymbol.trim().toUpperCase();
    if (!symbol) return;
    if (settings && settings.watchlist.includes(symbol)) {
      setNewSymbol('');
      return;
    }
    setSettings((s) => (s ? { ...s, watchlist: [...s.watchlist, symbol] } : s));
    setNewSymbol('');
  };

  const removeSymbol = (symbol: string) => {
    setSettings((s) => (s ? { ...s, watchlist: s.watchlist.filter((x) => x !== symbol) } : s));
  };

  const save = async () => {
    if (!settings) return;
    setSaving(true);
    try {
      const updated = await updateSettings(settings, currentUserId);
      setSettings(updated);
      showSuccess('Settings saved');
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Failed to save settings');
    } finally {
      setSaving(false);
    }
  };

  if (loading) return <Spinner />;
  if (error) return <ErrorState message={error} onRetry={fetchData} />;
  if (!settings) return <ErrorState message="No settings found" onRetry={fetchData} />;

  return (
    <div className="settings-page">
      <h1>Settings</h1>

      <section className="card">
        <h2>Watchlist</h2>
        <div className="chip-list" data-testid="watchlist">
          {settings.watchlist.map((s) => (
            <span key={s} className="chip">
              {s}
              <button type="button" className="chip-x" aria-label={`Remove ${s}`} onClick={() => removeSymbol(s)}>
                x
              </button>
            </span>
          ))}
        </div>
        <div className="chip-add">
          <input
            type="text"
            value={newSymbol}
            onChange={(e) => setNewSymbol(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                addSymbol();
              }
            }}
            placeholder="Add symbol e.g. TATAMOTORS"
            data-testid="symbol-input"
          />
          <button type="button" className="btn" onClick={addSymbol}>
            Add
          </button>
        </div>
      </section>

      <section className="card">
        <h2>Execution</h2>
        <label className="toggle-row">
          <span>
            Auto-execute signals
            <small>Approve signals automatically when confidence is met</small>
          </span>
          <input
            type="checkbox"
            checked={settings.autoExecute}
            onChange={(e) => setSettings({ ...settings, autoExecute: e.target.checked })}
            data-testid="auto-execute"
          />
        </label>
        <label className="field">
          Min confidence
          <input
            type="number"
            min="0"
            max="1"
            step="0.01"
            value={settings.minConfidence}
            onChange={(e) => setSettings({ ...settings, minConfidence: Number(e.target.value) })}
            data-testid="min-confidence"
          />
        </label>
        <label className="field">
          Negative limit (margin credit line)
          <input
            type="number"
            min="0"
            step="1000"
            value={settings.negativeLimit}
            onChange={(e) => setSettings({ ...settings, negativeLimit: Number(e.target.value) })}
            data-testid="negative-limit"
          />
        </label>
      </section>

      <div className="settings-actions">
        <button type="button" className="btn btn-primary" onClick={save} disabled={saving} data-testid="save-settings">
          {saving ? 'Saving...' : 'Save'}
        </button>
      </div>
    </div>
  );
}

export default SettingsPage;
