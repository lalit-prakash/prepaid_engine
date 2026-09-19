import { Component, computed, input } from '@angular/core';

export interface BarSeries {
  name: string;
  /** CSS colour, normally a design token such as 'var(--color-primary)'. */
  color: string;
  values: number[];
}

/**
 * A small accessible grouped bar chart drawn as SVG (no charting dependency). One or more series
 * share the same labels. It never invents data: an empty input renders a plain "no data" note, and
 * every chart also offers its numbers as a table (a chart alone is not accessible, and colour is
 * never the only way to tell series apart - the legend names them and the table lists them).
 */
@Component({
  selector: 'pe-bar-chart',
  template: `
    @if (labels().length === 0) {
      <p class="chart-empty">{{ emptyMessage() }}</p>
    } @else {
      <svg class="chart" [attr.viewBox]="'0 0 ' + width + ' ' + height" role="img" [attr.aria-label]="ariaLabel()" preserveAspectRatio="xMidYMid meet">
        @for (tick of ticks(); track tick.value) {
          <line [attr.x1]="left" [attr.x2]="width" [attr.y1]="tick.y" [attr.y2]="tick.y" class="grid" />
          <text [attr.x]="left - 6" [attr.y]="tick.y + 4" class="axis axis--y" text-anchor="end">{{ tick.text }}</text>
        }
        @for (group of groups(); track group.label) {
          @for (bar of group.bars; track bar.series) {
            <rect [attr.x]="bar.x" [attr.y]="bar.y" [attr.width]="bar.width" [attr.height]="bar.height" [attr.fill]="bar.color" rx="2">
              <title>{{ group.label }} — {{ bar.series }}: {{ bar.text }}</title>
            </rect>
          }
          <text [attr.x]="group.centre" [attr.y]="height - 8" class="axis" text-anchor="middle">{{ group.label }}</text>
        }
      </svg>
      @if (series().length > 1) {
        <ul class="legend">
          @for (s of series(); track s.name) {
            <li><span class="legend__swatch" [style.background]="s.color"></span>{{ s.name }}</li>
          }
        </ul>
      }
      <details class="chart-table">
        <summary>View data as a table</summary>
        <table>
          <thead>
            <tr><th></th>@for (s of series(); track s.name) { <th class="num">{{ s.name }}</th> }</tr>
          </thead>
          <tbody>
            @for (label of labels(); track $index; let i = $index) {
              <tr><td>{{ label }}</td>@for (s of series(); track s.name) { <td class="num">{{ format()(s.values[i]) }}</td> }</tr>
            }
          </tbody>
        </table>
      </details>
    }
  `,
  styles: [`
    :host { display: block; }
    .chart { width: 100%; height: auto; display: block; }
    .grid { stroke: var(--border-subtle); stroke-width: 1; }
    .axis { fill: var(--text-muted); font-size: 11px; }
    .chart-empty { color: var(--text-muted); font-style: italic; margin: var(--space-3) 0; }
    .legend { list-style: none; display: flex; gap: var(--space-4); padding: 0; margin: var(--space-2) 0 0; font-size: var(--font-size-meta); color: var(--text-secondary); }
    .legend__swatch { display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin-right: 6px; }
    .chart-table { margin-top: var(--space-2); font-size: var(--font-size-meta); color: var(--text-secondary); }
    .chart-table summary { cursor: pointer; }
    .chart-table table { margin-top: var(--space-2); width: 100%; }
    .chart-table th, .chart-table td { padding: 2px var(--space-2); text-align: left; }
    .chart-table .num { text-align: right; }
  `],
})
export class BarChart {
  readonly labels = input.required<string[]>();
  readonly series = input.required<BarSeries[]>();
  readonly ariaLabel = input('Bar chart');
  readonly emptyMessage = input('No data in this period.');
  readonly format = input<(n: number) => string>((n) => String(n));

  protected readonly width = 640;
  protected readonly height = 220;
  protected readonly left = 52;
  private readonly top = 12;
  private readonly bottom = 28;

  private readonly max = computed(() => {
    const all = this.series().flatMap((s) => s.values);
    const m = Math.max(0, ...all);
    return m === 0 ? 1 : m;
  });

  protected readonly ticks = computed(() => {
    const max = this.max();
    const plotHeight = this.height - this.top - this.bottom;
    return [0, 0.5, 1].map((f) => ({
      value: f,
      y: this.top + plotHeight - f * plotHeight,
      text: this.format()(max * f),
    }));
  });

  protected readonly groups = computed(() => {
    const labels = this.labels();
    const series = this.series();
    const max = this.max();
    const plotWidth = this.width - this.left;
    const plotHeight = this.height - this.top - this.bottom;
    const groupWidth = plotWidth / Math.max(labels.length, 1);
    const barWidth = Math.max(2, Math.min(28, (groupWidth * 0.7) / Math.max(series.length, 1)));
    const cluster = barWidth * series.length;

    return labels.map((label, i) => {
      const groupStart = this.left + i * groupWidth + (groupWidth - cluster) / 2;
      return {
        label,
        centre: this.left + i * groupWidth + groupWidth / 2,
        bars: series.map((s, j) => {
          const value = s.values[i] ?? 0;
          const h = (value / max) * plotHeight;
          return {
            series: s.name,
            color: s.color,
            x: groupStart + j * barWidth,
            y: this.top + plotHeight - h,
            width: barWidth - 1,
            height: h,
            text: this.format()(value),
          };
        }),
      };
    });
  });
}
