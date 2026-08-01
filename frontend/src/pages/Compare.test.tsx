// Compare page tests: three-column layout, gap table, error/retry.
// API is mocked (getComparison + the three history endpoints).

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Compare from '../pages/Compare';
import { getComparison, getPortfolioHistory } from '../services/api';
import type { PortfolioComparison } from '../types';

vi.mock('../services/api');

const mockedGetComparison = vi.mocked(getComparison);
const mockedGetPortfolioHistory = vi.mocked(getPortfolioHistory);

const sampleComparison: PortfolioComparison = {
  portfolios: [
    { userId: 1, name: 'Trader One', cash: 90000, holdingsValue: 20000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
    { userId: 2, name: 'System', cash: 50000, holdingsValue: 60000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
    { userId: 3, name: 'System-Reckless', cash: -20000, holdingsValue: 130000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
  ],
  gaps: [
    { leader: 'Trader One', trailer: 'System', gapRupees: 0, gapPercent: 0 },
    { leader: 'System-Reckless', trailer: 'Trader One', gapRupees: 15000, gapPercent: 0.15 },
  ],
};

function mockDefaultResponses() {
  mockedGetComparison.mockResolvedValue(sampleComparison);
  mockedGetPortfolioHistory.mockResolvedValue([]);
}

describe('Compare', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockDefaultResponses();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders three portfolio columns', async () => {
    render(<Compare />);

    expect(await screen.findByText('Your Portfolio')).toBeInTheDocument();
    expect(screen.getAllByText('System').length).toBeGreaterThan(0);
    expect(screen.getAllByText('System-Reckless').length).toBeGreaterThan(0);
    expect(screen.getByTestId('column-your')).toBeInTheDocument();
    expect(screen.getByTestId('column-system')).toBeInTheDocument();
    expect(screen.getByTestId('column-reckless')).toBeInTheDocument();
  });

  it('shows cash, holdings, total value and return per column', async () => {
    render(<Compare />);

    // Each column renders a Cash/Holdings/Total Value stat block.
    expect(await screen.findAllByText('Cash').then((nodes) => nodes.length)).toBe(3);
    expect(screen.getAllByText('Total Value').length).toBe(3);
    expect(screen.getAllByText('₹1,10,000').length).toBeGreaterThanOrEqual(1);
  });

  it('renders gap summary rows', async () => {
    render(<Compare />);

    expect(await screen.findByTestId('gap-table')).toBeInTheDocument();
    expect(screen.getAllByText('Trader One').length).toBeGreaterThan(0);
    expect(screen.getAllByText('System-Reckless').length).toBeGreaterThan(0);
  });

  it('fetches history for all three users for the chart', async () => {
    render(<Compare />);

    await waitFor(() => {
      expect(mockedGetPortfolioHistory).toHaveBeenCalledWith(1);
      expect(mockedGetPortfolioHistory).toHaveBeenCalledWith(2);
      expect(mockedGetPortfolioHistory).toHaveBeenCalledWith(3);
    });
  });

  it('shows error state when fetch fails and retries', async () => {
    mockedGetComparison.mockRejectedValueOnce(new Error('boom'));
    const user = userEvent.setup();

    render(<Compare />);

    expect(await screen.findByText(/boom/i)).toBeInTheDocument();

    mockedGetComparison.mockResolvedValue(sampleComparison);
    await user.click(screen.getByRole('button', { name: /retry/i }));

    expect(await screen.findByText('Your Portfolio')).toBeInTheDocument();
  });
});
