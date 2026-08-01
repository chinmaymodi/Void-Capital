// Shared presentational components (SRP: these never call APIs).
// Every page follows the same data-state pattern: loading -> error -> empty -> content.

interface StatCardProps {
  label: string;
  value: string;
  accent?: 'default' | 'positive' | 'negative';
}

export function StatCard({ label, value, accent = 'default' }: StatCardProps) {
  return (
    <div className={`stat-card accent-${accent}`}>
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
    </div>
  );
}

export function Spinner() {
  return (
    <div className="spinner-wrap" data-testid="spinner" role="status" aria-label="Loading">
      <div className="spinner"></div>
    </div>
  );
}

interface ErrorStateProps {
  message: string;
  onRetry?: () => void;
}

export function ErrorState({ message, onRetry }: ErrorStateProps) {
  return (
    <div className="state-box state-error" role="alert">
      <p>{message}</p>
      {onRetry && (
        <button type="button" className="btn" onClick={onRetry}>
          Retry
        </button>
      )}
    </div>
  );
}

export function EmptyState({ message }: { message: string }) {
  return (
    <div className="state-box state-empty">
      <p>{message}</p>
    </div>
  );
}
