import type { TradeSide } from './stream-events.model';

export type OrderType = 'MKT' | 'LMT' | 'STP' | 'STP_LMT';
export type TimeInForce = 'DAY' | 'GTC';

export interface OrderCommandRequest {
  instrument: string;
  side: TradeSide;
  qty: number;
  order_type: OrderType;
  limit_price?: number | null;
  stop_price?: number | null;
  tif?: TimeInForce;
  bracket?: {
    take_profit?: number | null;
    stop_loss?: number | null;
  } | null;
}
