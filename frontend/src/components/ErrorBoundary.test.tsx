// ErrorBoundary (W4): catches render/lazy-chunk failures and shows a
// recoverable fallback instead of a blank screen.

import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ErrorBoundary } from './ErrorBoundary';

function Bomb(): never {
  throw new Error('chunk failed to load');
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('ErrorBoundary', () => {
  it('renders children when there is no error', () => {
    render(
      <ErrorBoundary>
        <div>fine</div>
      </ErrorBoundary>,
    );
    expect(screen.getByText('fine')).toBeInTheDocument();
  });

  it('renders the fallback with the error message when a child throws', () => {
    // React logs caught boundary errors to console.error; silence for the test.
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

    render(
      <ErrorBoundary>
        <Bomb />
      </ErrorBoundary>,
    );

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Something went wrong');
    expect(alert).toHaveTextContent('chunk failed to load');
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
    expect(consoleSpy).toHaveBeenCalled();
  });
});