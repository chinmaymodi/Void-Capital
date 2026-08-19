// Typed API client. One axios instance, interceptors for logging and error
// normalization, typed functions per endpoint (SRP: one place for the HTTP
// contract). All responses are ApiResponse<T> envelopes; callers get .data.

import axios from 'axios';
import type {
  AdminStatus,
  ApiResponse,
  Holding,
  ModelPerformance,
  PagedResolvedSignals,
  PagedTrades,
  PnlSnapshot,
  PortfolioComparison,
  PortfolioState,
  Settings,
  Signal,
  SignalBatchResult,
  SignalJob,
  SquareOffResult,
  Trade,
  TradeFilters,
  TradeRequest,
  User,
} from '../types';

const api = axios.create({
  baseURL: '/api/v1',
  timeout: 15000,
});

// Request logging in dev (console.debug per the D4 spec), plus the API key
// header (A1-class auth). The dashboard authenticates as admin via
// VITE_API_KEY (frontend/.env, gitignored); without it every request 401s.
api.interceptors.request.use((config) => {
  const apiKey = import.meta.env.VITE_API_KEY as string | undefined;
  if (apiKey) {
    config.headers['X-Api-Key'] = apiKey;
  } else if (import.meta.env.DEV) {
    console.warn('[api] VITE_API_KEY is not set; requests will be rejected (401)');
  }
  if (import.meta.env.DEV) {
    console.debug(`[api] ${config.method?.toUpperCase()} ${config.url}`, config.params ?? '');
  }
  return config;
});

// Response interceptor: unwrap the ApiResponse envelope on 2xx, and normalize
// errors so callers receive a real Error (so `err instanceof Error` works in
// every page handler) carrying the backend message and an attached status.
api.interceptors.response.use(
  (response) => response,
  (error) => {
    const status: number = error.response?.status ?? 0;
    // Backend errors are ApiResponse envelopes (see ExceptionMiddleware);
    // prefer .error, fall back to a generic message for network failures.
    const message: string =
      error.response?.data?.error ?? error.message ?? 'Request failed';
    const normalized = new Error(message) as Error & { status: number };
    normalized.status = status;
    return Promise.reject(normalized);
  },
);

async function unwrap<T>(request: Promise<{ data: ApiResponse<T> }>): Promise<T> {
  const { data } = await request;
  if (!data.success) {
    throw new Error(data.error ?? 'Request failed');
  }
  if (data.data === null) {
    throw new Error('Empty response');
  }
  return data.data;
}

export const USER_ID = 1; // Trader One (demo human). Fallback when no user is selected.

export function getUsers(): Promise<User[]> {
  return unwrap(api.get<ApiResponse<User[]>>(`/users`));
}

export function getPortfolio(userId: number = USER_ID): Promise<PortfolioState> {
  return unwrap(api.get<ApiResponse<PortfolioState>>(`/portfolio/${userId}`));
}

export function getPortfolioHistory(userId: number = USER_ID): Promise<PnlSnapshot[]> {
  return unwrap(api.get<ApiResponse<PnlSnapshot[]>>(`/portfolio/${userId}/history`));
}

export function getHoldings(userId: number = USER_ID): Promise<Holding[]> {
  return unwrap(api.get<ApiResponse<Holding[]>>(`/holdings/${userId}`));
}

export function buyStock(
  request: TradeRequest,
  userId: number = USER_ID,
): Promise<Trade> {
  return unwrap(api.post<ApiResponse<Trade>>(`/holdings/${userId}/buy`, request));
}

export function sellStock(
  request: TradeRequest,
  userId: number = USER_ID,
): Promise<Trade> {
  return unwrap(api.post<ApiResponse<Trade>>(`/holdings/${userId}/sell`, request));
}

export function getTrades(filters: TradeFilters = {}, userId: number = USER_ID): Promise<PagedTrades> {
  return unwrap(
    api.get<ApiResponse<PagedTrades>>(`/trades/${userId}`, {
      params: {
        page: filters.page ?? 1,
        pageSize: filters.pageSize ?? 20,
        symbol: filters.symbol || undefined,
        type: filters.type || undefined,
        from: filters.from || undefined,
        to: filters.to || undefined,
      },
    }),
  );
}

export function getSettings(userId: number = USER_ID): Promise<Settings> {
  return unwrap(api.get<ApiResponse<Settings>>(`/settings/${userId}`));
}

export function updateSettings(settings: Settings, userId: number = USER_ID): Promise<Settings> {
  return unwrap(
    api.put<ApiResponse<Settings>>(`/settings/${userId}`, {
      autoExecute: settings.autoExecute,
      minConfidence: settings.minConfidence,
      negativeLimit: settings.negativeLimit,
      interestRate: settings.interestRate,
      watchlist: settings.watchlist,
    }),
  );
}

