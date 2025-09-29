import type { TradeSide } from './stream-events.model';

export interface RiskLimits {
  per_trade_pct: number;
  daily_stop_pct: number;
  weekly_stop_pct: number;
  [key: string]: unknown;
}

export interface RiskDecision {
  allowed: boolean;
  reason?: string | null;
  allowedQty?: number | null;
}

export interface TradeIntentRequest {
  instrument: string;
  side: TradeSide;
  intendedQty: number;
  entry: number;
  stop: number;
  strategy: string;
}
