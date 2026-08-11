// UserContext: which trader the dashboard-style pages (Dashboard, Holdings,
// Trades, Signals, Settings) are viewing. Split from UserProvider.tsx so
// that file only exports the provider component (fast-refresh rule).

import { createContext } from 'react';
import type { User } from '../types';

export interface UserContextValue {
  users: User[];
  currentUserId: number;
  currentUser: User | null;
  setCurrentUserId: (id: number) => void;
}

export const UserContext = createContext<UserContextValue | null>(null);