// SignalPerformance page tests: model cards, resolved table with outcome
// badges, empty state, error/retry. API is mocked.

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import SignalPerformance from '../pages/SignalPerformance';
import { getModelPerformance, getResolvedSignals } from '../services/api';
import type { ModelPerformance, PagedResolvedSignals } from '../types';

vi.mock('../services/api');

const mockedGetModelPerformance = vi.mocked(getModelPerformance);
const mockedGetResolvedSignals = vi.mocked(getResolvedSignals);

const sampleModels: ModelPerformance[] = [
  {
    modelName: 'sma',
    totalSignals: 10,
    resolvedSignals: 8,
    hitTargetCount: 6,
    winRate: 0.75,
    avgReturn: 0.05,
    bestReturn: 0.12,
    worstReturn: -0.03,
  },
  {
    modelName: 'rsi',
    totalSignals: 8,
    resolvedSignals: 5,
    hitTargetCount: 2,
    winRate: 0.4,
    avgReturn: 0.01,
    bestReturn: 0.06,
    worstReturn: -0.02,
  },
];

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
    {
      signalId: 56,
      date: '2026-07-30',
      symbol: 'TCS',
      action: 'SELL',
      modelName: 'rsi',
      entryPrice: 3800,
      targetPrice: null,
      exitPrice: 3700,
      outcome: 'EXPIRED',
      actualReturn: -0.026,
      resolvedAt: '2026-08-01T10:00:00Z',
      evaluationDays: 5,
    },
  ],
  total: 2,
  page: 1,
  pageSize: 100,
};

describe('SignalPerformance', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedGetModelPerformance.mockResolvedValue(sampleModels);
    mockedGetResolvedSignals.mockResolvedValue({ ...sampleResolved, total: 2 });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders per-model summary cards', async () => {
    render(<SignalPerformance />);

    expect(await screen.findAllByText('sma').then((n) => n.length)).toBeGreaterThan(0);
    expect(screen.getByText('8/10 resolved')).toBeInTheDocument();
    expect(screen.getByTestId('winrate-sma')).toHaveTextContent(/75%/);
    expect(screen.getAllByText('rsi').length).toBeGreaterThan(0);
  });

  it('renders resolved signal table with outcome badges', async () => {
    render(<SignalPerformance />);

    expect(await screen.findByTestId('resolved-table')).toBeInTheDocument();
    expect(screen.getByText('Hit Target')).toBeInTheDocument();
    expect(screen.getByText('Expired')).toBeInTheDocument();
    expect(screen.getByText('RELIANCE')).toBeInTheDocument();
    expect(screen.getByText(/5\.2%/)).toBeInTheDocument();
  });

  it('renders win rate and cumulative charts', async () => {
    render(<SignalPerformance />);

    expect(await screen.findByTestId('winrate-chart')).toBeInTheDocument();
    expect(screen.getByTestId('cumulative-chart')).toBeInTheDocument();
  });

  it('shows empty state when no models', async () => {
    mockedGetModelPerformance.mockResolvedValue([]);
    mockedGetResolvedSignals.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100 });

    render(<SignalPerformance />);

    expect(
      await screen.findByText(/No model performance data yet/i),
    ).toBeInTheDocument();
  });

  it('shows error state when fetch fails and retries', async () => {
    mockedGetModelPerformance.mockRejectedValueOnce(new Error('boom'));
    mockedGetResolvedSignals.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100 });
    const user = userEvent.setup();

    render(<SignalPerformance />);

    expect(await screen.findByText(/boom/i)).toBeInTheDocument();

    mockedGetModelPerformance.mockResolvedValue(sampleModels);
    mockedGetResolvedSignals.mockResolvedValue(sampleResolved);
    await user.click(screen.getByRole('button', { name: /retry/i }));

    expect(await screen.findByText('8/10 resolved')).toBeInTheDocument();
  });
});
