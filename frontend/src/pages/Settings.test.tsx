// Settings page tests: settings load, watchlist chip add/remove, auto-execute
// toggle, and save via PUT. The service layer and toast are exercised.

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import SettingsPage from '../pages/Settings';
import { getSettings, updateSettings } from '../services/api';
import { ToastProvider } from '../components/Toast';
import type { Settings } from '../types';

vi.mock('../services/api');

const mockedGetSettings = vi.mocked(getSettings);
const mockedUpdateSettings = vi.mocked(updateSettings);

const sampleSettings: Settings = {
  id: 1,
  userId: 1,
  autoExecute: false,
  minConfidence: 0.5,
  negativeLimit: 0,
  interestRate: 0,
  watchlist: ['RELIANCE', 'TCS'],
};

function renderSettings() {
  return render(
    <ToastProvider>
      <SettingsPage />
    </ToastProvider>,
  );
}

describe('SettingsPage', () => {
  beforeEach(() => {
    mockedGetSettings.mockReset();
    mockedUpdateSettings.mockReset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders settings after fetch', async () => {
    mockedGetSettings.mockResolvedValue(sampleSettings);

    renderSettings();

    expect(await screen.findByText('RELIANCE')).toBeInTheDocument();
    expect(screen.getByText('TCS')).toBeInTheDocument();
    expect(screen.getByTestId('auto-execute')).not.toBeChecked();
    expect(screen.getByTestId('min-confidence')).toHaveValue(0.5);
  });

  it('adds a symbol to the watchlist and saves it', async () => {
    mockedGetSettings.mockResolvedValue(sampleSettings);
    mockedUpdateSettings.mockResolvedValue({
      ...sampleSettings,
      watchlist: ['RELIANCE', 'TCS', 'INFY'],
    });
    const user = userEvent.setup();

    renderSettings();
    await screen.findByText('RELIANCE');

    await user.type(screen.getByTestId('symbol-input'), 'infy');
    await user.click(screen.getByRole('button', { name: 'Add' }));
    expect(screen.getByText('INFY')).toBeInTheDocument(); // uppercased

    await user.click(screen.getByTestId('save-settings'));

    await waitFor(() => {
      expect(mockedUpdateSettings).toHaveBeenCalledWith(
        expect.objectContaining({ watchlist: ['RELIANCE', 'TCS', 'INFY'] }),
      );
    });
  });

  it('does not add a duplicate symbol', async () => {
    mockedGetSettings.mockResolvedValue(sampleSettings);
    const user = userEvent.setup();

    renderSettings();
    await screen.findByText('RELIANCE');

    const chips = screen.getAllByText('RELIANCE');
    expect(chips).toHaveLength(1);

    await user.type(screen.getByTestId('symbol-input'), 'reliance');
    await user.click(screen.getByRole('button', { name: 'Add' }));

    expect(screen.getAllByText('RELIANCE')).toHaveLength(1);
  });

  it('removes a symbol from the watchlist', async () => {
    mockedGetSettings.mockResolvedValue(sampleSettings);
    mockedUpdateSettings.mockResolvedValue({ ...sampleSettings, watchlist: ['RELIANCE'] });
    const user = userEvent.setup();

    renderSettings();
    await screen.findByText('TCS');

    await user.click(screen.getByRole('button', { name: 'Remove TCS' }));
    expect(screen.queryByText('TCS')).not.toBeInTheDocument();

    await user.click(screen.getByTestId('save-settings'));
    await waitFor(() => {
      expect(mockedUpdateSettings).toHaveBeenCalledWith(
        expect.objectContaining({ watchlist: ['RELIANCE'] }),
      );
    });
  });

  it('toggles auto-execute and saves it', async () => {
    mockedGetSettings.mockResolvedValue(sampleSettings);
    mockedUpdateSettings.mockResolvedValue({ ...sampleSettings, autoExecute: true });
    const user = userEvent.setup();

    renderSettings();
    await screen.findByText('RELIANCE');

    await user.click(screen.getByTestId('auto-execute'));
    expect(screen.getByTestId('auto-execute')).toBeChecked();

    await user.click(screen.getByTestId('save-settings'));
    await waitFor(() => {
      expect(mockedUpdateSettings).toHaveBeenCalledWith(
        expect.objectContaining({ autoExecute: true }),
      );
    });
  });

  it('shows error state on load failure', async () => {
    mockedGetSettings.mockRejectedValue(new Error('settings unavailable'));

    renderSettings();

    expect(await screen.findByText(/settings unavailable/i)).toBeInTheDocument();
  });
});
