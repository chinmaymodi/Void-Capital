// Holdings page tests: table rendering, sorting, and buy/sell modal flows.
// The service layer is mocked; components render in jsdom without a router.

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Holdings from '../pages/Holdings';
import { UserProvider } from '../context/UserProvider';
import { buyStock, getHoldings, getUsers, sellStock } from '../services/api';
import { ToastProvider } from '../components/Toast';

vi.mock('../services/api');

const mockedGetHoldings = vi.mocked(getHoldings);
const mockedBuy = vi.mocked(buyStock);
const mockedSell = vi.mocked(sellStock);
const mockedGetUsers = vi.mocked(getUsers);

const sampleHoldings = [
  {
    id: 1,
    symbol: 'RELIANCE',
    shares: 10,
    avgBuyPrice: 2850,
    currentPrice: 2880,
    unrealizedPnl: 300,
    percentOfPortfolio: 0.35,
  },
  {
    id: 2,
    symbol: 'TCS',
    shares: 3,
    avgBuyPrice: 3800,
    currentPrice: 3700,
    unrealizedPnl: -300,
    percentOfPortfolio: 0.22,
  },
];

function renderHoldings() {
  return render(
    <ToastProvider>
      <UserProvider>
        <Holdings />
      </UserProvider>
    </ToastProvider>,
  );
}

describe('Holdings', () => {
  beforeEach(() => {
    mockedGetHoldings.mockReset();
    mockedBuy.mockReset();
    mockedSell.mockReset();
    mockedGetUsers.mockResolvedValue([{ id: 1, name: 'Trader One' }]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders holdings rows after fetch', async () => {
    mockedGetHoldings.mockResolvedValue(sampleHoldings);

    renderHoldings();

    expect(await screen.findByText('RELIANCE')).toBeInTheDocument();
    expect(screen.getByText('TCS')).toBeInTheDocument();
    expect(screen.getByTestId('holdings-table')).toBeInTheDocument();
  });

  it('sorts by symbol ascending then descending on click', async () => {
    mockedGetHoldings.mockResolvedValue(sampleHoldings);
    const user = userEvent.setup();

    renderHoldings();
    await screen.findByText('RELIANCE');

    const table = screen.getByTestId('holdings-table');
    const symbolSort = within(table).getByRole('button', { name: /Symbol/ });
    await user.click(symbolSort);

    const rows = within(table).getAllByRole('row');
    // Initial sort is symbol asc; first click toggles to desc: TCS, RELIANCE
    expect(within(rows[1]).getByText('TCS')).toBeInTheDocument();
    expect(within(rows[2]).getByText('RELIANCE')).toBeInTheDocument();

    await user.click(symbolSort);
    // Second click toggles back to asc: RELIANCE, TCS (re-query: keyed rows
    // are moved by React, so stale element references are unreliable).
    const rowsAsc = within(table).getAllByRole('row');
    expect(within(rowsAsc[1]).getByText('RELIANCE')).toBeInTheDocument();
    expect(within(rowsAsc[2]).getByText('TCS')).toBeInTheDocument();
  });

  it('opens buy modal and submits a trade', async () => {
    mockedGetHoldings.mockResolvedValue(sampleHoldings);
    mockedBuy.mockResolvedValue({
      id: 99,
      symbol: 'RELIANCE',
      type: 'BUY',
      shares: 5,
      price: 2880,
      total: 14400,
      reason: 'Manual trade',
      timestamp: '2026-08-01T12:00:00Z',
    });
    const user = userEvent.setup();

    renderHoldings();
    await screen.findByText('RELIANCE');

    const buyButtons = screen.getAllByRole('button', { name: 'Buy' });
    await user.click(buyButtons[0]);

    const dialog = screen.getByRole('dialog');
    const input = within(dialog).getByTestId('shares-input');
    await user.type(input, '5');
    await user.click(within(dialog).getByTestId('submit-trade'));

    await waitFor(() => {
      expect(mockedBuy).toHaveBeenCalledWith({ symbol: 'RELIANCE', shares: 5 }, 1);
    });
    expect(mockedGetHoldings).toHaveBeenCalledTimes(2); // initial + refresh
  });

  it('rejects selling more than the current holding', async () => {
    mockedGetHoldings.mockResolvedValue(sampleHoldings);
    const user = userEvent.setup();

    renderHoldings();
    await screen.findByText('TCS');

    // TCS row has 3 shares; attempt to sell 10.
    const rows = screen.getAllByRole('row');
    const tcsRow = rows.find((r) => within(r).queryByText('TCS'));
    const sellButton = within(tcsRow!).getByRole('button', { name: 'Sell' });
    await user.click(sellButton);

    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByTestId('shares-input'), '10');
    await user.click(within(dialog).getByTestId('submit-trade'));

    expect(await screen.findByText(/only hold 3 shares/i)).toBeInTheDocument();
    expect(mockedSell).not.toHaveBeenCalled();
  });
});
