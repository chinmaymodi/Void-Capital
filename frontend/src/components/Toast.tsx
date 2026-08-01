// Toast provider: lightweight auto-dismissing notifications for API errors.
// Implemented as a tiny context so any component can call useToast() without
// a global state library (matches the "no global state management" rule).

import { useCallback, useRef, useState } from 'react';
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

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((t) => t.id !== id));
  }, []);

  const show = useCallback(
    (message: string, kind: Toast['kind']) => {
      const id = nextId.current++;
      setToasts((current) => [...current, { id, message, kind }]);
      setTimeout(() => dismiss(id), 4000);
    },
    [dismiss],
  );

  const value: ToastContextValue = {
    showError: (message) => show(message, 'error'),
    showSuccess: (message) => show(message, 'success'),
  };

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
