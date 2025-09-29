import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NgClass, TitleCasePipe } from '@angular/common';
import { StreamStore } from '../core/state/stream-store';

interface NavigationLink {
  label: string;
  path: string;
  description: string;
}

@Component({
  selector: 'nebula-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgClass, TitleCasePipe],
  templateUrl: './nebula-shell.component.html',
  styleUrl: './nebula-shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class NebulaShellComponent {
  private readonly streamStore = inject(StreamStore);

  protected readonly links: NavigationLink[] = [
    { label: 'Trader Desk', path: '/desk', description: 'Live signals & executions' },
    { label: 'Risk Console', path: '/risk', description: 'Limits, kill switches, circuit breakers' },
    { label: 'Backtest Lab', path: '/backtest', description: 'Walk-forward analytics (coming soon)' }
  ];

  protected readonly connectionState = this.streamStore.connectionState;
  protected readonly lastError = this.streamStore.lastError;
  protected readonly latestAlert = this.streamStore.latestAlert;

  protected readonly isConnected = computed(() => this.connectionState() === 'open');
  protected readonly isStale = computed(() => {
    const state = this.connectionState();
    return state === 'closed' || state === 'error';
  });

  reconnect(): void {
    this.streamStore.reconnect();
  }
}
