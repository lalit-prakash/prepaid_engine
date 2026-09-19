import { Component, ElementRef, OnDestroy, afterNextRender, computed, inject, input, signal } from '@angular/core';

/**
 * A single-series area/line chart drawn as SVG (no charting dependency). It plots only the points
 * it is given, never interpolating data that was not supplied, and shows a value tooltip on hover
 * or keyboard focus of a point. An empty input renders a plain "no data" note. Every chart also
 * offers its numbers as a table so it is not the only way to read the data.
 */
@Component({
  selector: 'pe-line-chart',
  template: `
    @if (labels().length === 0) {
      <p class="empty">{{ emptyMessage() }}</p>
    } @else {
      <svg class="chart" [attr.width]="width()" [attr.height]="height" [attr.viewBox]="'0 0 ' + width() + ' ' + height" role="img" [attr.aria-label]="ariaLabel()">
        <defs>
          <linearGradient [attr.id]="gradientId" x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" [attr.stop-color]="color()" stop-opacity="0.28" />
            <stop offset="100%" [attr.stop-color]="color()" stop-opacity="0.02" />
          </linearGradient>
        </defs>
        @for (tick of ticks(); track tick.value) {
          <line [attr.x1]="left" [attr.x2]="width() - right" [attr.y1]="tick.y" [attr.y2]="tick.y" class="grid" />
          <text [attr.x]="left - 8" [attr.y]="tick.y + 4" class="axis" text-anchor="end">{{ tick.text }}</text>
        }
        @if (points().length > 1) {
          <path [attr.d]="areaPath()" [attr.fill]="'url(#' + gradientId + ')'" />
          <path [attr.d]="linePath()" fill="none" [attr.stroke]="color()" stroke-width="2.2" stroke-linejoin="round" stroke-linecap="round" />
        }
        @for (label of xLabels(); track label.x) {
          <text [attr.x]="label.x" [attr.y]="height - 8" class="axis" text-anchor="middle">{{ label.text }}</text>
        }
        @for (p of points(); track p.i) {
          <circle [attr.cx]="p.x" [attr.cy]="p.y" [attr.r]="active() === p.i ? 5 : 3.2" [attr.fill]="active() === p.i ? color() : '#fff'"
            [attr.stroke]="color()" stroke-width="2" tabindex="0" class="point"
            (mouseenter)="active.set(p.i)" (mouseleave)="active.set(null)" (focus)="active.set(p.i)" (blur)="active.set(null)">
            <title>{{ p.label }}: {{ p.text }}</title>
          </circle>
        }
      </svg>
      @if (activePoint(); as p) {
        <div class="tip" [style.left.px]="p.x" [style.top.px]="Math.max(p.y - 46, 0)">
          <span class="tip__label">{{ p.label }}</span>
          <strong>{{ p.text }}</strong>
        </div>
      }
      <details class="chart-table">
        <summary>View data as a table</summary>
        <table>
          <thead><tr><th></th><th class="num">{{ seriesName() }}</th></tr></thead>
          <tbody>
            @for (l of labels(); track $index; let i = $index) { <tr><td>{{ l }}</td><td class="num">{{ format()(values()[i]) }}</td></tr> }
          </tbody>
        </table>
      </details>
    }
  `,
  styles: [`
    :host { display: block; position: relative; }
    .chart { display: block; max-width: 100%; overflow: visible; }
    .grid { stroke: var(--border-subtle); stroke-width: 1; stroke-dasharray: 3 4; }
    .axis { fill: var(--text-muted); font-size: 11px; }
    .point { cursor: pointer; outline: none; }
    .point:focus-visible { stroke-width: 3; }
    .empty { color: var(--text-muted); font-style: italic; margin: var(--space-4) 0; }
    .tip {
      position: absolute;
      transform: translateX(-50%);
      background: var(--surface-card);
      border: 1px solid var(--border-default);
      border-radius: var(--radius-md);
      box-shadow: var(--shadow-md);
      padding: 4px 10px;
      font-size: 12px;
      pointer-events: none;
      white-space: nowrap;
      display: flex;
      flex-direction: column;
    }
    .tip__label { color: var(--text-muted); font-size: 11px; }
    .chart-table { font-size: var(--font-size-meta); color: var(--text-secondary); margin-top: 2px; }
    .chart-table summary { cursor: pointer; }
    .chart-table table { margin-top: 4px; width: 100%; }
    .chart-table th, .chart-table td { padding: 2px 8px; text-align: left; }
    .chart-table .num { text-align: right; }
  `],
})
export class LineChart implements OnDestroy {
  readonly labels = input.required<string[]>();
  readonly values = input.required<number[]>();
  readonly seriesName = input('Value');
  readonly color = input('var(--color-primary)');
  readonly ariaLabel = input('Line chart');
  readonly emptyMessage = input('No data in this period.');
  readonly format = input<(n: number) => string>((n) => String(n));

  protected readonly Math = Math;
  protected readonly width = signal(520);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private observer?: ResizeObserver;

  constructor() {
    // Draw at the container's real width so circles and text are never stretched: measure once after the
    // first render, then follow resizes.
    afterNextRender(() => {
      const w = Math.floor(this.host.nativeElement.clientWidth);
      if (w > 120 && w !== this.width()) this.width.set(w);
    });
    if (typeof ResizeObserver !== 'undefined') {
      this.observer = new ResizeObserver((entries) => {
        const w = Math.floor(entries[0].contentRect.width);
        if (w > 120 && w !== this.width()) this.width.set(w);
      });
      this.observer.observe(this.host.nativeElement);
    }
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }

  protected readonly height = 150;
  protected readonly left = 44;
  protected readonly right = 8;
  private readonly top = 10;
  private readonly bottom = 26;
  protected readonly gradientId = 'lc-' + Math.random().toString(36).slice(2, 8);
  protected readonly active = signal<number | null>(null);

  private readonly max = computed(() => {
    const m = Math.max(0, ...this.values());
    return m === 0 ? 1 : m;
  });

  protected readonly ticks = computed(() => {
    const plot = this.height - this.top - this.bottom;
    return [0, 0.5, 1].map((f) => ({ value: f, y: this.top + plot - f * plot, text: this.format()(this.max() * f) }));
  });

  protected readonly points = computed(() => {
    const labels = this.labels();
    const values = this.values();
    const plotW = this.width() - this.left - this.right;
    const plotH = this.height - this.top - this.bottom;
    const n = labels.length;
    return labels.map((label, i) => ({
      i,
      label,
      text: this.format()(values[i] ?? 0),
      x: this.left + (n === 1 ? plotW / 2 : (i / (n - 1)) * plotW),
      y: this.top + plotH - ((values[i] ?? 0) / this.max()) * plotH,
    }));
  });

  protected readonly linePath = computed(() => this.points().map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x},${p.y}`).join(' '));

  protected readonly areaPath = computed(() => {
    const pts = this.points();
    if (pts.length < 2) return '';
    const base = this.height - this.bottom;
    return `${this.linePath()} L${pts[pts.length - 1].x},${base} L${pts[0].x},${base} Z`;
  });

  protected readonly xLabels = computed(() => {
    const pts = this.points();
    const step = Math.max(1, Math.ceil(pts.length / 8));
    return pts.filter((_, i) => i % step === 0 || i === pts.length - 1).map((p) => ({ x: p.x, text: p.label }));
  });

  protected readonly activePoint = computed(() => {
    const i = this.active();
    return i === null ? null : (this.points()[i] ?? null);
  });
}
