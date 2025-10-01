import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe, NgClass, PercentPipe } from '@angular/common';
import { KtuiBadgeDirective, KtuiCardDirective, KtuiPillDirective } from '../../ktui';
import { Subscription } from 'rxjs';
import { StreamStore } from '../../core/state/stream-store';
import { OrchestratorApiService } from '../../core/services/orchestrator-api.service';
import type { RiskLimits } from '../../core/models/risk.model';
import type { ExecutionEvent, SignalEvent } from '../../core/models/stream-events.model';
import { SignalChartComponent, type SignalChartPoint } from './signal-chart/signal-chart.component';

@Component({
  selector: 'nebula-trader-desk',
  standalone: true,
  imports: [DatePipe, DecimalPipe, PercentPipe, NgClass, KtuiCardDirective, KtuiBadgeDirective, KtuiPillDirective, SignalChartComponent],
  templateUrl: './trader-desk.component.html',
  styleUrl: './trader-desk.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TraderDeskComponent {
  private readonly streamStore = inject(StreamStore);
  private readonly orchestrator = inject(OrchestratorApiService);
  private readonly destroyRef = inject(DestroyRef);
  private limitsSubscription?: Subscription;

  protected readonly signalRows = computed(() => this.tail(this.streamStore.signals(), 30));
  protected readonly executionRows = computed(() => this.tail(this.streamStore.executions(), 50));

  protected readonly selectedInstrument = signal<string | null>(null);

  protected readonly signalByInstrument = computed(() => {
    const map = new Map<string, SignalEvent[]>();
    for (const signal of this.signalRows()) {
      const existing = map.get(signal.instrument);
      if (existing) {
        existing.push(signal);
      } else {
        map.set(signal.instrument, [signal]);
      }
    }
    return map;
  });

  protected readonly instrumentOptions = computed(() => Array.from(this.signalByInstrument().keys()));

  protected readonly selectedChartData = computed(() => {
    const instrument = this.selectedInstrument();
    if (!instrument) {
      return null;
    }

    const history = this.signalByInstrument().get(instrument);
    const latestSignal = history?.[0];
    if (!latestSignal) {
      return null;
    }

    const priceSeries = this.streamStore.getPriceSeries(instrument);
    if (!priceSeries.length) {
      return null;
    }

    const points: SignalChartPoint[] = priceSeries.map((point) => ({
      ts: point.end,
      price: point.close
    }));

    if (!points.length) {
      return null;
    }

    return {
      instrument,
      points,
      entry: latestSignal.recommended.entry,
      stop: latestSignal.recommended.stop,
      target: latestSignal.recommended.target ?? null,
      side: latestSignal.recommended.side
    } as const;
  });

  protected readonly riskLimits = signal<RiskLimits | null>(null);
  protected readonly limitsError = signal<string | null>(null);
  protected readonly loadingLimits = signal(false);
  protected readonly panicState = signal<'idle' | 'armed' | 'tripped'>('idle');
  protected readonly panicFeedback = signal<string | null>(null);

  private readonly latestSignal = this.streamStore.latestSignal;

  protected readonly riskUsage = computed(() => {
    const limits = this.riskLimits();
    const recent = this.latestSignal();

    if (!limits || !recent?.recommended?.risk_pct) {
      return null;
    }

    const perTradeLimit = Number(limits.per_trade_pct ?? 0);
    const suggestedRisk = Number(recent.recommended.risk_pct ?? 0);

    if (!perTradeLimit || !Number.isFinite(perTradeLimit)) {
      return null;
    }

    const ratio = suggestedRisk / perTradeLimit;
    const width = Math.max(0, Math.min(ratio * 100, 100));
    const percent = Math.round(ratio * 100);

    return {
      ratio,
      width,
      percent,
      used: suggestedRisk,
      capacity: perTradeLimit,
      overLimit: ratio > 1
    } as const;
  });

  constructor() {
    this.fetchRiskLimits();
    this.destroyRef.onDestroy(() => this.limitsSubscription?.unsubscribe());

    effect(() => {
      const instruments = this.instrumentOptions();
      const selected = this.selectedInstrument();

      if (!instruments.length) {
        if (selected !== null) {
          this.selectedInstrument.set(null);
        }
        return;
      }

      if (!selected || !instruments.includes(selected)) {
        this.selectedInstrument.set(instruments[0]);
      }
    });
  }

  refreshLimits(): void {
    this.fetchRiskLimits();
  }

  armPanic(): void {
    this.panicState.set('armed');
    this.panicFeedback.set(null);
  }

  cancelPanic(): void {
    this.panicState.set('idle');
    this.panicFeedback.set(null);
  }

  triggerPanic(): void {
    this.streamStore.halt();
    this.panicState.set('tripped');
    this.panicFeedback.set('Streams halted locally. Confirm orchestrator kill switch manually.');
  }

  protected scoreColor(score: number): string {
    if (score >= 0.75) {
      return 'text-emerald-300';
    }
    if (score >= 0.5) {
      return 'text-amber-200';
    }
    return 'text-rose-200';
  }

  protected selectInstrument(instrument: string): void {
    this.selectedInstrument.set(instrument);
  }

  private fetchRiskLimits(): void {
    this.loadingLimits.set(true);
    this.limitsError.set(null);

    this.limitsSubscription?.unsubscribe();

    this.limitsSubscription = this.orchestrator.getRiskLimits().subscribe({
      next: (limits) => {
        this.riskLimits.set(limits);
        this.loadingLimits.set(false);
      },
      error: (err: unknown) => {
        this.limitsError.set((err as Error)?.message ?? 'Unable to load risk limits');
        this.loadingLimits.set(false);
      }
    });
  }

  private tail<T>(collection: readonly T[], take: number): T[] {
    if (collection.length <= take) {
      return [...collection].reverse();
    }

    return [...collection.slice(collection.length - take)].reverse();
  }

  protected trackSignal(_: number, item: SignalEvent): string {
    return `${item.ts}-${item.instrument}-${item.strategy}`;
  }

  protected trackExecution(_: number, item: ExecutionEvent): string {
    return item.exec_id;
  }
}
