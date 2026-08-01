// API contract types. Shapes mirror the .NET DTOs exactly (see
// src/VoidCapital.Api/Modules/Portfolio/DTOs and Modules/MarketData/StockPrice.cs).
// Every endpoint returns the ApiResponse<T> envelope; unwrap .data.

export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  error: string | null;
  traceId: string | null;
}

export interface PortfolioState {
  cash: number;
  holdingsValue: number;
  totalValue: number;
}

export interface PnlSnapshot {
  id: number;
  userId: number;
  date: string; // "2026-07-31" (DateOnly)
  portfolioValue: number;
  cashValue: number;
  holdingsValue: number;
}

export interface Holding {
  id: number;
  symbol: string;
  shares: number;
  avgBuyPrice: number;
  currentPrice: number;
  unrealizedPnl: number;
  percentOfPortfolio: number;
}

export type TradeType = 'BUY' | 'SELL';

export interface Trade {
  id: number;
  symbol: string;
  type: TradeType;
  shares: number;
  price: number;
  total: number;
  reason: string | null;
  timestamp: string;
}

export interface TradeRequest {
  symbol: string;
  shares: number;
}

export interface StockPrice {
  symbol: string;
  date: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface Settings {
  id: number;
  userId: number;
  autoExecute: boolean;
  minConfidence: number;
  negativeLimit: number;
  interestRate: number;
  watchlist: string[];
}

export interface TradeFilters {
  page?: number;
  pageSize?: number;
  symbol?: string;
  type?: TradeType | '';
  from?: string;
  to?: string;
}

export interface PagedTrades {
  items: Trade[];
  total: number;
  page: number;
  pageSize: number;
}
