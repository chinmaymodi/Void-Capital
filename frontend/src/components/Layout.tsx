// App shell: sidebar navigation + header with user picker, portfolio total
// and <Outlet/>. The header total is fetched on mount and whenever the
// selected user changes (per D4 spec) so it reflects the latest portfolio
// state across pages.

import { useEffect, useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { useUser } from '../context/useUser';
import { getPortfolio } from '../services/api';
import { useToast } from './useToast';

const NAV_ITEMS = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/holdings', label: 'Holdings' },
  { to: '/trades', label: 'Trades' },
  { to: '/signals', label: 'Signals' },
  { to: '/system', label: 'System Portfolio' },
  { to: '/compare', label: 'Compare' },
  { to: '/performance', label: 'Performance' },
  { to: '/admin', label: 'Admin' },
  { to: '/settings', label: 'Settings' },
];

const formatter = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 0,
});

export function Layout() {
  const [total, setTotal] = useState<number | null>(null);
  const { users, currentUserId, currentUser, setCurrentUserId } = useUser();
  const { showError } = useToast();

  useEffect(() => {
    getPortfolio(currentUserId)
      .then((state) => setTotal(state.totalValue))
      .catch((err) => {
        showError(`Failed to load portfolio total: ${err instanceof Error ? err.message : String(err)}`);
        setTotal(null);
      });
  }, [currentUserId, showError]);

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar-brand">Void Capital</div>
        <nav className="sidebar-nav">
          {NAV_ITEMS.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
      </aside>
      <div className="app-main">
        <header className="topbar">
          {users.length > 0 ? (
            <select
              className="topbar-user"
              value={currentUserId}
              onChange={(e) => setCurrentUserId(Number(e.target.value))}
              data-testid="user-picker"
              aria-label="Select user"
            >
              {users.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.name}
                </option>
              ))}
            </select>
          ) : (
            <span className="topbar-title">{currentUser?.name ?? 'Trader One'}</span>
          )}
          <span className="topbar-total" data-testid="header-total">
            {total === null ? '--' : formatter.format(total)}
          </span>
        </header>
        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
