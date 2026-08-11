// Toast provider: renders, auto-dismisses, and stacks toasts.

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ToastProvider } from './Toast';
import { useToast } from './useToast';

function Probe() {
  const { showError, showSuccess } = useToast();
  return (
    <div>
      <button onClick={() => showError('Something broke')}>fail</button>
      <button onClick={() => showSuccess('Saved')}>ok</button>
    </div>
  );
}

function renderProbe() {
  return render(
    <ToastProvider>
      <Probe />
    </ToastProvider>,
  );
}

describe('ToastProvider', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows an error toast with the message', () => {
    renderProbe();
    fireEvent.click(screen.getByRole('button', { name: 'fail' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Something broke');
  });

  it('shows a success toast', () => {
    renderProbe();
    fireEvent.click(screen.getByRole('button', { name: 'ok' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Saved');
  });

  it('auto-dismisses after 4 seconds', () => {
    vi.useFakeTimers();
    renderProbe();
    fireEvent.click(screen.getByRole('button', { name: 'fail' }));
    expect(screen.getByRole('alert')).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(4000);
    });

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('dismisses manually via the close button', () => {
    renderProbe();
    fireEvent.click(screen.getByRole('button', { name: 'fail' }));
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('stacks multiple toasts', () => {
    renderProbe();
    fireEvent.click(screen.getByRole('button', { name: 'fail' }));
    fireEvent.click(screen.getByRole('button', { name: 'ok' }));
    expect(screen.getAllByRole('alert')).toHaveLength(2);
  });
});