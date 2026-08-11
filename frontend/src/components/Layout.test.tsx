// Layout shell: sidebar nav + header total fetched on mount.

import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Layout } from './Layout';
import { UserProvider } from '../context/UserProvider';
import { getPortfolio, getUsers } from '../services/api';
import { ToastProvider } from './Toast';

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
});