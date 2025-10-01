import { Injectable, Signal, WritableSignal, signal } from '@angular/core';
import { Observable, Subject, filter, map, shareReplay } from 'rxjs';
import { getWebSocketBaseUrl } from './orchestrator-config';
import type {
  AlertEvent,
  ExecutionEvent,
  PriceEvent,
  PricePoint,
  SignalEvent,
  StreamChannel,
  StreamEvent
} from '../models/stream-events.model';

type ConnectionState = 'idle' | 'connecting' | 'open' | 'closed' | 'error';

const RECONNECT_DELAY_MS = 5_000;

@Injectable({ providedIn: 'root' })
export class EventStreamService {
  private readonly eventsSubject = new Subject<StreamEvent>();
  private connection?: WebSocket;
  private reconnectHandle?: number;

  private readonly stateSignal: WritableSignal<ConnectionState> = signal('idle');
  private readonly lastErrorSignal: WritableSignal<string | null> = signal(null);

  readonly events$: Observable<StreamEvent> = this.eventsSubject.asObservable().pipe(shareReplay(1));

  readonly signals$: Observable<SignalEvent> = this.events$.pipe(
    filter((event): event is SignalEvent => event.channel === 'signals')
  );

  readonly executions$: Observable<ExecutionEvent> = this.events$.pipe(
    filter((event): event is ExecutionEvent => event.channel === 'executions')
  );

  readonly alerts$: Observable<AlertEvent> = this.events$.pipe(
    filter((event): event is AlertEvent => event.channel === 'alerts')
  );

  readonly prices$: Observable<PriceEvent> = this.events$.pipe(
    filter((event): event is PriceEvent => event.channel === 'prices')
  );

  readonly connectionState: Signal<ConnectionState> = this.stateSignal.asReadonly();
  readonly lastError: Signal<string | null> = this.lastErrorSignal.asReadonly();

  constructor() {
    if (typeof window === 'undefined') {
      return;
    }

    queueMicrotask(() => this.connect());
  }

  open(): void {
    if (typeof window === 'undefined') {
      return;
    }

    this.disposeConnection();
    this.connect();
  }

  close(): void {
    this.disposeConnection();
    this.stateSignal.set('closed');
  }

  private connect(): void {
    if (this.connection) {
      return;
    }

    this.stateSignal.set('connecting');
    this.lastErrorSignal.set(null);

    try {
      const url = this.resolveEndpoint('/ws');
      this.connection = new WebSocket(url);
      this.connection.addEventListener('open', () => {
        this.stateSignal.set('open');
      });

      this.connection.addEventListener('message', (event) => {
        this.handleMessage(event.data);
      });

      this.connection.addEventListener('close', () => {
        this.handleClose();
      });

      this.connection.addEventListener('error', (event) => {
        this.handleError(event);
      });
    } catch (error) {
      this.lastErrorSignal.set((error as Error).message ?? 'Unknown WebSocket error');
      this.scheduleReconnect();
    }
  }

  private handleMessage(data: unknown): void {
    try {
      const parsed: unknown = typeof data === 'string' ? JSON.parse(data) : data;
      const normalized = this.normalizeEvent(parsed);
      if (normalized) {
        this.eventsSubject.next(normalized);
      }
    } catch (error) {
      this.lastErrorSignal.set(`Failed to parse stream payload: ${(error as Error).message}`);
    }
  }

  private handleClose(): void {
    this.stateSignal.set('closed');
    this.disposeConnection(false);
    this.scheduleReconnect();
  }

  private handleError(event: Event): void {
    this.lastErrorSignal.set('WebSocket error');
    this.stateSignal.set('error');
    this.disposeConnection(false);
    this.scheduleReconnect();
  }

  private scheduleReconnect(): void {
    if (typeof window === 'undefined') {
      return;
    }

    if (this.reconnectHandle) {
      return;
    }

    this.reconnectHandle = window.setTimeout(() => {
      this.reconnectHandle = undefined;
      this.connect();
    }, RECONNECT_DELAY_MS);
  }

  private disposeConnection(clearHandle = true): void {
    if (clearHandle && this.reconnectHandle) {
      clearTimeout(this.reconnectHandle);
      this.reconnectHandle = undefined;
    }

    if (this.connection) {
      this.connection.close();
      this.connection = undefined;
    }
  }

  private normalizeEvent(value: unknown): StreamEvent | null {
    if (!isRecord(value)) {
      return null;
    }

  const channel = value['channel'];
    if (!isChannel(channel)) {
      return null;
    }

    switch (channel) {
      case 'signals':
        return toSignalEvent(value);
      case 'executions':
        return toExecutionEvent(value);
      case 'alerts':
        return toAlertEvent(value);
    case 'prices':
      return toPriceEvent(value);
    }
  }

