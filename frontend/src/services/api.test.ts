// API error normalization (W1): the response interceptor must reject with a
// real Error carrying the backend message and an attached status, so page
// handlers' `err instanceof Error ? err.message : ...` branches work. The
// axios adapter is swapped per-test to throw axios-shaped errors and let the
// registered interceptor normalize them.

import { afterEach, describe, expect, it } from 'vitest';
import api from './api';

afterEach(() => {
  delete api.defaults.adapter;
});

describe('api error normalization', () => {
  it('rejects with a real Error carrying the backend message and status', async () => {
    api.defaults.adapter = async () => {
      throw {
        response: { status: 400, data: { error: 'Invalid symbol' } },
        message: 'Request failed with status code 400',
      };
    };

    const promise = api.get('/test');
    await expect(promise).rejects.toBeInstanceOf(Error);
    await expect(promise).rejects.toMatchObject({ message: 'Invalid symbol', status: 400 });
  });

  it('falls back to the axios message for network failures (status 0)', async () => {
    api.defaults.adapter = async () => {
      throw { message: 'Network Error' };
    };

    await expect(api.get('/test')).rejects.toMatchObject({
      message: 'Network Error',
      status: 0,
    });
  });

  it('falls back to a generic message when nothing is available', async () => {
    api.defaults.adapter = async () => {
      throw {};
    };

    await expect(api.get('/test')).rejects.toMatchObject({
      message: 'Request failed',
      status: 0,
    });
  });
});