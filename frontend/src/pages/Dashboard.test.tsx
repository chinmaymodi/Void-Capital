// Dashboard page tests. The service layer is mocked so tests never touch the
// network; each test exercises a data state (loading -> content / error).

import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Dashboard from '../pages/Dashboard';
import { UserProvider } from '../context/UserProvider';
import { getPortfolio, getPortfolioHistory, getUsers } from '../services/api';

vi.mock('../services/api');

const mockedGetPortfolio = vi.mocked(getPortfolio);
const mockedGetHistory = vi.mocked(getPortfolioHistory);
const mockedGetUsers = vi.mocked(getUsers);

function renderPage() {
  return render(
    <UserProvider>
      <Dashboard />
    </UserProvider>,
  );
}

describe('Dashboard', () => {
  beforeEach(() => {
    mockedGetPortfolio.mockReset();
    mockedGetHistory.mockReset();
    mockedGetUsers.mockResolvedValue([{ id: 1, name: 'Trader One' }]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('shows a loading spinner on mount', () => {
    mockedGetPortfolio.mockImplementation(() => new Promise(() => {}));
    mockedGetHistory.mockImplementation(() => new Promise(() => {}));

    renderPage();
    expect(screen.getByTestId('spinner')).toBeInTheDocument();
  });

  it('renders stats cards after data loads', async () => {
    mockedGetPortfolio.mockResolvedValue({
      cash: 60400,
      holdingsValue: 45750,
      totalValue: 106150,
    });
    mockedGetHistory.mockResolvedValue([]);

    renderPage();

    expect(await screen.findByText('Cash')).toBeInTheDocument();
    expect(screen.getByText('Holdings')).toBeInTheDocument();
    expect(screen.getByText('Total')).toBeInTheDocument();
  });

  it('renders an empty state when no history snapshots exist', async () => {
    mockedGetPortfolio.mockResolvedValue({
      cash: 60400,
      holdingsValue: 45750,
      totalValue: 106150,
    });
    mockedGetHistory.mockResolvedValue([]);

    renderPage();

    expect(
      await screen.findByText(/No portfolio history recorded yet/i),
    ).toBeInTheDocument();
  });

  it('shows an error state and retries when the API fails', async () => {
    mockedGetPortfolio.mockRejectedValueOnce(new Error('Network down'));
    mockedGetHistory.mockRejectedValueOnce(new Error('Network down'));
    mockedGetPortfolio.mockResolvedValue({
      cash: 60400,
      holdingsValue: 45750,
      totalValue: 106150,
    });
    mockedGetHistory.mockResolvedValue([]);

    renderPage();

    expect(await screen.findByText('Network down')).toBeInTheDocument();

    const retry = screen.getByRole('button', { name: 'Retry' });
    retry.click();

    await waitFor(() => {
      expect(mockedGetPortfolio).toHaveBeenCalledTimes(2);
    });
    expect(await screen.findByText('Cash')).toBeInTheDocument();
  });
});