// ---------- Signals (D7.1) ----------

export function getTodaySignals(userId: number = USER_ID): Promise<Signal[]> {
  return unwrap(api.get<ApiResponse<Signal[]>>(`/signals/today/${userId}`));
}

export function approveSignal(signalId: number): Promise<Signal> {
  return unwrap(api.post<ApiResponse<Signal>>(`/signals/${signalId}/approve`));
}

export function rejectSignal(signalId: number): Promise<Signal> {
  return unwrap(api.post<ApiResponse<Signal>>(`/signals/${signalId}/reject`));
}

export function batchApproveSignals(ids: number[]): Promise<SignalBatchResult[]> {
  return unwrap(api.post<ApiResponse<SignalBatchResult[]>>(`/signals/batch-approve`, { ids }));
}

export function batchRejectSignals(ids: number[]): Promise<SignalBatchResult[]> {
  return unwrap(api.post<ApiResponse<SignalBatchResult[]>>(`/signals/batch-reject`, { ids }));
}

// ---------- Performance (D7.3) ----------

export function getModelPerformance(): Promise<ModelPerformance[]> {
  return unwrap(api.get<ApiResponse<ModelPerformance[]>>(`/performance/models`));
}

export function getResolvedSignals(
  filters: { userId?: number; model?: string; page?: number; pageSize?: number } = {},
): Promise<PagedResolvedSignals> {
  return unwrap(
    api.get<ApiResponse<PagedResolvedSignals>>(`/performance/signals`, {
      params: {
        userId: filters.userId || undefined,
        model: filters.model || undefined,
        page: filters.page ?? 1,
        pageSize: filters.pageSize ?? 20,
      },
    }),
  );
}

export function getComparison(): Promise<PortfolioComparison> {
  return unwrap(api.get<ApiResponse<PortfolioComparison>>(`/performance/compare`));
}

// ---------- Admin (D7.2) ----------

export function getAdminStatus(): Promise<AdminStatus> {
  return unwrap(api.get<ApiResponse<AdminStatus>>(`/admin/status`));
}

export function getAdminSettings(userId: number): Promise<Settings> {
  return unwrap(api.get<ApiResponse<Settings>>(`/admin/settings/${userId}`));
}

export function updateAdminSettings(userId: number, settings: Settings): Promise<Settings> {
  return unwrap(
    api.put<ApiResponse<Settings>>(`/admin/settings/${userId}`, {
      autoExecute: settings.autoExecute,
      minConfidence: settings.minConfidence,
      negativeLimit: settings.negativeLimit,
      interestRate: settings.interestRate,
      watchlist: settings.watchlist,
    }),
  );
}

export function updateGlobalSettings(minConfidence: number, watchlist: string[]): Promise<Settings[]> {
  return unwrap(
    api.put<ApiResponse<Settings[]>>(`/admin/settings/global`, { minConfidence, watchlist }),
  );
}

export function squareOff(userId: number): Promise<SquareOffResult> {
  return unwrap(api.post<ApiResponse<SquareOffResult>>(`/admin/square-off/${userId}`));
}

export function runSignalGeneration(): Promise<SignalJob> {
  return unwrap(api.post<ApiResponse<SignalJob>>(`/admin/run-signals`));
}

export function getSignalJobStatus(jobId: number): Promise<SignalJob> {
  return unwrap(api.get<ApiResponse<SignalJob>>(`/admin/run-signals/${jobId}`));
}

/**
 * Kicks off signal generation and polls until the job leaves RUNNING.
 * Signal generation runs as a background job (the Python pipeline takes
 * minutes, beyond the 15s axios timeout), so the POST returns a job id
 * immediately and this helper polls the status endpoint every 2.5s.
 * Polling is capped at 15 minutes: a job that never leaves RUNNING (hung
 * pipeline, service restart) throws instead of polling forever.
 */
const POLL_INTERVAL_MS = 2500;
const MAX_WAIT_MS = 15 * 60 * 1000;

export async function runSignalGenerationAndWait(
  onStatus?: (job: SignalJob) => void,
): Promise<SignalJob> {
  const job = await runSignalGeneration();
  const deadline = Date.now() + MAX_WAIT_MS;
  for (;;) {
    const current = await getSignalJobStatus(job.jobId);
    onStatus?.(current);
    if (current.status !== 'RUNNING') return current;
    if (Date.now() >= deadline) {
      throw new Error('Signal generation timed out after 15 minutes');
    }
    await new Promise((resolve) => setTimeout(resolve, POLL_INTERVAL_MS));
  }
}

export default api;
