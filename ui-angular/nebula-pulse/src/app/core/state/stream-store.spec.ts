import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import type { AlertEvent, ExecutionEvent, PriceEvent, PricePoint, SignalEvent } from '../models/stream-events.model';
import { EventStreamService } from '../services/event-stream.service';
import { StreamStore } from './stream-store';

describe('StreamStore', () => {
  let store: StreamStore;
  let signalsSubject: Subject<SignalEvent>;
  let executionsSubject: Subject<ExecutionEvent>;
  let alertsSubject: Subject<AlertEvent>;
  let pricesSubject: Subject<PriceEvent>;

  class StubEventStreamService {
    readonly signals$ = (signalsSubject = new Subject<SignalEvent>());
    readonly executions$ = (executionsSubject = new Subject<ExecutionEvent>());
    readonly alerts$ = (alertsSubject = new Subject<AlertEvent>());
  readonly prices$ = (pricesSubject = new Subject<PriceEvent>());
    readonly connectionState = signal<'open'>('open');
    readonly lastError = signal<string | null>(null);
    open(): void {}
    close(): void {}
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        StreamStore,
        { provide: EventStreamService, useClass: StubEventStreamService }
      ]
    });

    store = TestBed.inject(StreamStore);
  });

  it('captures stream events into rolling buffers', () => {
    const signalEvent: SignalEvent = {
      channel: 'signals',
      ts: new Date().toISOString(),
      instrument: 'BTCUSDT',
      strategy: 'TPB_VWAP',
      score: 0.84,
      recommended: { side: 'BUY', entry: 62000, stop: 61150, risk_pct: 0.0035 }
    };

    const executionEvent: ExecutionEvent = {
      channel: 'executions',
      ts: new Date().toISOString(),
      order_id: 'o-1',
      exec_id: 'e-1',
      instrument: 'BTCUSDT',
      price: 62010,
      qty: 0.02,
      status: 'FILLED',
      liquidity: 'MAKER'
    };

    const alertEvent: AlertEvent = {
      channel: 'alerts',
      ts: new Date().toISOString(),
      type: 'RATE_LIMIT',
      severity: 'WARN',
      detail: 'Alpha Vantage quota half consumed'
    };

    const pricePoints: PricePoint[] = [
      {
        start: new Date(Date.now() - 60_000).toISOString(),
        end: new Date().toISOString(),
        open: 61980,
        high: 62050,
        low: 61940,
        close: 62010,
        volume: 12.5
      }
    ];

    const priceEvent: PriceEvent = {
      channel: 'prices',
      ts: new Date().toISOString(),
      instrument: 'BTCUSDT',
      interval: '1m',
      points: pricePoints
    };

    signalsSubject.next(signalEvent);
    executionsSubject.next(executionEvent);
    alertsSubject.next(alertEvent);
    pricesSubject.next(priceEvent);

    expect(store.signals().at(-1)).toEqual(signalEvent);
    expect(store.executions().at(-1)).toEqual(executionEvent);
    expect(store.alerts().at(-1)).toEqual(alertEvent);
    expect(store.getPriceSeries('BTCUSDT')).toEqual(pricePoints);
  });

  it('halts streams and clears cached data', () => {
    const closeSpy = spyOn(TestBed.inject(EventStreamService), 'close');

    signalsSubject.next({
      channel: 'signals',
      ts: new Date().toISOString(),
      instrument: 'ETHUSDT',
      strategy: 'TPB_VWAP',
      score: 0.65,
      recommended: { side: 'SELL', entry: 3100, stop: 3145, risk_pct: 0.0025 }
    });

    pricesSubject.next({
      channel: 'prices',
      ts: new Date().toISOString(),
      instrument: 'ETHUSDT',
      interval: '1m',
      points: [
        {
          start: new Date(Date.now() - 60_000).toISOString(),
          end: new Date().toISOString(),
          open: 3105,
          high: 3110,
          low: 3090,
          close: 3095,
          volume: 4.2
        }
      ]
    });

    store.halt();

    expect(closeSpy).toHaveBeenCalled();
    expect(store.signals().length).toBe(0);
    expect(store.executions().length).toBe(0);
    expect(store.alerts().length).toBe(0);
    expect(store.getPriceSeries('ETHUSDT').length).toBe(0);
  });
});
