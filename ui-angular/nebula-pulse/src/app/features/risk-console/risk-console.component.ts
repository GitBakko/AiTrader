import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe, NgClass, PercentPipe, TitleCasePipe } from '@angular/common';
import { KtuiBadgeDirective, KtuiCardDirective, KtuiPillDirective } from '../../ktui';
import { OrchestratorApiService } from '../../core/services/orchestrator-api.service';
import type { RiskLimits } from '../../core/models/risk.model';
import { StreamStore } from '../../core/state/stream-store';
import { Subscription } from 'rxjs';
import type { AlertEvent } from '../../core/models/stream-events.model';

interface KillSwitch {
  id: string;
  label: string;
  description: string;
  engaged: boolean;
}

@Component({
  selector: 'nebula-risk-console',
  standalone: true,
  imports: [DatePipe, PercentPipe, TitleCasePipe, NgClass, KtuiCardDirective, KtuiPillDirective, KtuiBadgeDirective],
  templateUrl: './risk-console.component.html',
  styleUrl: './risk-console.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RiskConsoleComponent {
  private readonly orchestrator = inject(OrchestratorApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly streamStore = inject(StreamStore);

  private limitsSubscription?: Subscription;

  protected readonly riskLimits = signal<RiskLimits | null>(null);
  protected readonly limitsError = signal<string | null>(null);
  protected readonly loadingLimits = signal(false);

  protected readonly killSwitches = signal<KillSwitch[]>([
    { id: 'strategies', label: 'Strategy host', description: 'Disable automated signal generation', engaged: false },
    { id: 'orders', label: 'Order routing', description: 'Block all new order submissions', engaged: false },
    { id: 'paper-broker', label: 'Paper broker', description: 'Pause fills and cancel working orders', engaged: false }
  ]);

  protected readonly latestAlert = this.streamStore.latestAlert;
  protected readonly connectionState = this.streamStore.connectionState;
  protected readonly alertHistory = computed(() => this.tail(this.streamStore.alerts(), 40));
  protected readonly streamActivity = this.streamStore.activitySnapshot;

  protected readonly circuitBreakerStatus = computed(() => {
    const alert = this.latestAlert();
    if (!alert) {
      return {
        status: 'Standby',
        tone: 'text-emerald-300',
        detail: 'No breaker events registered'
      } as const;
    }

    if (alert.type === 'DAILY_STOP_HIT') {
      return {
        status: 'TRIPPED',
        tone: 'text-rose-300',
        detail: alert.detail ?? 'Daily stop reached'
      } as const;
    }

    return {
      status: 'Monitoring',
      tone: 'text-amber-200',
      detail: alert.detail ?? 'Investigate latest alert'
    } as const;
  });

  constructor() {
    this.fetchRiskLimits();
    this.destroyRef.onDestroy(() => this.limitsSubscription?.unsubscribe());
  }

  refreshLimits(): void {
    this.fetchRiskLimits();
  }

  toggleSwitch(id: string): void {
    this.killSwitches.update((current) =>
      current.map((entry) =>
        entry.id === id ? { ...entry, engaged: !entry.engaged } : entry
      )
    );
  }

  protected alertSeverityBadge(severity: AlertEvent['severity']): 'info' | 'warning' | 'danger' {
    switch (severity) {
      case 'INFO':
        return 'info';
      case 'WARN':
        return 'warning';
      case 'CRIT':
      default:
        return 'danger';
    }
  }

  protected trackAlert(_: number, item: AlertEvent): string {
    return `${item.ts}-${item.type}`;
  }

  private tail<T>(collection: readonly T[], take: number): T[] {
    if (collection.length <= take) {
      return [...collection].reverse();
    }

    return [...collection.slice(collection.length - take)].reverse();
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
        this.limitsError.set((err as Error)?.message ?? 'Unable to fetch limits');
        this.loadingLimits.set(false);
      }
    });
  }
}
