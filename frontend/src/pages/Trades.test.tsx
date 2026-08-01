// Trades page tests: table rendering, filter application, pagination,
// page-size change, and CSV export. The service layer is mocked.

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Trades from '../pages/Trades';
import { getTrades } from '../services/api';
import type { PagedTrades } from '../types';

vi.mock('../services/api');

const mockedGetTrades = vi.mocked(getTrades);

const samplePage: PagedTrades = {
  items: [
    {
      id: 1,
      symbol: 'RELIANCE',
      type: 'BUY',
      shares: 10,
      price: 2850,
      total: 28500,
      reason: 'SMA crossover',
      timestamp: '2026-08-01T10:00:00Z',
    },
    {
      id: 2,
      symbol: 'TCS',
      type: 'SELL',
      shares: 3,
      price: 3800,
      total: 11400,
      reason: null,
      timestamp: '2026-08-01T11:00:00Z',
    },
  ],
  total: 42,
  page: 1,
  pageSize: 20,
};

function renderTrades() {
  return render(<Trades />);
}

describe('Trades', () => {
  beforeEach(() => {
    mockedGetTrades.mockReset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders trade rows after fetch', async () => {
    mockedGetTrades.mockResolvedValue(samplePage);

    renderTrades();

    expect(await screen.findByText('RELIANCE')).toBeInTheDocument();
    expect(screen.getByText('TCS')).toBeInTheDocument();
    expect(screen.getByTestId('trades-table')).toBeInTheDocument();
    expect(screen.getByText(/42 trades/)).toBeInTheDocument();
  });

  it('applies filters on Apply and refetches with them', async () => {
    mockedGetTrades.mockResolvedValue(samplePage);
    const user = userEvent.setup();

    renderTrades();
    await screen.findByText('RELIANCE');

    await user.type(screen.getByTestId('filter-symbol'), 'reliance');
    await user.selectOptions(screen.getByTestId('filter-type'), 'BUY');
    await user.click(screen.getByTestId('apply-filters'));

    await waitFor(() => {
      expect(mockedGetTrades).toHaveBeenLastCalledWith(
        expect.objectContaining({
          symbol: 'RELIANCE', // uppercased on apply
          type: 'BUY',
          page: 1,
        }),
      );
    });
  });

  it('reset clears filters back to defaults', async () => {
    mockedGetTrades.mockResolvedValue(samplePage);
    const user = userEvent.setup();

    renderTrades();
    await screen.findByText('RELIANCE');

    await user.type(screen.getByTestId('filter-symbol'), 'TCS');
    await user.click(screen.getByTestId('apply-filters'));
    await user.click(screen.getByRole('button', { name: 'Reset' }));

    expect(screen.getByTestId('filter-symbol')).toHaveValue('');
    await waitFor(() => {
      expect(mockedGetTrades).toHaveBeenLastCalledWith(
        expect.objectContaining({ symbol: undefined, type: undefined, from: undefined, to: undefined, page: 1 }),
      );
    });
  });

  it('changes page size and resets to page 1', async () => {
    mockedGetTrades.mockResolvedValue(samplePage);
    const user = userEvent.setup();

    renderTrades();
    await screen.findByText('RELIANCE');

    await user.selectOptions(screen.getByTestId('page-size'), '50');

    await waitFor(() => {
      expect(mockedGetTrades).toHaveBeenLastCalledWith(
        expect.objectContaining({ pageSize: 50, page: 1 }),
      );
    });
  });

  it('navigates to next page', async () => {
    mockedGetTrades.mockResolvedValue(samplePage);
    const user = userEvent.setup();

    renderTrades();
    await screen.findByText('RELIANCE');

    const next = screen.getByRole('button', { name: 'Next' });
    await user.click(next);

    await waitFor(() => {
      expect(mockedGetTrades).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }));
    });
  });

  it('opens CSV export URL with active filters', async () => {
    mockedGetTrades.mockResolvedValue(samplePage);
    const user = userEvent.setup();
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);

    renderTrades();
    await screen.findByText('RELIANCE');

    await user.type(screen.getByTestId('filter-symbol'), 'TCS');
    await user.click(screen.getByTestId('apply-filters'));
    await user.click(screen.getByTestId('export-csv'));

    expect(openSpy).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/trades/1/export?symbol=TCS'),
      '_blank',
    );
    openSpy.mockRestore();
  });

  it('shows empty state when no trades match', async () => {
    mockedGetTrades.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 20 });

    renderTrades();

    expect(await screen.findByText(/No trades match these filters/i)).toBeInTheDocument();
  });

  it('shows error state when fetch fails and retries', async () => {
    mockedGetTrades.mockRejectedValueOnce(new Error('boom'));
    const user = userEvent.setup();

    renderTrades();

    expect(await screen.findByText(/boom/i)).toBeInTheDocument();

    mockedGetTrades.mockResolvedValue(samplePage);
    await user.click(screen.getByRole('button', { name: /retry/i }));

    expect(await screen.findByText('RELIANCE')).toBeInTheDocument();
  });
});
