// Toast provider: lightweight auto-dismissing notifications for API errors.
// Implemented as a tiny context so any component can call useToast() without
// a global state library (matches the "no global state management" rule).

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { ToastContext } from './ToastContext';
import type { ToastContextValue } from './useToast';

interface Toast {
  id: number;
  message: string;
  kind: 'error' | 'success';
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);
  const timers = useRef<Set<ReturnType<typeof setTimeout>>>(new Set());

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((t) => t.id !== id));
  }, []);

  const show = useCallback(
    (message: string, kind: Toast['kind']) => {
      const id = nextId.current++;
      setToasts((current) => [...current, { id, message, kind }]);
      const timer = setTimeout(() => {
        timers.current.delete(timer);
        dismiss(id);
      }, 4000);
      timers.current.add(timer);
    },
    [dismiss],
  );

  // Clear pending timers on unmount so a toast never fires after the
  // provider is gone.
  useEffect(() => {
    const active = timers.current;
    return () => {
      active.forEach(clearTimeout);
      active.clear();
    };
  }, []);

  // Stable identities: the context value must not change when a toast
  // appears/dismisses, or consumers with showError in an effect deps array
  // (Layout's portfolio-total refetch) would re-run on every toast.
  const showError = useCallback((message: string) => show(message, 'error'), [show]);
  const showSuccess = useCallback((message: string) => show(message, 'success'), [show]);
  const value = useMemo<ToastContextValue>(
    () => ({ showError, showSuccess }),
    [showError, showSuccess],
  );

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toast-stack" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className={`toast toast-${t.kind}`} role="alert">
            {t.message}
            <button
              type="button"
              className="toast-close"
              aria-label="Dismiss"
              onClick={() => dismiss(t.id)}
            >
              x
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
