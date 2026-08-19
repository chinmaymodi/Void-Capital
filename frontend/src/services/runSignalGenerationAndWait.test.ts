// runSignalGenerationAndWait polling cap (W3): a job that never leaves
// RUNNING must throw after the 15-minute cap instead of polling forever.
// The real implementation is exercised end-to-end through the axios instance
// with a fake adapter (module-internal references cannot be vi.mock'd), so
// the POST + polling GETs are served from the adapter's job sequence.

import { act } from '@testing-library/react';
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios';
import { afterEach, describe, expect, it, vi } from 'vitest';
import api, { runSignalGenerationAndWait } from './api';
import type { SignalJob } from '../types';

const runningJob: SignalJob = {
  jobId: 7,
  status: 'RUNNING',
  startedAt: '2026-08-17T10:00:00Z',
  finishedAt: null,
  message: '',
};

const doneJob: SignalJob = {
  jobId: 7,
  status: 'SUCCEEDED',
  startedAt: '2026-08-17T10:00:00Z',
  finishedAt: '2026-08-17T10:02:00Z',
  message: 'Signal generation completed',
};

// Serves the POST /admin/run-signals with the running job, then each polling
// GET /admin/run-signals/{id} with the next job in the sequence (last one
// repeats).
function fakeAdapter(jobSequence: SignalJob[]): AxiosAdapter {
  let poll = 0;
  return async (config: InternalAxiosRequestConfig) => {
    const job =
      config.method === 'post'
        ? runningJob
        : jobSequence[Math.min(poll++, jobSequence.length - 1)];
    return {
      data: { success: true, data: job },
      status: 200,
      statusText: 'OK',
      headers: {},
      config,
    } as unknown as ReturnType<AxiosAdapter>;
  };
}

afterEach(() => {
  delete api.defaults.adapter;
  vi.useRealTimers();
});

describe('runSignalGenerationAndWait', () => {
  it('returns as soon as the job leaves RUNNING', async () => {
    api.defaults.adapter = fakeAdapter([runningJob, doneJob]);

    const result = await runSignalGenerationAndWait();

    expect(result).toEqual(doneJob);
  });

  it('throws after the 15-minute cap when the job stays RUNNING', async () => {
    vi.useFakeTimers();
    api.defaults.adapter = fakeAdapter([runningJob]);

    const promise = runSignalGenerationAndWait();
    // Attach the rejection handler before advancing timers so the timeout
    // rejection is never observed as unhandled.
    const assertion = expect(promise).rejects.toThrow('timed out after 15 minutes');
    await act(async () => {
      await vi.advanceTimersByTimeAsync(15 * 60 * 1000 + 100);
    });

    await assertion;
  });

  it('reports intermediate statuses through the callback', async () => {
    const onStatus = vi.fn();
    api.defaults.adapter = fakeAdapter([runningJob, doneJob]);

    await runSignalGenerationAndWait(onStatus);

    expect(onStatus).toHaveBeenCalledTimes(2);
    expect(onStatus).toHaveBeenNthCalledWith(1, runningJob);
    expect(onStatus).toHaveBeenNthCalledWith(2, doneJob);
  });
});