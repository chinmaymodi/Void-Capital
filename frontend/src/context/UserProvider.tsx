// UserProvider: loads the user list once on mount and defaults to user 1
// (Trader One). Pages read currentUserId via useUser (see useUser.ts) and
// pass it to the API so switching users refetches data.

import { useEffect, useMemo, useState } from 'react';
import { getUsers } from '../services/api';
import { UserContext } from './UserContext';
import type { User } from '../types';

export function UserProvider({ children }: { children: React.ReactNode }) {
  const [users, setUsers] = useState<User[]>([]);
  const [currentUserId, setCurrentUserId] = useState(1);

  useEffect(() => {
    getUsers()
      .then(setUsers)
      .catch(() => setUsers([])); // API down: fall back to the default user
  }, []);

  const currentUser = useMemo(
    () => users.find((u) => u.id === currentUserId) ?? null,
    [users, currentUserId],
  );

  const value = useMemo(
    () => ({ users, currentUserId, currentUser, setCurrentUserId }),
    [users, currentUserId, currentUser],
  );

  return <UserContext.Provider value={value}>{children}</UserContext.Provider>;
}