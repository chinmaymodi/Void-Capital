// useUser hook. Split from UserProvider.tsx so that file only exports the
// provider component (satisfies the react fast-refresh rule).

import { useContext } from 'react';
import { UserContext, type UserContextValue } from './UserContext';

export function useUser(): UserContextValue {
  const ctx = useContext(UserContext);
  if (!ctx) {
    throw new Error('useUser must be used within a UserProvider');
  }
  return ctx;
}