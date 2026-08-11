// App root: routing + lazy loading + the Layout shell.

import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import App from './App';

// Lazy pages are mocked so tests never load the real page chunks (which
// would pull in the API layer). Each mock renders a marker with its name.
vi.mock('./pages/Dashboard', () => ({ default: () => <div>Dashboard page</div> }));
vi.mock('./pages/Holdings', () => ({ default: () => <div>Holdings page</div> }));
vi.mock('./pages/Trades', () => ({ default: () => <div>Trades page</div> }));
vi.mock('./pages/Settings', () => ({ default: () => <div>Settings page</div> }));
vi.mock('./pages/Signals', () => ({ default: () => <div>Signals page</div> }));
vi.mock('./pages/SystemPortfolio', () => ({ default: () => <div>System page</div> }));
vi.mock('./pages/Compare', () => ({ default: () => <div>Compare page</div> }));
vi.mock('./pages/SignalPerformance', () => ({ default: () => <div>Performance page</div> }));
vi.mock('./pages/Admin', () => ({ default: () => <div>Admin page</div> }));

// The Layout fetches the portfolio total on mount; stub it out. UserProvider
// fetches the user list; stub that too.
vi.mock('./services/api', () => ({
  getPortfolio: vi.fn().mockResolvedValue({ cash: 0, holdingsValue: 0, totalValue: 0 }),
  getUsers: vi.fn().mockResolvedValue([{ id: 1, name: 'Trader One' }]),
}));

describe('App', () => {
  it('renders the dashboard at the index route', async () => {
    window.history.pushState({}, '', '/');
    render(<App />);
    expect(await screen.findByText('Dashboard page')).toBeInTheDocument();
  });

  it('renders the holdings route', async () => {
    window.history.pushState({}, '', '/holdings');
    render(<App />);
    expect(await screen.findByText('Holdings page')).toBeInTheDocument();
  });

  it('renders the settings route', async () => {
    window.history.pushState({}, '', '/settings');
    render(<App />);
    expect(await screen.findByText('Settings page')).toBeInTheDocument();
  });

  it('renders the not-found placeholder for unknown routes', async () => {
    window.history.pushState({}, '', '/does-not-exist');
    render(<App />);
    expect(await screen.findByText('Not Found')).toBeInTheDocument();
  });

  it('renders the sidebar brand in the layout shell', async () => {
    window.history.pushState({}, '', '/');
    render(<App />);
    expect(await screen.findByText('Void Capital')).toBeInTheDocument();
  });
});