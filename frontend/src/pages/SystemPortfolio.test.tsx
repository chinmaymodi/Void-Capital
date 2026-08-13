// SystemPortfolio page tests: user tab switching, header stats, holdings
// table, trade log, resolution log, and model filtering. The agent list
// derives from the user roster (via UserProvider -> getUsers). API is mocked.

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ToastProvider } from '../components/Toast';
import { UserProvider } from '../context/UserProvider';
import SystemPortfolio from '../pages/SystemPortfolio';
import {
  getAdminSettings,
  getComparison,
  getHoldings,
  getPortfolioHistory,
  getResolvedSignals,
  getTrades,
  getUsers,
} from '../services/api';
import type { ComparisonPortfolio, PagedResolvedSignals, PagedTrades, Settings } from '../types';

vi.mock('../services/api');

const mockedGetUsers = vi.mocked(getUsers);
const mockedGetComparison = vi.mocked(getComparison);
const mockedGetAdminSettings = vi.mocked(getAdminSettings);
const mockedGetHoldings = vi.mocked(getHoldings);
const mockedGetTrades = vi.mocked(getTrades);
const mockedGetResolvedSignals = vi.mocked(getResolvedSignals);
const mockedGetPortfolioHistory = vi.mocked(getPortfolioHistory);

const sampleUsers = [
  { id: 1, name: 'Trader One' },
  { id: 2, name: 'System Portfolio' },
  { id: 3, name: 'System-Reckless' },
  { id: 4, name: 'Options-Careful' },
  { id: 5, name: 'Options-Reckless' },
  { id: 6, name: 'Intraday-Careful' },
  { id: 7, name: 'Intraday-Reckless' },
];

const systemComparison: ComparisonPortfolio = {
  userId: 2,
  name: 'System Portfolio',
  cash: 50000,
  holdingsValue: 60000,
  totalValue: 110000,
  totalReturn: 10000,
  totalReturnPercent: 0.1,
};

const recklessComparison: ComparisonPortfolio = {
  userId: 3,
  name: 'System-Reckless',
  cash: -20000,
  holdingsValue: 130000,
  totalValue: 110000,
  totalReturn: 10000,
  totalReturnPercent: 0.1,
};

const recklessSettings: Settings = {
  id: 2,
  userId: 3,
  autoExecute: true,
  minConfidence: 0.5,
  negativeLimit: 100000,
  interestRate: 0.0005,
  watchlist: [],
};

const sampleHoldings = [
  { id: 1, symbol: 'RELIANCE', shares: 10, avgBuyPrice: 2850, currentPrice: 3000, unrealizedPnl: 1500, percentOfPortfolio: 0.27 },
];

const sampleTrades: PagedTrades = {
  items: [
    {
      id: 10,
      symbol: 'TCS',
      type: 'BUY',
      shares: 5,
      price: 3800,
      total: 19000,
      reason: 'Auto-execute',
      timestamp: '2026-08-01T10:00:00Z',
    },
  ],
  total: 1,
  page: 1,
  pageSize: 10,
};

const sampleResolved: PagedResolvedSignals = {
  items: [
    {
      signalId: 55,
      date: '2026-08-01',
      symbol: 'RELIANCE',
      action: 'BUY',
      modelName: 'sma',
      entryPrice: 2860,
      targetPrice: 3000,
      exitPrice: 3010,
      outcome: 'HIT_TARGET',
      actualReturn: 0.052,
      resolvedAt: '2026-08-02T10:00:00Z',
      evaluationDays: 5,
    },
  ],
  total: 1,
  page: 1,
  pageSize: 10,
};

function mockDefaultResponses() {
  mockedGetUsers.mockResolvedValue(sampleUsers);
  mockedGetComparison.mockResolvedValue({ portfolios: [systemComparison, recklessComparison], gaps: [] });
  mockedGetAdminSettings.mockResolvedValue(recklessSettings);
  mockedGetHoldings.mockResolvedValue(sampleHoldings);
  mockedGetTrades.mockResolvedValue(sampleTrades);
  mockedGetResolvedSignals.mockResolvedValue(sampleResolved);
  mockedGetPortfolioHistory.mockResolvedValue([]);
}

function renderPage() {
  return render(
    <ToastProvider>
      <UserProvider>
        <SystemPortfolio />
      </UserProvider>
    </ToastProvider>,
  );
}

describe('SystemPortfolio', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockDefaultResponses();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders header stats for the selected system user', async () => {
    renderPage();

    expect(await screen.findByText('Starting Budget')).toBeInTheDocument();
    // comparison for user 2: totalValue 110000, totalReturn 10000 -> budget 100000
    expect(screen.getAllByText('₹1,00,000').length).toBeGreaterThanOrEqual(1);
    // one tab per agent (all users except the demo human)
    expect(screen.getByTestId('system-tab-2')).toBeInTheDocument();
    expect(screen.getByTestId('system-tab-3')).toBeInTheDocument();
    expect(screen.getByTestId('system-tab-7')).toBeInTheDocument();
    expect(screen.getByTestId('system-holdings-table')).toBeInTheDocument();
  });

  it('switches to System-Reckless on tab click', async () => {
    const user = userEvent.setup();
    renderPage();

    await screen.findByText('Starting Budget');

    await user.click(screen.getByTestId('system-tab-3'));

    await waitFor(() => {
      expect(mockedGetAdminSettings).toHaveBeenCalledWith(3);
      expect(mockedGetTrades).toHaveBeenCalledWith(expect.objectContaining({ page: 1 }), 3);
      expect(mockedGetResolvedSignals).toHaveBeenCalledWith(
        expect.objectContaining({ userId: 3 }),
      );
    });
    // Negative limit shown for the reckless user.
    expect(await screen.findByText(/Negative Limit/)).toBeInTheDocument();
  });

  it('renders holdings table', async () => {
    renderPage();

    const holdingsTable = await screen.findByTestId('system-holdings-table');
    expect(within(holdingsTable).getByText('RELIANCE')).toBeInTheDocument();
    expect(within(holdingsTable).getByText('10')).toBeInTheDocument();
  });

  it('renders trade log', async () => {
    renderPage();

    expect(await screen.findByTestId('system-trades-table')).toBeInTheDocument();
    expect(screen.getByText('TCS')).toBeInTheDocument();
  });

  it('renders resolution log with outcome badge', async () => {
    renderPage();

    expect(await screen.findByTestId('system-resolved-table')).toBeInTheDocument();
    expect(screen.getByText('Hit Target')).toBeInTheDocument();
    expect(screen.getByText(/5\.2%/)).toBeInTheDocument();
  });

  it('filters resolution log by model', async () => {
    const user = userEvent.setup();
    renderPage();

    await screen.findByTestId('system-resolved-table');

    await user.selectOptions(screen.getByTestId('resolution-model-filter'), 'rsi');
    await user.click(screen.getByTestId('apply-resolution-filter'));

    await waitFor(() => {
      expect(mockedGetResolvedSignals).toHaveBeenLastCalledWith(
        expect.objectContaining({ model: 'rsi', userId: 2 }),
      );
    });
  });

  it('shows error state when comparison fetch fails and retries', async () => {
    mockedGetComparison.mockRejectedValueOnce(new Error('boom'));
    const user = userEvent.setup();

    renderPage();

    expect(await screen.findByText(/boom/i)).toBeInTheDocument();

    mockedGetComparison.mockResolvedValue({ portfolios: [systemComparison, recklessComparison], gaps: [] });
    await user.click(screen.getByRole('button', { name: /retry/i }));

    expect(await screen.findByTestId('system-holdings-table')).toBeInTheDocument();
  });
});
