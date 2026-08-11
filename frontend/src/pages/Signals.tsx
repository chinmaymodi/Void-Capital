// Signals page: today's model predictions as cards with approve/reject,
// batch actions with confirmation, and 30s auto-refresh (paused while a
// confirmation dialog is open). Container component owning fetch + selection.

import { useCallback, useEffect, useRef, useState } from 'react';
import { useToast } from '../components/useToast';
import { EmptyState, ErrorState, Spinner } from '../components/ui';
import { useUser } from '../context/useUser';
import {
  approveSignal,
  batchApproveSignals,
  batchRejectSignals,
  getTodaySignals,
  rejectSignal,
  runSignalGenerationAndWait,
} from '../services/api';
import type { Signal, SignalBatchResult, SignalStatus } from '../types';

const REFRESH_MS = 30_000;

const percent = new Intl.NumberFormat('en-IN', { style: 'percent', maximumFractionDigits: 0 });

const STATUS_LABEL: Record<SignalStatus, string> = {
  PENDING: 'Pending',
  APPROVED: 'Approved',
  REJECTED: 'Rejected',
  EXECUTED: 'Executed',
  FAILED: 'Failed',
};

export function Signals() {
  const [signals, setSignals] = useState<Signal[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [confirmAction, setConfirmAction] = useState<'approve' | 'reject' | null>(null);
  const [busyIds, setBusyIds] = useState<Set<number>>(new Set());
  const [batchBusy, setBatchBusy] = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const { showError, showSuccess } = useToast();
  const [running, setRunning] = useState(false);
  const pausedRef = useRef(false);
  const { currentUserId } = useUser();

  const handleRunSignals = async () => {
    setRunning(true);
    try {
      const job = await runSignalGenerationAndWait();
      if (job.status === 'SUCCEEDED') {
        showSuccess(job.message ?? 'Signal generation complete');
      } else {
        showError(job.message ?? 'Signal generation failed');
      }
      fetchData();
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Failed to run signal generation');
    } finally {
      setRunning(false);
    }
  };

  const fetchData = useCallback(() => {
    setLoading(true);
    setError(null);
    getTodaySignals(currentUserId)
      .then((items) => {
        setSignals(items);
        setLastUpdated(new Date());
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load signals'))
      .finally(() => setLoading(false));
  }, [currentUserId]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  // Auto-refresh every 30s; paused while a confirmation dialog is open.
  useEffect(() => {
    const timer = setInterval(() => {
      if (!pausedRef.current) fetchData();
    }, REFRESH_MS);
    return () => clearInterval(timer);
  }, [fetchData]);

  // Keep the paused flag in sync with the open dialog without re-arming the timer.
  useEffect(() => {
    pausedRef.current = confirmAction !== null;
  }, [confirmAction]);

  const markResult = (result: SignalBatchResult) => {
    setSignals((current) =>
      current.map((s) => (s.id === result.id ? { ...s, status: result.success ? 'APPROVED' : 'FAILED' } : s)),
    );
    return result.success;
  };

  const handleApprove = async (signal: Signal) => {
    setBusyIds((s) => new Set(s).add(signal.id));
    try {
      const updated = await approveSignal(signal.id);
      setSignals((current) => current.map((s) => (s.id === updated.id ? updated : s)));
      showSuccess(`${signal.symbol} approved`);
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Failed to approve signal');
    } finally {
      setBusyIds((s) => {
        const next = new Set(s);
        next.delete(signal.id);
        return next;
      });
    }
  };

  const handleReject = async (signal: Signal) => {
    setBusyIds((s) => new Set(s).add(signal.id));
    try {
      const updated = await rejectSignal(signal.id);
      setSignals((current) => current.map((s) => (s.id === updated.id ? updated : s)));
      showSuccess(`${signal.symbol} rejected`);
    } catch (err) {
      showError(err instanceof Error ? err.message : 'Failed to reject signal');
    } finally {
      setBusyIds((s) => {
        const next = new Set(s);
        next.delete(signal.id);
        return next;
      });
    }
  };

  const toggleSelect = (id: number) => {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleSelectAll = () => {
    setSelected((current) => (current.size === signals.length ? new Set() : new Set(signals.map((s) => s.id))));
  };

  const confirmBatch = (action: 'approve' | 'reject') => {
    if (selected.size === 0) return;
    setConfirmAction(action);
  };

  const runBatch = async () => {
    if (!confirmAction) return;
    setBatchBusy(true);
    const ids = [...selected];
    try {
      const results =
        confirmAction === 'approve' ? await batchApproveSignals(ids) : await batchRejectSignals(ids);
      results.forEach(markResult);
      const okCount = results.filter((r) => r.success).length;
      showSuccess(
        `${okCount} of ${results.length} signals ${confirmAction === 'approve' ? 'approved' : 'rejected'}`,
      );
      setSelected(new Set());
    } catch (err) {
      showError(err instanceof Error ? err.message : `Failed to ${confirmAction} selected signals`);
    } finally {
      setBatchBusy(false);
      setConfirmAction(null);
    }
  };

  const selectedCount = selected.size;
  const allSelected = signals.length > 0 && selectedCount === signals.length;

  if (loading && signals.length === 0) return <Spinner />;
  if (error && signals.length === 0) return <ErrorState message={error} onRetry={fetchData} />;

  return (
    <div className="signals-page">
      <div className="page-header">
        <h1>Today's Signals</h1>
        <div className="signals-header-actions">
          {lastUpdated && (
            <span className="signals-updated" data-testid="last-updated">
              Updated {lastUpdated.toLocaleTimeString()}
            </span>
          )}
          <button type="button" className="btn" onClick={fetchData} data-testid="refresh-signals">
            Refresh
          </button>
          <button type="button" className="btn btn-primary" onClick={handleRunSignals} disabled={running} data-testid="run-signals">
            {running ? 'Running...' : 'Run Signal Generation'}
          </button>
        </div>
      </div>

      <div className="batch-bar">
        <label className="batch-select-all">
          <input type="checkbox" checked={allSelected} onChange={toggleSelectAll} data-testid="select-all" />
          Select all
        </label>
        <button
          type="button"
          className="btn btn-buy"
          disabled={selectedCount === 0 || batchBusy}
          onClick={() => confirmBatch('approve')}
          data-testid="batch-approve"
        >
          Approve Selected ({selectedCount})
        </button>
        <button
          type="button"
          className="btn btn-sell"
          disabled={selectedCount === 0 || batchBusy}
          onClick={() => confirmBatch('reject')}
          data-testid="batch-reject"
        >
          Reject Selected ({selectedCount})
        </button>
      </div>

      {signals.length === 0 ? (
        <EmptyState message="No pending signals today." />
      ) : (
        <div className="signal-grid">
          {signals.map((s) => (
            <div key={s.id} className="signal-card" data-testid={`signal-${s.id}`}>
              <div className="signal-card-header">
                <label className="signal-check">
                  <input
                    type="checkbox"
                    checked={selected.has(s.id)}
                    onChange={() => toggleSelect(s.id)}
                    data-testid={`select-${s.id}`}
                  />
                </label>
                <span className="symbol-cell">{s.symbol}</span>
                <span className={`badge badge-${s.action.toLowerCase()}`}>{s.action}</span>
                <span className={`badge badge-status badge-${s.status.toLowerCase()}`}>
                  {STATUS_LABEL[s.status]}
                </span>
              </div>

              <div className="signal-confidence">
                <div className="signal-confidence-label">
                  <span>Confidence</span>
                  <span data-testid={`confidence-${s.id}`}>{percent.format(s.confidence)}</span>
                </div>
                <div className="confidence-bar" data-testid={`confidence-bar-${s.id}`}>
                  <div className="confidence-fill" style={{ width: `${Math.min(100, s.confidence * 100)}%` }} />
                </div>
              </div>

              {s.reason && <p className="signal-reason">{s.reason}</p>}

              <div className="signal-meta">
                <span className="chip">{s.modelName}</span>
                {s.suggestedQuantity != null && <span>Qty {s.suggestedQuantity}</span>}
                {s.entryPrice != null && <span>Entry {s.entryPrice.toLocaleString('en-IN')}</span>}
                {s.targetPrice != null && <span>Target {s.targetPrice.toLocaleString('en-IN')}</span>}
                {s.stopLoss != null && <span>Stop {s.stopLoss.toLocaleString('en-IN')}</span>}
              </div>

              {s.status === 'FAILED' && s.failureReason && (
                <p className="signal-failure" data-testid={`failure-${s.id}`}>
                  {s.failureReason}
                </p>
              )}

              <div className="signal-actions">
                <button
                  type="button"
                  className="btn btn-buy"
                  disabled={busyIds.has(s.id) || s.status !== 'PENDING'}
                  onClick={() => handleApprove(s)}
                  data-testid={`approve-${s.id}`}
                >
                  Approve
                </button>
                <button
                  type="button"
                  className="btn btn-sell"
                  disabled={busyIds.has(s.id) || s.status !== 'PENDING'}
                  onClick={() => handleReject(s)}
                  data-testid={`reject-${s.id}`}
                >
                  Reject
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {confirmAction && (
        <div className="modal-overlay" data-testid="batch-confirm">
          <div className="modal">
            <h2>
              {confirmAction === 'approve' ? 'Approve' : 'Reject'} {selectedCount} signal
              {selectedCount === 1 ? '' : 's'}?
            </h2>
            <p className="modal-hint">
              {confirmAction === 'approve'
                ? 'Approved signals move to execution (auto-execute runs immediately).'
                : 'Rejected signals are marked REJECTED and will not execute.'}
            </p>
            <div className="modal-actions">
              <button
                type="button"
                className="btn"
                disabled={batchBusy}
                onClick={() => setConfirmAction(null)}
                data-testid="batch-cancel"
              >
                Cancel
              </button>
              <button
                type="button"
                className="btn btn-primary"
                disabled={batchBusy}
                onClick={runBatch}
                data-testid="batch-confirm-button"
              >
                {batchBusy ? 'Working...' : 'Confirm'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default Signals;
