// Shared context object for toasts. The context value type lives here so
// both Toast.tsx (provider) and useToast.ts (hook) can import it without
// mixing component and non-component exports in one file.

import { createContext } from 'react';
import type { ToastContextValue } from './useToast';

export const ToastContext = createContext<ToastContextValue | null>(null);
