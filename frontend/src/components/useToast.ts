// useToast hook. Split from Toast.tsx so that file only exports components
// (satisfies the react fast-refresh rule).

import { useContext } from 'react';
import { ToastContext } from './ToastContext';

export interface ToastContextValue {
  showError: (message: string) => void;
  showSuccess: (message: string) => void;
}

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) {
    throw new Error('useToast must be used within ToastProvider');
  }
  return ctx;
}
