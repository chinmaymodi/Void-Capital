// Admin page tests: status cards, run-signals, per-user config save,
// global settings save, square-off with confirmation. API is mocked.

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Admin from '../pages/Admin';
import { ToastProvider } from '../components/Toast';
import {
  getAdminSettings,
  getAdminStatus,
  runSignalGeneration,
  squareOff,
  updateAdminSettings,
  updateGlobalSettings,
} from '../services/api';
import type { AdminStatus, Settings } from '../types';

vi.mock('../services/api');

const mockedGetAdminStatus = vi.mocked(getAdminStatus);
const mockedGetAdminSettings = vi.mocked(getAdminSettings);
const mockedRunSignalGeneration = vi.mocked(runSignalGeneration);
const mockedUpdateAdminSettings = vi.mocked(updateAdminSettings);
const mockedUpdateGlobalSettings = vi.mocked(updateGlobalSettings);
const mockedSquareOff = vi.mocked(squareOff);

const sampleStatus: AdminStatus = {
  utcNow: '2026-08-02T10:00:00Z',
  pendingSignalCount: 4,
  users: [
    { userId: 2, name: 'System', currentCash: 50000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
    { userId: 3, name: 'System-Reckless', currentCash: -20000, totalValue: 110000, totalReturn: 10000, totalReturnPercent: 0.1 },
  ],
};

const settings2: Settings = {
  id: 1,
  userId: 2,
  autoExecute: true,
  minConfidence: 0.5,
  negativeLimit: 0,
  interestRate: 0,
  watchlist: [],
};

const settings3: Settings = {
  id: 2,
  userId: 3,
  autoExecute: true,
  minConfidence: 0.5,
  negativeLimit: 100000,
  interestRate: 0.0005,
  watchlist: [],
};

function mockDefaultResponses() {
  mockedGetAdminStatus.mockResolvedValue(sampleStatus);
  mockedGetAdminSettings.mockImplementation((userId: number) =>
    Promise.resolve(userId === 2 ? settings2 : settings3),
  );
  mockedRunSignalGeneration.mockResolvedValue('Signal generation completed');
  mockedUpdateAdminSettings.mockImplementation((userId: number, s: Settings) =>
    Promise.resolve({ ...s, userId }),
  );
  mockedUpdateGlobalSettings.mockResolvedValue([settings2, settings3]);
  mockedSquareOff.mockResolvedValue({
    userId: 3,
    positionsSold: 3,
    proceeds: 40000,
    remainingCash: 20000,
  });
}

function renderPage() {
  return render(
    <ToastProvider>
      <Admin />
    </ToastProvider>,
  );
}

describe('Admin', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockDefaultResponses();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders status cards with pending count and user returns', async () => {
    renderPage();

    expect(await screen.findByText('Pending Signals')).toBeInTheDocument();
    expect(screen.getByText('4')).toBeInTheDocument();
    expect(await screen.findByText('System Return')).toBeInTheDocument();
  });

  it('shows config forms for both system users', async () => {
    renderPage();

    expect(await screen.findByTestId('config-2')).toBeInTheDocument();
    expect(screen.getByTestId('config-3')).toBeInTheDocument();
    expect(screen.getByTestId('negative-limit-3')).toHaveValue(100000);
    expect(screen.getByTestId('interest-rate-3')).toHaveValue(0.0005);
  });

  it('run signals calls the API and shows success toast', async () => {
    const user = userEvent.setup();
    renderPage();

    await screen.findByTestId('run-signals');
    await user.click(screen.getByTestId('run-signals'));

    await waitFor(() => {
      expect(mockedRunSignalGeneration).toHaveBeenCalledTimes(1);
    });
    expect(await screen.findByText(/Signal generation completed/i)).toBeInTheDocument();
  });

  it('saves per-user config via PUT', async () => {
    const user = userEvent.setup();
    renderPage();

    await screen.findByTestId('config-3');

    const limitInput = screen.getByTestId('negative-limit-3');
    await user.clear(limitInput);
    await user.type(limitInput, '150000');
    await user.click(screen.getByTestId('save-config-3'));

    await waitFor(() => {
      expect(mockedUpdateAdminSettings).toHaveBeenCalledWith(
        3,
        expect.objectContaining({ negativeLimit: 150000 }),
      );
    });
  });

  it('saves global settings (min confidence + watchlist)', async () => {
    const user = userEvent.setup();
    renderPage();

    await screen.findByTestId('save-global');

    const confInput = screen.getByTestId('global-min-confidence');
    await user.clear(confInput);
    await user.type(confInput, '0.60');
    await user.type(screen.getByTestId('global-watchlist'), 'RELIANCE, tcs');
    await user.click(screen.getByTestId('save-global'));

    await waitFor(() => {
      expect(mockedUpdateGlobalSettings).toHaveBeenCalledWith(
        0.6,
        ['RELIANCE', 'TCS'],
      );
    });
  });

  it('square off requires confirmation then sells all holdings', async () => {
    const user = userEvent.setup();
    renderPage();

    await screen.findByTestId('square-off-reckless');
    await user.click(screen.getByTestId('square-off-reckless'));

    expect(screen.getByTestId('square-off-confirm')).toBeInTheDocument();
    expect(mockedSquareOff).not.toHaveBeenCalled();

    await user.click(screen.getByTestId('square-off-confirm-button'));

    await waitFor(() => {
      expect(mockedSquareOff).toHaveBeenCalledWith(3);
    });
    expect(await screen.findByText(/Sold 3 position\(s\) for proceeds/)).toBeInTheDocument();
    expect(screen.getByText('Square Off Complete')).toBeInTheDocument();
  });

  it('shows error state when status fetch fails and retries', async () => {
    mockedGetAdminStatus.mockRejectedValueOnce(new Error('boom'));
    const user = userEvent.setup();

    renderPage();

    expect(await screen.findByText(/boom/i)).toBeInTheDocument();

    mockedGetAdminStatus.mockResolvedValue(sampleStatus);
    await user.click(screen.getByRole('button', { name: /retry/i }));

    expect(await screen.findByText('Pending Signals')).toBeInTheDocument();
  });
});