  private resolveEndpoint(endpoint: string): string {
    if (/^wss?:\/\//i.test(endpoint)) {
      return endpoint;
    }

    if (!endpoint.startsWith('/')) {
      endpoint = `/${endpoint}`;
    }

    const base = getWebSocketBaseUrl();

    try {
      const url = new URL(endpoint, base);
      return url.toString();
    } catch {
      const normalized = endpoint.startsWith('/') ? endpoint : `/${endpoint}`;
      return `${base.replace(/\/$/, '')}${normalized}`;
    }
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isChannel(value: unknown): value is StreamChannel {
  return value === 'signals' || value === 'executions' || value === 'alerts' || value === 'prices';
}

function toSignalEvent(value: Record<string, unknown>): SignalEvent | null {
  const instrument = value['instrument'];
  const ts = value['ts'];
  const strategy = value['strategy'];
  const score = value['score'];
  const recommendedRaw = value['recommended'];
  const featuresRaw = value['features'];

  if (typeof instrument !== 'string' || typeof ts !== 'string' || typeof strategy !== 'string') {
    return null;
  }

  if (typeof score !== 'number' || !isRecord(recommendedRaw)) {
    return null;
  }

  const recommended = recommendedRaw;
  const side = recommended['side'];
  const entry = recommended['entry'];
  const stop = recommended['stop'];
  const target = recommended['target'];
  const riskPct = recommended['risk_pct'];

  if (side !== 'BUY' && side !== 'SELL') {
    return null;
  }

  if (typeof entry !== 'number' || typeof stop !== 'number') {
    return null;
  }

  if (typeof riskPct !== 'number') {
    return null;
  }

  return {
    channel: 'signals',
    instrument,
    ts,
    strategy: strategy as SignalEvent['strategy'],
    score,
    features: isRecord(featuresRaw) ? featuresRaw : undefined,
    recommended: {
      side,
      entry,
      stop,
      target: typeof target === 'number' ? target : null,
      risk_pct: riskPct
    }
  };
}

function toExecutionEvent(value: Record<string, unknown>): ExecutionEvent | null {
  const orderId = value['order_id'];
  const execId = value['exec_id'];
  const instrument = value['instrument'];
  const ts = value['ts'];
  const price = value['price'];
  const qty = value['qty'];
  const status = value['status'];
  const rawLiquidity = value['liquidity'];

  if (typeof orderId !== 'string' || typeof execId !== 'string' || typeof instrument !== 'string' || typeof ts !== 'string') {
    return null;
  }

  if (typeof price !== 'number' || typeof qty !== 'number' || typeof status !== 'string') {
    return null;
  }

  const liquidity = rawLiquidity === 'MAKER' || rawLiquidity === 'TAKER' ? rawLiquidity : rawLiquidity === null ? null : null;

  return {
    channel: 'executions',
    ts,
    order_id: orderId,
    exec_id: execId,
    instrument,
    price,
    qty,
    status: status as ExecutionEvent['status'],
    liquidity
  };
}

function toAlertEvent(value: Record<string, unknown>): AlertEvent | null {
  const ts = value['ts'];
  const type = value['type'];
  const severity = value['severity'];
  const detail = value['detail'];
  const context = value['context'];

  if (typeof ts !== 'string' || typeof type !== 'string' || typeof severity !== 'string') {
    return null;
  }

  return {
    channel: 'alerts',
    ts,
    type: type as AlertEvent['type'],
    severity: severity as AlertEvent['severity'],
    detail: typeof detail === 'string' ? detail : undefined,
    context: isRecord(context) ? context : undefined
  };
}

function toPriceEvent(value: Record<string, unknown>): PriceEvent | null {
  const instrument = value['instrument'];
  const ts = value['ts'];
  const interval = value['interval'];
  const pointsRaw = value['points'];

  if (typeof instrument !== 'string' || typeof ts !== 'string' || typeof interval !== 'string') {
    return null;
  }

  if (!Array.isArray(pointsRaw)) {
    return null;
  }

  const points: PricePoint[] = [];
  for (const item of pointsRaw) {
    if (!isRecord(item)) {
      continue;
    }

    const start = item['start'];
    const end = item['end'];
    const open = item['open'];
    const high = item['high'];
    const low = item['low'];
    const close = item['close'];
    const volume = item['volume'];

    if (typeof start !== 'string' || typeof end !== 'string') {
      continue;
    }

    if (
      typeof open !== 'number' ||
      typeof high !== 'number' ||
      typeof low !== 'number' ||
      typeof close !== 'number'
    ) {
      continue;
    }

    const normalizedVolume = typeof volume === 'number' ? volume : 0;

    points.push({
      start,
      end,
      open,
      high,
      low,
      close,
      volume: normalizedVolume
    });
  }

  return {
    channel: 'prices',
    ts,
    instrument,
    interval,
    points: points.sort((a, b) => a.end.localeCompare(b.end))
  };
}
