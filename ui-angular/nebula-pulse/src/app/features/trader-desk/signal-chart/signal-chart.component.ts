import { ChangeDetectionStrategy, Component, Input, computed, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';

type TradeSide = 'BUY' | 'SELL';

export interface SignalChartPoint {
  ts: string;
  price: number;
}

interface LevelLine {
  key: 'entry' | 'stop' | 'target';
  y: number;
  color: string;
  value: number;
  label: string;
}

interface PlotPoint extends SignalChartPoint {
  x: number;
  y: number;
}

@Component({
  selector: 'nebula-signal-chart',
  standalone: true,
  imports: [DatePipe, DecimalPipe],
  templateUrl: './signal-chart.component.html',
  styleUrl: './signal-chart.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SignalChartComponent {
  private readonly pointsSignal = signal<SignalChartPoint[]>([]);
  private readonly entrySignal = signal<number | null>(null);
  private readonly stopSignal = signal<number | null>(null);
  private readonly targetSignal = signal<number | null>(null);
  private readonly sideSignal = signal<TradeSide | null>(null);

  @Input() set points(value: readonly SignalChartPoint[] | null | undefined) {
    if (!value?.length) {
      this.pointsSignal.set([]);
      return;
    }

    const normalized = value
      .map((point) => SignalChartComponent.normalizePoint(point))
      .filter((point): point is SignalChartPoint => point !== null)
      .sort((a, b) => a.ts.localeCompare(b.ts));

    this.pointsSignal.set(normalized);
  }

  @Input() set entry(value: number | null | undefined) {
    this.entrySignal.set(this.toNumeric(value));
  }

  @Input() set stop(value: number | null | undefined) {
    this.stopSignal.set(this.toNumeric(value));
  }

  @Input() set target(value: number | null | undefined) {
    this.targetSignal.set(this.toNumeric(value));
  }

  @Input() set side(value: TradeSide | null | undefined) {
    this.sideSignal.set(value ?? null);
  }

  protected readonly width = 360;
  protected readonly height = 180;
  protected readonly paddingX = 28;
  protected readonly paddingY = 18;
  protected readonly viewBox = `0 0 ${this.width} ${this.height}`;

  private readonly domain = computed(() => {
    const values: number[] = [];
    for (const point of this.pointsSignal()) {
      values.push(point.price);
    }

    for (const value of [this.entrySignal(), this.stopSignal(), this.targetSignal()]) {
      if (typeof value === 'number' && Number.isFinite(value)) {
        values.push(value);
      }
    }

    if (!values.length) {
      return { min: 0, max: 1, range: 1 } as const;
    }

    let min = Math.min(...values);
    let max = Math.max(...values);

    if (min === max) {
      const delta = Math.abs(min) > 0 ? Math.abs(min) * 0.05 : 1;
      min -= delta;
      max += delta;
    } else {
      const padding = (max - min) * 0.05;
      min -= padding;
      max += padding;
    }

    const range = max - min;
    return { min, max, range: range <= 0 ? 1 : range } as const;
  });

  protected readonly plottedPoints = computed<PlotPoint[]>(() => {
    const points = this.pointsSignal();
    if (!points.length) {
      return [];
    }

    const { min, range } = this.domain();
    const usableWidth = this.width - this.paddingX * 2;
    const usableHeight = this.height - this.paddingY * 2;
    const total = points.length;

    return points.map((point, index) => {
      const ratio = total <= 1 ? 0 : index / (total - 1);
      const x = this.paddingX + ratio * usableWidth;
      const normalized = (point.price - min) / range;
      const y = this.height - this.paddingY - normalized * usableHeight;
      return { ...point, x, y };
    });
  });

  protected readonly pricePath = computed(() => {
    const coords = this.plottedPoints();
    if (coords.length < 2) {
      return null;
    }

    return coords
      .map((coord, index) => `${index === 0 ? 'M' : 'L'} ${coord.x} ${coord.y}`)
      .join(' ');
  });

  protected readonly levels = computed<LevelLine[]>(() => {
    const levels: LevelLine[] = [];
    const entry = this.buildLevel('entry', this.entrySignal(), '#22d3ee', 'Entry');
    if (entry) {
      levels.push(entry);
    }

    const stop = this.buildLevel('stop', this.stopSignal(), '#fb7185', 'Stop');
    if (stop) {
      levels.push(stop);
    }

    const target = this.buildLevel('target', this.targetSignal(), '#34d399', 'Target');
    if (target) {
      levels.push(target);
    }

    return levels;
  });

  protected readonly latestPoint = computed(() => {
    const coords = this.plottedPoints();
    return coords.length ? coords[coords.length - 1] : null;
  });

  protected readonly timestamps = computed(() => {
    const points = this.pointsSignal();
    if (!points.length) {
      return null;
    }

    return { start: points[0].ts, end: points[points.length - 1].ts } as const;
  });

  protected readonly hasData = computed(() => this.pointsSignal().length > 0);
  protected readonly sideColor = computed(() => (this.sideSignal() === 'SELL' ? '#fb7185' : '#22d3ee'));

  private buildLevel(key: LevelLine['key'], value: number | null, color: string, label: string): LevelLine | null {
    if (typeof value !== 'number' || !Number.isFinite(value)) {
      return null;
    }

    const { min, range } = this.domain();
    const usableHeight = this.height - this.paddingY * 2;
    const normalized = (value - min) / range;
    const y = this.height - this.paddingY - normalized * usableHeight;

    return { key, color, value, y, label };
  }

  private toNumeric(value: number | null | undefined): number | null {
    if (typeof value === 'number' && Number.isFinite(value)) {
      return value;
    }

    return null;
  }

  private static normalizePoint(point: SignalChartPoint): SignalChartPoint | null {
    if (typeof point?.ts !== 'string') {
      return null;
    }

    if (typeof point.price !== 'number' || !Number.isFinite(point.price)) {
      return null;
    }

    return { ts: point.ts, price: point.price };
  }
}
