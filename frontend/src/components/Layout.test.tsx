// Layout shell: sidebar nav + header total fetched on mount.

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Layout } from './Layout';
import { UserProvider } from '../context/UserProvider';
import { getPortfolio, getUsers } from '../services/api';
import { ToastProvider } from './Toast';
import { useToast } from './useToast';

vi.mock('../services/api');

const mockedGetPortfolio = vi.mocked(getPortfolio);
const mockedGetUsers = vi.mocked(getUsers);

function renderLayout() {
  return render(
    <ToastProvider>
      <UserProvider>
        <MemoryRouter initialEntries={['/holdings']}>
          <Routes>
            <Route element={<Layout />}>
              <Route path="/holdings" element={<div>Holdings page</div>} />
            </Route>
          </Routes>
        </MemoryRouter>
      </UserProvider>
    </ToastProvider>,
  );
}

// Outlet content that can fire a toast, to prove toasts do not re-trigger the
// Layout's portfolio-total effect (W2).
function ToastTrigger() {
  const { showError } = useToast();
  return <button onClick={() => showError('toast!')}>toast</button>;
}

function renderLayoutWithToastTrigger() {
  return render(
    <ToastProvider>
      <UserProvider>
        <MemoryRouter initialEntries={['/holdings']}>
          <Routes>
            <Route element={<Layout />}>
              <Route path="/holdings" element={<ToastTrigger />} />
            </Route>
          </Routes>
        </MemoryRouter>
      </UserProvider>
    </ToastProvider>,
  );
}

describe('Layout', () => {
  beforeEach(() => {
    mockedGetPortfolio.mockReset();
    mockedGetUsers.mockResolvedValue([{ id: 1, name: 'Trader One' }]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders all navigation items', () => {
    mockedGetPortfolio.mockResolvedValue({ cash: 0, holdingsValue: 0, totalValue: 0 });
    renderLayout();
    for (const label of ['Dashboard', 'Holdings', 'Trades', 'Signals', 'System Portfolio', 'Compare', 'Performance', 'Admin', 'Settings']) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it('fetches and formats the portfolio total', async () => {
    mockedGetPortfolio.mockResolvedValue({ cash: 60400, holdingsValue: 45750, totalValue: 106150 });
    renderLayout();
    await waitFor(() => {
      expect(screen.getByTestId('header-total')).toHaveTextContent('₹1,06,150');
    });
  });

  it('shows a placeholder total before data arrives', () => {
    mockedGetPortfolio.mockImplementation(() => new Promise(() => {}));
    renderLayout();
    expect(screen.getByTestId('header-total')).toHaveTextContent('--');
  });

  it('shows an error toast when the fetch fails', async () => {
    mockedGetPortfolio.mockRejectedValue(new Error('Network down'));
    renderLayout();
    expect(await screen.findByRole('alert')).toHaveTextContent('Failed to load portfolio total');
  });

  it('renders the outlet content', () => {
    mockedGetPortfolio.mockResolvedValue({ cash: 0, holdingsValue: 0, totalValue: 0 });
    renderLayout();
    expect(screen.getByText('Holdings page')).toBeInTheDocument();
  });

  it('switching the user picker refetches the portfolio total', async () => {
    const user = userEvent.setup();
    mockedGetPortfolio.mockResolvedValue({ cash: 0, holdingsValue: 0, totalValue: 106150 });
    mockedGetUsers.mockResolvedValue([
      { id: 1, name: 'Trader One' },
      { id: 2, name: 'System Portfolio' },
    ]);
    renderLayout();

    await waitFor(() => {
      expect(screen.getByTestId('header-total')).toHaveTextContent('₹1,06,150');
    });

    mockedGetPortfolio.mockResolvedValue({ cash: 0, holdingsValue: 0, totalValue: 200000 });
    await user.selectOptions(screen.getByTestId('user-picker'), '2');

    await waitFor(() => {
      expect(mockedGetPortfolio).toHaveBeenCalledWith(2);
    });
    expect(screen.getByTestId('header-total')).toHaveTextContent('₹2,00,000');
  });

  it('does not refetch the portfolio total when a toast fires (W2)', async () => {
    mockedGetPortfolio.mockResolvedValue({ cash: 0, holdingsValue: 0, totalValue: 106150 });
    renderLayoutWithToastTrigger();

    await waitFor(() => {
      expect(mockedGetPortfolio).toHaveBeenCalledTimes(1);
    });

    fireEvent.click(screen.getByRole('button', { name: 'toast' }));
    expect(screen.getByRole('alert')).toHaveTextContent('toast!');

    // Give any spurious refetch (old behavior: showError identity changed on
    // every toast, re-firing the effect) a chance to run, then assert it did
    // not.
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 100));
    });
    expect(mockedGetPortfolio).toHaveBeenCalledTimes(1);
  });
});