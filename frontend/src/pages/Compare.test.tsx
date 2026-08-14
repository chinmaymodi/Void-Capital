// Compare page tests: per-user columns, gap table, error/retry. Columns
// derive from the user roster (via UserProvider -> getUsers), so the tests
// mock a 7-user roster and per-user history endpoints.

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { UserProvider } from '../context/UserProvider';
import Compare from '../pages/Compare';
import { getComparison, getPortfolioHistory, getUsers } from '../services/api';
import type { PortfolioComparison } from '../types';

vi.mock('../services/api');

const mockedGetComparison = vi.mocked(getComparison);
const mockedGetPortfolioHistory = vi.mocked(getPortfolioHistory);
const mockedGetUsers = vi.mocked(getUsers);

const sampleUsers = [
  { id: 1, name: 'Trader One' },
  { id: 2, name: 'System Portfolio' },
  { id: 3, name: 'System-Reckless' },
  { id: 4, name: 'Options-Careful' },
  { id: 5, name: 'Options-Reckless' },
  { id: 6, name: 'Intraday-Careful' },
  { id: 7, name: 'Intraday-Reckless' },
];

const sampleComparison: PortfolioComparison = {
  portfolios: [
    { userId: 1, name: 'Trader One', cash: 90000, holdingsValue: 20000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
    { userId: 2, name: 'System Portfolio', cash: 50000, holdingsValue: 60000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
    { userId: 3, name: 'System-Reckless', cash: -20000, holdingsValue: 130000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
    { userId: 4, name: 'Options-Careful', cash: 50000, holdingsValue: 60000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
    { userId: 5, name: 'Options-Reckless', cash: 50000, holdingsValue: 60000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
    { userId: 6, name: 'Intraday-Careful', cash: 50000, holdingsValue: 60000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
    { userId: 7, name: 'Intraday-Reckless', cash: 50000, holdingsValue: 60000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
  ],
  gaps: [
    { leader: 'Trader One', trailer: 'System Portfolio', gapRupees: 0, gapPercent: 0 },
    { leader: 'System-Reckless', trailer: 'Trader One', gapRupees: 15000, gapPercent: 0.15 },
  ],
};

function mockDefaultResponses() {
  mockedGetUsers.mockResolvedValue(sampleUsers);
  mockedGetComparison.mockResolvedValue(sampleComparison);
  mockedGetPortfolioHistory.mockResolvedValue([]);
}

function renderPage() {
  return render(
    <UserProvider>
      <Compare />
    </UserProvider>,
  );
}

describe('Compare', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockDefaultResponses();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders one column per user, current user labeled Your Portfolio', async () => {
    renderPage();

    expect(await screen.findByText('Your Portfolio')).toBeInTheDocument();
    expect(screen.getAllByText('System Portfolio').length).toBeGreaterThan(0);
    expect(screen.getAllByText('System-Reckless').length).toBeGreaterThan(0);
    expect(screen.getByTestId('column-user1')).toBeInTheDocument();
    expect(screen.getByTestId('column-user2')).toBeInTheDocument();
    expect(screen.getByTestId('column-user3')).toBeInTheDocument();
    expect(screen.getByTestId('column-user4')).toBeInTheDocument();
    expect(screen.getByTestId('column-user5')).toBeInTheDocument();
    expect(screen.getByTestId('column-user6')).toBeInTheDocument();
    expect(screen.getByTestId('column-user7')).toBeInTheDocument();
  });

  it('shows cash, holdings, total value and return per column', async () => {
    renderPage();

    // Each column renders a Cash/Holdings/Total Value stat block.
    expect(await screen.findAllByText('Cash').then((nodes) => nodes.length)).toBe(7);
    expect(screen.getAllByText('Total Value').length).toBe(7);
    expect(screen.getAllByText('₹1,10,000').length).toBeGreaterThanOrEqual(1);
  });

  it('renders CAGR, Sharpe and max drawdown overlay when history exists', async () => {
    // Two-day series: 100 -> 110 (10% gain). CAGR annualizes the gain over
    // the 1-day span (huge positive number); Sharpe is positive; max
    // drawdown is 0. Assert the labels and the positive/negative styling
    // rather than exact annualized values.
    mockedGetPortfolioHistory.mockResolvedValue([
      { date: '2026-01-01', portfolioValue: 100, cashValue: 100, holdingsValue: 0 },
      { date: '2026-01-02', portfolioValue: 110, cashValue: 110, holdingsValue: 0 },
    ]);
    renderPage();

    expect(await screen.findAllByText('CAGR').then((nodes) => nodes.length)).toBe(7);
    expect(screen.getAllByText('Sharpe').length).toBe(7);
    expect(screen.getAllByText('Max Drawdown').length).toBe(7);
    // Positive CAGR renders with the positive pnl class.
    expect(document.querySelectorAll('.compare-stats .pnl.positive').length).toBeGreaterThanOrEqual(7);
  });

  it('renders negative metrics when the series loses value', async () => {
    mockedGetPortfolioHistory.mockResolvedValue([
      { date: '2026-01-01', portfolioValue: 100, cashValue: 100, holdingsValue: 0 },
      { date: '2026-01-02', portfolioValue: 50, cashValue: 50, holdingsValue: 0 },
    ]);
    renderPage();

    expect(await screen.findAllByText('Max Drawdown').then((nodes) => nodes.length)).toBe(7);
    // -50% over 1 day annualizes to roughly -100% CAGR; max drawdown is
    // -50%. Both render with the negative pnl class.
    expect(document.querySelectorAll('.compare-stats .pnl.negative').length).toBeGreaterThanOrEqual(7);
  });

  it('renders gap summary rows', async () => {
    renderPage();

    expect(await screen.findByTestId('gap-table')).toBeInTheDocument();
    expect(screen.getAllByText('Trader One').length).toBeGreaterThan(0);
    expect(screen.getAllByText('System-Reckless').length).toBeGreaterThan(0);
  });

  it('fetches history for all roster users for the chart', async () => {
    renderPage();

    await waitFor(() => {
      expect(mockedGetPortfolioHistory).toHaveBeenCalledTimes(7);
      for (let id = 1; id <= 7; id++) {
        expect(mockedGetPortfolioHistory).toHaveBeenCalledWith(id);
      }
    });
  });

  it('shows error state when fetch fails and retries', async () => {
    // Persistent rejection: fetchData re-fires once the user roster loads, so
    // the failure must survive that refetch until we retry.
    mockedGetComparison.mockRejectedValue(new Error('boom'));
    const user = userEvent.setup();

    renderPage();

    expect(await screen.findByText(/boom/i)).toBeInTheDocument();

    mockedGetComparison.mockResolvedValue(sampleComparison);
    await user.click(screen.getByRole('button', { name: /retry/i }));

    expect(await screen.findByText('Your Portfolio')).toBeInTheDocument();
  });
});
