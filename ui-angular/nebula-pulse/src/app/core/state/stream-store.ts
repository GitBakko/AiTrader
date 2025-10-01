import { DestroyRef, Injectable, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { EventStreamService } from '../services/event-stream.service';
import type { AlertEvent, ExecutionEvent, PriceEvent, PricePoint, SignalEvent } from '../models/stream-events.model';

const MAX_SIGNAL_HISTORY = 200;
const MAX_EXECUTION_HISTORY = 200;
const MAX_ALERT_HISTORY = 100;
const MAX_PRICE_HISTORY = 720;

@Injectable({ providedIn: 'root' })
export class StreamStore {
  private readonly events = inject(EventStreamService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly signalsStore: WritableSignal<SignalEvent[]> = signal([]);
  private readonly executionsStore: WritableSignal<ExecutionEvent[]> = signal([]);
  private readonly alertsStore: WritableSignal<AlertEvent[]> = signal([]);
  private readonly priceSeriesStore: WritableSignal<Record<string, PricePoint[]>> = signal({});

  readonly signals: Signal<SignalEvent[]> = this.signalsStore.asReadonly();
  readonly executions: Signal<ExecutionEvent[]> = this.executionsStore.asReadonly();
  readonly alerts: Signal<AlertEvent[]> = this.alertsStore.asReadonly();
  readonly priceSeries: Signal<Record<string, PricePoint[]>> = this.priceSeriesStore.asReadonly();

  readonly latestSignal = computed(() => this.pickLast(this.signals()));
  readonly latestExecution = computed(() => this.pickLast(this.executions()));
  readonly latestAlert = computed(() => this.pickLast(this.alerts()));

  readonly connectionState = this.events.connectionState;
  readonly lastError = this.events.lastError;

  readonly signalCount = computed(() => this.signals().length);
  readonly executionCount = computed(() => this.executions().length);
  readonly alertCount = computed(() => this.alerts().length);

  readonly lastSignalTs = computed(() => this.pickLastTimestamp(this.signals()));
  readonly lastExecutionTs = computed(() => this.pickLastTimestamp(this.executions()));
  readonly lastAlertTs = computed(() => this.pickLastTimestamp(this.alerts()));

  readonly activitySnapshot = computed(() => ({
    signals: {
      count: this.signalCount(),
      lastTs: this.lastSignalTs()
    },
    executions: {
      count: this.executionCount(),
      lastTs: this.lastExecutionTs()
    },
    alerts: {
      count: this.alertCount(),
      lastTs: this.lastAlertTs()
    },
    lastUpdatedTs: this.resolveLatestTimestamp([
      this.lastSignalTs(),
      this.lastExecutionTs(),
      this.lastAlertTs()
    ])
  } as const));

  constructor() {
    const signalSub = this.events.signals$.subscribe((event) => {
      this.pushEvent(this.signalsStore, event, MAX_SIGNAL_HISTORY);
    });

    const executionSub = this.events.executions$.subscribe((event) => {
      this.pushEvent(this.executionsStore, event, MAX_EXECUTION_HISTORY);
    });

    const alertSub = this.events.alerts$.subscribe((event) => {
      this.pushEvent(this.alertsStore, event, MAX_ALERT_HISTORY);
    });

    const priceSub = this.events.prices$.subscribe((event) => {
      this.ingestPrices(event);
    });

    this.destroyRef.onDestroy(() => {
      signalSub.unsubscribe();
      executionSub.unsubscribe();
      alertSub.unsubscribe();
      priceSub.unsubscribe();
    });
  }

  reset(): void {
    this.signalsStore.set([]);
    this.executionsStore.set([]);
    this.alertsStore.set([]);
    this.priceSeriesStore.set({});
  }

  reconnect(): void {
    this.events.open();
  }

  getPriceSeries(instrument: string): PricePoint[] {
    const series = this.priceSeries();
    return series[instrument] ?? [];
  }

  halt(): void {
    this.events.close();
    this.reset();
  }

  private pushEvent<T>(store: WritableSignal<T[]>, event: T, limit: number): void {
    store.update((current) => {
      const next = [...current, event];
      if (next.length > limit) {
        next.splice(0, next.length - limit);
      }
      return next;
    });
  }

  private pickLast<T>(collection: T[]): T | null {
    return collection.length ? collection[collection.length - 1] ?? null : null;
  }

  private pickLastTimestamp<T extends { ts: string }>(collection: T[]): string | null {
    const last = this.pickLast(collection);
    return last ? last.ts ?? null : null;
  }

  private resolveLatestTimestamp(values: Array<string | null>): string | null {
    const timestamps = values.filter((value): value is string => typeof value === 'string');
    if (!timestamps.length) {
      return null;
    }

    return timestamps.reduce((latest, current) => (current > latest ? current : latest));
  }

  private ingestPrices(event: PriceEvent): void {
    const sanitized = this.sanitizePoints(event.points);
    if (!sanitized.length) {
      return;
    }

    this.priceSeriesStore.update((current) => {
      const next = { ...current };
      next[event.instrument] = this.clampHistory(sanitized, MAX_PRICE_HISTORY);
      return next;
    });
  }

  private sanitizePoints(points: PricePoint[]): PricePoint[] {
    const seen = new Set<string>();
    const deduped: PricePoint[] = [];

    for (const point of points) {
      const key = `${point.start}-${point.end}`;
      if (seen.has(key)) {
        continue;
      }

      seen.add(key);
      deduped.push(point);
    }

    deduped.sort((a, b) => a.end.localeCompare(b.end));
    return deduped;
  }

  private clampHistory(points: PricePoint[], limit: number): PricePoint[] {
    if (points.length <= limit) {
      return [...points];
    }

    return points.slice(points.length - limit);
  }
}
