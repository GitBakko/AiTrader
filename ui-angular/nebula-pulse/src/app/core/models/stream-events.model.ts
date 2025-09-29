export type StreamChannel = 'signals' | 'executions' | 'alerts';

export interface BaseStreamEvent {
  channel: StreamChannel;
  ts: string;
}

export type StrategyCode = 'TPB_VWAP' | 'ORB_15' | 'VRB';

export type TradeSide = 'BUY' | 'SELL';

export interface SignalRecommendedPlan {
  side: TradeSide;
  entry: number;
  stop: number;
  target?: number | null;
  risk_pct: number;
}

export interface SignalEvent extends BaseStreamEvent {
  channel: 'signals';
  instrument: string;
  strategy: StrategyCode;
  score: number;
  features?: Record<string, unknown>;
  recommended: SignalRecommendedPlan;
}

export type ExecutionStatus = 'NEW' | 'PARTIAL' | 'FILLED' | 'CANCELLED' | 'REJECTED';
export type ExecutionLiquidity = 'MAKER' | 'TAKER' | null;

export interface ExecutionEvent extends BaseStreamEvent {
  channel: 'executions';
  order_id: string;
  exec_id: string;
  instrument: string;
  price: number;
  qty: number;
  status: ExecutionStatus;
  liquidity: ExecutionLiquidity;
}

export type AlertType = 'DAILY_STOP_HIT' | 'FEED_LOSS' | 'BROKER_REJECT' | 'SPREAD_SPIKE' | 'RATE_LIMIT';
export type AlertSeverity = 'INFO' | 'WARN' | 'CRIT';

export interface AlertEvent extends BaseStreamEvent {
  channel: 'alerts';
  type: AlertType;
  severity: AlertSeverity;
  detail?: string;
  context?: Record<string, unknown>;
}

export type StreamEvent = SignalEvent | ExecutionEvent | AlertEvent;
