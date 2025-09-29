import { DestroyRef, Injectable, Signal, WritableSignal, computed, inject, signal } from '@angular/core';
import { EventStreamService } from '../services/event-stream.service';
import type { AlertEvent, ExecutionEvent, SignalEvent } from '../models/stream-events.model';

const MAX_SIGNAL_HISTORY = 200;
const MAX_EXECUTION_HISTORY = 200;
const MAX_ALERT_HISTORY = 100;

@Injectable({ providedIn: 'root' })
export class StreamStore {
  private readonly events = inject(EventStreamService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly signalsStore: WritableSignal<SignalEvent[]> = signal([]);
  private readonly executionsStore: WritableSignal<ExecutionEvent[]> = signal([]);
  private readonly alertsStore: WritableSignal<AlertEvent[]> = signal([]);

  readonly signals: Signal<SignalEvent[]> = this.signalsStore.asReadonly();
  readonly executions: Signal<ExecutionEvent[]> = this.executionsStore.asReadonly();
  readonly alerts: Signal<AlertEvent[]> = this.alertsStore.asReadonly();

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

    this.destroyRef.onDestroy(() => {
      signalSub.unsubscribe();
      executionSub.unsubscribe();
      alertSub.unsubscribe();
    });
  }

  reset(): void {
    this.signalsStore.set([]);
    this.executionsStore.set([]);
    this.alertsStore.set([]);
  }

  reconnect(): void {
    this.events.open();
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
}
