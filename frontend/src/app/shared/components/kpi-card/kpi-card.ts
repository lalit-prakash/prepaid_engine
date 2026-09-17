import { Component, input } from '@angular/core';
import { Icon } from '../icon/icon';

export type KpiTone = 'primary' | 'success' | 'warning' | 'danger' | 'info' | 'neutral';

/**
 * A single KPI tile for dashboards (Overview, Billing, Recharge Operations,
 * ...). Deliberately compact — see README "Information density": dashboards
 * must not become a wall of oversized cards.
 */
@Component({
  selector: 'pe-kpi-card',
  imports: [Icon],
  template: `
    <div class="kpi" [class]="'kpi--' + tone()" [class.kpi--clickable]="clickable()" [class.kpi--horizontal]="horizontal()">
      @if (icon()) {
        <div class="kpi__icon"><pe-icon [name]="icon()!" [size]="17" /></div>
      }
      <div class="kpi__body">
        <div class="kpi__label">{{ label() }}</div>
        @if (unavailable()) {
          <div class="kpi__value kpi__value--unavailable">Data unavailable</div>
        } @else {
          <div class="kpi__value num">{{ value() }}</div>
        }
        @if (sublabel() && !unavailable()) {
          <div class="kpi__sublabel">{{ sublabel() }}</div>
        }
        @if (illustrative()) {
          <div class="kpi__illustrative" title="No backend endpoint exists for this yet — illustrative only">Illustrative</div>
        }
        @if (progressPct() !== undefined && !unavailable()) {
          <div class="kpi__progress-track"><div class="kpi__progress-fill" [style.width.%]="progressPct()"></div></div>
        }
      </div>
    </div>
  `,
  styles: [`
    .kpi {
      background: var(--surface-card);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      padding: var(--space-4) var(--space-5);
      box-shadow: var(--shadow-sm);
      box-sizing: border-box;
      min-width: 0;
      max-width: 100%;
      border-top: 3px solid var(--color-neutral-300);
    }
    .kpi--clickable { cursor: pointer; transition: box-shadow var(--transition-fast); }
    .kpi--clickable:hover { box-shadow: var(--shadow-md); }

    .kpi--horizontal {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      padding: var(--space-3) var(--space-3);
      border-top: none;
      border-left: 3px solid var(--color-neutral-300);
    }
    .kpi--horizontal.kpi--primary { border-left-color: var(--color-primary); }
    .kpi--horizontal.kpi--success { border-left-color: var(--color-success); }
    .kpi--horizontal.kpi--warning { border-left-color: var(--color-warning); }
    .kpi--horizontal.kpi--danger  { border-left-color: var(--color-danger); }
    .kpi--horizontal.kpi--info    { border-left-color: var(--color-info); }
    .kpi--horizontal .kpi__icon {
      margin-bottom: 0;
      flex-shrink: 0;
      width: 30px;
      height: 30px;
    }
    .kpi--horizontal .kpi__label {
      margin-bottom: 1px;
      font-size: 11px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .kpi--horizontal .kpi__value { font-size: 20px; }
    .kpi--horizontal .kpi__sublabel {
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .kpi--horizontal .kpi__body { min-width: 0; flex: 1; }

    .kpi--primary { border-top-color: var(--color-primary); }
    .kpi--success { border-top-color: var(--color-success); }
    .kpi--warning { border-top-color: var(--color-warning); }
    .kpi--danger  { border-top-color: var(--color-danger); }
    .kpi--info    { border-top-color: var(--color-info); }

    .kpi__icon {
      width: 34px;
      height: 34px;
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 16px;
      margin-bottom: var(--space-2);
      background: var(--color-neutral-100);
    }
    .kpi--primary .kpi__icon { background: var(--color-primary-lighter); }
    .kpi--success .kpi__icon { background: var(--color-success-light); }
    .kpi--warning .kpi__icon { background: var(--color-warning-light); }
    .kpi--danger  .kpi__icon { background: var(--color-danger-light); }
    .kpi--info    .kpi__icon { background: var(--color-info-light); }

    .kpi__label {
      font-size: var(--font-size-meta);
      color: var(--text-secondary);
      font-weight: var(--font-weight-medium);
      margin-bottom: var(--space-2);
      overflow-wrap: break-word;
    }
    .kpi__value {
      font-size: var(--font-size-kpi);
      font-weight: var(--font-weight-semibold);
      color: var(--text-primary);
      line-height: 1.15;
      overflow-wrap: break-word;
      word-break: break-word;
    }
    .kpi__value--unavailable {
      font-size: var(--font-size-body);
      font-weight: var(--font-weight-medium);
      color: var(--text-muted);
      font-style: italic;
    }
    .kpi__sublabel {
      margin-top: var(--space-1);
      font-size: var(--font-size-meta);
      color: var(--text-muted);
    }
    .kpi__illustrative {
      margin-top: var(--space-2);
      display: inline-block;
      font-size: 10px;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      color: var(--color-analytic-dark);
      background: var(--color-analytic-light);
      padding: 1px 6px;
      border-radius: var(--radius-sm);
    }
    .kpi__progress-track {
      margin-top: var(--space-2);
      height: 5px;
      border-radius: var(--radius-pill);
      background: var(--color-neutral-100);
      overflow: hidden;
    }
    .kpi__progress-fill {
      height: 100%;
      background: var(--color-success);
      border-radius: var(--radius-pill);
      transition: width var(--transition-base);
    }
  `],
})
export class KpiCard {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly sublabel = input<string>();
  readonly tone = input<KpiTone>('neutral');
  readonly icon = input<string>();
  readonly clickable = input(false);
  /** Compact icon-left row layout (used by the Dashboard's KPI strip) instead of the
   * default icon-on-top stacked card used elsewhere. */
  readonly horizontal = input(false);
  /** True when this number has no real backend source yet (see docs/frontend-scope.md). */
  readonly illustrative = input(false);
  /** True when the metric is real but genuinely cannot be computed right now (e.g. no rows
   * for today yet) — shows "Data unavailable" instead of a fabricated or stale value. */
  readonly unavailable = input(false);
  /** Optional 0-100 progress bar rendered under the value — used by the Dashboard's
   * "Today's Billing" card to show real percent-processed progress. */
  readonly progressPct = input<number>();
}
