// API contract types. Shapes mirror the .NET DTOs exactly (see
// src/VoidCapital.Api/Modules/Portfolio/DTOs and Modules/MarketData/StockPrice.cs).
// Every endpoint returns the ApiResponse<T> envelope; unwrap .data.

export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  error: string | null;
  traceId: string | null;
}

export interface User {
  id: number;
  name: string;
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
  instrumentType?: string;
  expiry?: string | null;
  strike?: number | null;
}

export type TradeType = 'BUY' | 'SELL';

export interface Trade {
  id: number;
  symbol: string;
  type: TradeType;
  shares: number;
  price: number;
  total: number;
  commission: number;
  reason: string | null;
  timestamp: string;
}

export interface TradeRequest {
  symbol: string;
  shares: number;
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

export type SignalStatus = 'PENDING' | 'APPROVED' | 'REJECTED' | 'EXECUTED' | 'FAILED';

export interface Signal {
  id: number;
  date: string;
  symbol: string;
  action: 'BUY' | 'HOLD' | 'SELL';
  confidence: number;
  reason: string | null;
  modelName: string;
  status: SignalStatus;
  suggestedQuantity: number | null;
  entryPrice: number | null;
  targetPrice: number | null;
  stopLoss: number | null;
  failureReason: string | null;
  instrumentType?: string;
  expiry?: string | null;
  strike?: number | null;
}

export interface SignalBatchResult {
  id: number;
  success: boolean;
  error: string | null;
}

export interface ModelPerformance {
  modelName: string;
  totalSignals: number;
  resolvedSignals: number;
  hitTargetCount: number;
  winRate: number;
  avgReturn: number;
  bestReturn: number | null;
  worstReturn: number | null;
}

export type SignalOutcome = 'HIT_TARGET' | 'HIT_STOP' | 'EXPIRED';

export interface ResolvedSignal {
  signalId: number;
  date: string;
  symbol: string;
  action: 'BUY' | 'HOLD' | 'SELL';
  modelName: string;
  entryPrice: number;
  targetPrice: number | null;
  exitPrice: number | null;
  outcome: SignalOutcome;
  actualReturn: number | null;
  resolvedAt: string | null;
  evaluationDays: number;
}

export interface PagedResolvedSignals {
  items: ResolvedSignal[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ComparisonPortfolio {
  userId: number;
  name: string;
  cash: number;
  holdingsValue: number;
  totalValue: number;
  totalReturn: number;
  totalReturnPercent: number;
  startingBudget: number;
}

export interface ComparisonGap {
  leader: string;
  trailer: string;
  gapRupees: number;
  gapPercent: number;
}

export interface PortfolioComparison {
  portfolios: ComparisonPortfolio[];
  gaps: ComparisonGap[];
}

export interface UserBalance {
  userId: number;
  name: string;
  currentCash: number;
  totalValue: number;
  totalReturn: number;
  totalReturnPercent: number;
}

export interface AdminStatus {
  utcNow: string;
  pendingSignalCount: number;
  users: UserBalance[];
}

export interface SquareOffResult {
  userId: number;
  positionsSold: number;
  proceeds: number;
  remainingCash: number;
}

export type SignalJobStatus = 'RUNNING' | 'SUCCEEDED' | 'FAILED';

export interface SignalJob {
  jobId: number;
  status: SignalJobStatus;
  startedAt: string;
  finishedAt: string | null;
  message: string | null;
}
