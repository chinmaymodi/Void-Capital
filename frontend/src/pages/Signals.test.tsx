// Signals page tests: card rendering, approve/reject, batch actions with
// confirmation, empty/error states. The service layer is mocked.

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Signals from '../pages/Signals';
import { ToastProvider } from '../components/Toast';
import { UserProvider } from '../context/UserProvider';
import {
  approveSignal,
  batchApproveSignals,
  batchRejectSignals,
  getTodaySignals,
  getUsers,
  rejectSignal,
} from '../services/api';
import type { Signal } from '../types';

vi.mock('../services/api');

const mockedGetTodaySignals = vi.mocked(getTodaySignals);
const mockedApproveSignal = vi.mocked(approveSignal);
const mockedRejectSignal = vi.mocked(rejectSignal);
const mockedBatchApprove = vi.mocked(batchApproveSignals);
const mockedBatchReject = vi.mocked(batchRejectSignals);
const mockedGetUsers = vi.mocked(getUsers);

const sampleSignals: Signal[] = [
  {
    id: 1,
    date: '2026-08-02',
    symbol: 'RELIANCE',
    action: 'BUY',
    confidence: 0.72,
    reason: 'SMA crossover bullish',
    modelName: 'sma',
    status: 'PENDING',
    suggestedQuantity: 10,
    entryPrice: 2860,
    targetPrice: 3000,
    stopLoss: 2700,
    failureReason: null,
  },
  {
    id: 2,
    date: '2026-08-02',
    symbol: 'TCS',
    action: 'SELL',
    confidence: 0.55,
    reason: null,
    modelName: 'rsi',
    status: 'PENDING',
    suggestedQuantity: 5,
    entryPrice: 3800,
    targetPrice: null,
    stopLoss: null,
    failureReason: null,
  },
];

function renderSignals() {
  return render(
    <ToastProvider>
      <UserProvider>
        <Signals />
      </UserProvider>
    </ToastProvider>,
  );
}

describe('Signals', () => {
  beforeEach(() => {
    mockedGetTodaySignals.mockReset();
    mockedApproveSignal.mockReset();
    mockedRejectSignal.mockReset();
    mockedBatchApprove.mockReset();
    mockedBatchReject.mockReset();
    mockedGetUsers.mockResolvedValue([{ id: 1, name: 'Trader One' }]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders signal cards after fetch', async () => {
    mockedGetTodaySignals.mockResolvedValue(sampleSignals);

    renderSignals();

    expect(await screen.findByText('RELIANCE')).toBeInTheDocument();
    expect(screen.getByText('TCS')).toBeInTheDocument();
    expect(screen.getByTestId('approve-1')).toBeInTheDocument();
    expect(screen.getByTestId('reject-2')).toBeInTheDocument();
    expect(screen.getByText(/72%/)).toBeInTheDocument();
    expect(screen.getByText('sma')).toBeInTheDocument();
  });

  it('approve updates the card without a full reload', async () => {
    mockedGetTodaySignals.mockResolvedValue(sampleSignals);
    mockedApproveSignal.mockResolvedValue({ ...sampleSignals[0], status: 'APPROVED' });
    const user = userEvent.setup();

    renderSignals();
    await screen.findByText('RELIANCE');

    await user.click(screen.getByTestId('approve-1'));

    expect(await screen.findByText('Approved')).toBeInTheDocument();
    expect(mockedApproveSignal).toHaveBeenCalledWith(1);
    expect(mockedGetTodaySignals).toHaveBeenCalledTimes(1); // no refetch on approve
  });

  it('reject updates the card', async () => {
    mockedGetTodaySignals.mockResolvedValue(sampleSignals);
    mockedRejectSignal.mockResolvedValue({ ...sampleSignals[1], status: 'REJECTED' });
    const user = userEvent.setup();

    renderSignals();
    await screen.findByText('TCS');

    await user.click(screen.getByTestId('reject-2'));

    expect(await screen.findByText('Rejected')).toBeInTheDocument();
    expect(mockedRejectSignal).toHaveBeenCalledWith(2);
  });

  it('batch approve requires confirmation then calls the API', async () => {
    mockedGetTodaySignals.mockResolvedValue(sampleSignals);
    mockedBatchApprove.mockResolvedValue([{ id: 1, success: true, error: null }]);
    const user = userEvent.setup();

    renderSignals();
    await screen.findByText('RELIANCE');

    await user.click(screen.getByTestId('select-1'));
    await user.click(screen.getByTestId('batch-approve'));

    // Confirmation dialog appears; API not called yet.
    expect(screen.getByTestId('batch-confirm')).toBeInTheDocument();
    expect(mockedBatchApprove).not.toHaveBeenCalled();

    await user.click(screen.getByTestId('batch-confirm-button'));

    await waitFor(() => {
      expect(mockedBatchApprove).toHaveBeenCalledWith([1]);
    });
    expect(await screen.findByText('Approved')).toBeInTheDocument();
    expect(screen.queryByTestId('batch-confirm')).not.toBeInTheDocument();
  });

  it('batch reject cancels without calling the API', async () => {
    mockedGetTodaySignals.mockResolvedValue(sampleSignals);
    const user = userEvent.setup();

    renderSignals();
    await screen.findByText('TCS');

    await user.click(screen.getByTestId('select-2'));
    await user.click(screen.getByTestId('batch-reject'));
    await user.click(screen.getByTestId('batch-cancel'));

    expect(screen.queryByTestId('batch-confirm')).not.toBeInTheDocument();
    expect(mockedBatchReject).not.toHaveBeenCalled();
  });

  it('batch reject success marks cards REJECTED', async () => {
    mockedGetTodaySignals.mockResolvedValue(sampleSignals);
    mockedBatchReject.mockResolvedValue([{ id: 2, success: true, error: null }]);
    const user = userEvent.setup();

    renderSignals();
    await screen.findByText('TCS');

    await user.click(screen.getByTestId('select-2'));
    await user.click(screen.getByTestId('batch-reject'));
    await user.click(screen.getByTestId('batch-confirm-button'));

    expect(await screen.findByText('Rejected')).toBeInTheDocument();
    expect(mockedBatchReject).toHaveBeenCalledWith([2]);
  });

  it('select all toggles every card', async () => {
    mockedGetTodaySignals.mockResolvedValue(sampleSignals);
    const user = userEvent.setup();

    renderSignals();
    await screen.findByText('RELIANCE');

    const selectAll = screen.getByTestId('select-all');
    await user.click(selectAll);

    expect(selectAll).toBeChecked();
    expect(screen.getByTestId('select-1')).toBeChecked();
    expect(screen.getByTestId('select-2')).toBeChecked();
  });

  it('shows empty state when no signals', async () => {
    mockedGetTodaySignals.mockResolvedValue([]);

    renderSignals();

    expect(await screen.findByText(/No pending signals today/i)).toBeInTheDocument();
  });

  it('shows error state when fetch fails and retries', async () => {
    mockedGetTodaySignals.mockRejectedValueOnce(new Error('boom'));
    const user = userEvent.setup();

    renderSignals();

    expect(await screen.findByText(/boom/i)).toBeInTheDocument();

    mockedGetTodaySignals.mockResolvedValue(sampleSignals);
    await user.click(screen.getByRole('button', { name: /retry/i }));

    expect(await screen.findByText('RELIANCE')).toBeInTheDocument();
  });
});
