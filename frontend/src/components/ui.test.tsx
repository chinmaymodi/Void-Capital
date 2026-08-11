// Shared presentational components: pure rendering, no API calls.

import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { EmptyState, ErrorState, Spinner, StatCard } from './ui';

describe('StatCard', () => {
  it('renders label and value', () => {
    render(<StatCard label="Cash" value="₹60,400" />);
    expect(screen.getByText('Cash')).toBeInTheDocument();
    expect(screen.getByText('₹60,400')).toBeInTheDocument();
  });

  it('applies the accent class', () => {
    const { container } = render(<StatCard label="P&L" value="+5%" accent="positive" />);
    expect(container.querySelector('.stat-card')).toHaveClass('accent-positive');
  });

  it('defaults to the default accent', () => {
    const { container } = render(<StatCard label="Total" value="1" />);
    expect(container.querySelector('.stat-card')).toHaveClass('accent-default');
  });
});

describe('Spinner', () => {
  it('renders a status spinner', () => {
    render(<Spinner />);
    expect(screen.getByTestId('spinner')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveAttribute('aria-label', 'Loading');
  });
});

describe('ErrorState', () => {
  it('shows the message', () => {
    render(<ErrorState message="Network down" />);
    expect(screen.getByRole('alert')).toHaveTextContent('Network down');
  });

  it('renders a retry button when onRetry is provided', () => {
    const onRetry = vi.fn();
    render(<ErrorState message="boom" onRetry={onRetry} />);
    const button = screen.getByRole('button', { name: 'Retry' });
    button.click();
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('omits the retry button when no handler is given', () => {
    render(<ErrorState message="boom" />);
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
  });
});

describe('EmptyState', () => {
  it('shows the message', () => {
    render(<EmptyState message="No holdings yet" />);
    expect(screen.getByText('No holdings yet')).toBeInTheDocument();
  });
});