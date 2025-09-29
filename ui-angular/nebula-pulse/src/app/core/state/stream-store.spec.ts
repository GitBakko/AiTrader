import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import type { AlertEvent, ExecutionEvent, SignalEvent } from '../models/stream-events.model';
import { EventStreamService } from '../services/event-stream.service';
import { StreamStore } from './stream-store';

describe('StreamStore', () => {
  let store: StreamStore;
  let signalsSubject: Subject<SignalEvent>;
  let executionsSubject: Subject<ExecutionEvent>;
  let alertsSubject: Subject<AlertEvent>;

  class StubEventStreamService {
    readonly signals$ = (signalsSubject = new Subject<SignalEvent>());
    readonly executions$ = (executionsSubject = new Subject<ExecutionEvent>());
    readonly alerts$ = (alertsSubject = new Subject<AlertEvent>());
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

    signalsSubject.next(signalEvent);
    executionsSubject.next(executionEvent);
    alertsSubject.next(alertEvent);

    expect(store.signals().at(-1)).toEqual(signalEvent);
    expect(store.executions().at(-1)).toEqual(executionEvent);
    expect(store.alerts().at(-1)).toEqual(alertEvent);
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

    store.halt();

    expect(closeSpy).toHaveBeenCalled();
    expect(store.signals().length).toBe(0);
    expect(store.executions().length).toBe(0);
    expect(store.alerts().length).toBe(0);
  });
});
