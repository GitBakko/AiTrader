import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe, NgClass, PercentPipe } from '@angular/common';
import { KtuiBadgeDirective, KtuiCardDirective, KtuiPillDirective } from '../../ktui';
import { StreamStore } from '../../core/state/stream-store';
import type { SignalEvent } from '../../core/models/stream-events.model';

interface StrategyProfile {
  code: string;
  description: string;
  status: 'idle' | 'running' | 'completed';
}

interface StrategyTelemetry extends StrategyProfile {
  signals: number;
  lastSignalTs: string | null;
  avgScore: number | null;
  avgRisk: number | null;
}

@Component({
  selector: 'nebula-backtest-lab',
  standalone: true,
  imports: [DatePipe, DecimalPipe, NgClass, PercentPipe, KtuiBadgeDirective, KtuiCardDirective, KtuiPillDirective],
  templateUrl: './backtest-lab.component.html',
  styleUrl: './backtest-lab.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BacktestLabComponent {
  private readonly streamStore = inject(StreamStore);

  private readonly strategyState = signal<StrategyProfile[]>([
    { code: 'TPB_VWAP', description: 'VWAP pullbacks on crypto majors', status: 'idle' },
    { code: 'ORB_15', description: 'Opening range breakout on US indices', status: 'idle' },
    { code: 'VRB', description: 'Volatility reversion bands on FX majors', status: 'idle' }
  ]);

  protected readonly strategies = computed<StrategyTelemetry[]>(() => {
    const definitions = this.strategyState();
    const signals = this.streamStore.signals();
    const metrics = this.accumulateStrategyMetrics(signals);

    return definitions.map((entry) => {
      const metric = metrics.get(entry.code) ?? { count: 0, sumScore: 0, sumRisk: 0, lastTs: null };
      const avgScore = metric.count ? metric.sumScore / metric.count : null;
      const avgRisk = metric.count ? metric.sumRisk / metric.count : null;

      return {
        ...entry,
        signals: metric.count,
        lastSignalTs: metric.lastTs,
        avgScore,
        avgRisk
      } satisfies StrategyTelemetry;
    });
  });

  protected readonly latestSignals = computed(() => this.tail(this.streamStore.signals(), 15));
  protected readonly signalTotal = this.streamStore.signalCount;
  protected readonly executionTotal = this.streamStore.executionCount;
  protected readonly lastStreamTs = computed(() => this.streamStore.activitySnapshot().lastUpdatedTs);

  protected readonly anyRunning = computed(() => this.strategyState().some((item) => item.status === 'running'));

  launchSimulation(code: string): void {
    this.strategyState.update((current) =>
      current.map((entry) =>
        entry.code === code ? { ...entry, status: entry.status === 'running' ? 'idle' : 'running' } : entry
      )
    );
  }

  protected trackSignal(_: number, item: SignalEvent): string {
    return `${item.ts}-${item.instrument}-${item.strategy}`;
  }

  protected scoreTone(score: number | null): string {
    if (score === null) {
      return 'text-slate-400';
    }

    if (score >= 0.75) {
      return 'text-emerald-300';
    }

    if (score >= 0.5) {
      return 'text-amber-200';
    }

    return 'text-rose-200';
  }

  protected riskTone(risk: number | null): string {
    if (risk === null) {
      return 'text-slate-400';
    }

    if (risk > 0.35) {
      return 'text-rose-200';
    }

    if (risk > 0.25) {
      return 'text-amber-200';
    }

    return 'text-emerald-300';
  }

  private accumulateStrategyMetrics(signals: readonly SignalEvent[]): Map<string, { count: number; sumScore: number; sumRisk: number; lastTs: string | null }> {
    const accumulator = new Map<string, { count: number; sumScore: number; sumRisk: number; lastTs: string | null }>();
    for (const event of signals) {
      const current = accumulator.get(event.strategy) ?? { count: 0, sumScore: 0, sumRisk: 0, lastTs: null };
      current.count += 1;
      current.sumScore += event.score ?? 0;
      current.sumRisk += event.recommended?.risk_pct ?? 0;
      current.lastTs = !current.lastTs || current.lastTs < event.ts ? event.ts : current.lastTs;
      accumulator.set(event.strategy, current);
    }
    return accumulator;
  }

  private tail<T>(collection: readonly T[], take: number): T[] {
    if (collection.length <= take) {
      return [...collection].reverse();
    }

    return [...collection.slice(collection.length - take)].reverse();
  }
}
