// Typed API client. One axios instance, interceptors for logging and error
// normalization, typed functions per endpoint (SRP: one place for the HTTP
// contract). All responses are ApiResponse<T> envelopes; callers get .data.

import axios from 'axios';
import type {
  ApiResponse,
  Holding,
  PnlSnapshot,
  PortfolioState,
  Settings,
  StockPrice,
  Trade,
  TradeRequest,
  TradeFilters,
  PagedTrades,
} from '../types';

const api = axios.create({
  baseURL: '/api/v1',
  timeout: 15000,
});

// Request logging in dev (console.debug per the D4 spec).
api.interceptors.request.use((config) => {
  if (import.meta.env.DEV) {
    console.debug(`[api] ${config.method?.toUpperCase()} ${config.url}`, config.params ?? '');
  }
  return config;
});

// Response interceptor: unwrap the ApiResponse envelope on 2xx, and normalize
// errors so callers receive { message, status } instead of raw axios errors.
api.interceptors.response.use(
  (response) => response,
  (error) => {
    const status: number = error.response?.status ?? 0;
    // Backend errors are ApiResponse envelopes (see ExceptionMiddleware);
    // prefer .error, fall back to a generic message for network failures.
    const message: string =
      error.response?.data?.error ?? error.message ?? 'Request failed';
    return Promise.reject({ message, status });
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

const USER_ID = 1; // Trader One (demo). User picker is a later ticket.

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

export function getStockPrice(symbol: string): Promise<number> {
  return unwrap(api.get<ApiResponse<number>>(`/market/${symbol}/price`));
}

export function getStockHistory(symbol: string): Promise<StockPrice[]> {
  return unwrap(api.get<ApiResponse<StockPrice[]>>(`/market/${symbol}/history`));
}

export function getTrades(filters: TradeFilters = {}): Promise<PagedTrades> {
  return unwrap(
    api.get<ApiResponse<PagedTrades>>(`/trades/${USER_ID}`, {
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

export function getSettings(): Promise<Settings> {
  return unwrap(api.get<ApiResponse<Settings>>(`/settings/${USER_ID}`));
}

export function updateSettings(settings: Settings): Promise<Settings> {
  return unwrap(
    api.put<ApiResponse<Settings>>(`/settings/${USER_ID}`, {
      autoExecute: settings.autoExecute,
      minConfidence: settings.minConfidence,
      negativeLimit: settings.negativeLimit,
      interestRate: settings.interestRate,
      watchlist: settings.watchlist,
    }),
  );
}

export default api;
