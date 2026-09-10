import { Component, input } from '@angular/core';

export type KpiTone = 'primary' | 'success' | 'warning' | 'danger' | 'info' | 'neutral';

/**
 * A single KPI tile for dashboards (Overview, Billing, Recharge Operations,
 * ...). Deliberately compact — see README "Information density": dashboards
 * must not become a wall of oversized cards.
 */
@Component({
  selector: 'pe-kpi-card',
  template: `
    <div class="kpi" [class]="'kpi--' + tone()" [class.kpi--clickable]="clickable()">
      <div class="kpi__label">{{ label() }}</div>
      <div class="kpi__value num">{{ value() }}</div>
      @if (sublabel()) {
        <div class="kpi__sublabel">{{ sublabel() }}</div>
      }
      @if (illustrative()) {
        <div class="kpi__illustrative" title="No backend endpoint exists for this yet — illustrative only">Illustrative</div>
      }
    </div>
  `,
  styles: [`
    .kpi {
      background: var(--surface-card);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
      padding: var(--space-4) var(--space-5);
      box-shadow: var(--shadow-sm);
      min-width: 160px;
      border-top: 3px solid var(--color-neutral-300);
    }
    .kpi--clickable { cursor: pointer; transition: box-shadow var(--transition-fast); }
    .kpi--clickable:hover { box-shadow: var(--shadow-md); }

    .kpi--primary { border-top-color: var(--color-primary); }
    .kpi--success { border-top-color: var(--color-success); }
    .kpi--warning { border-top-color: var(--color-warning); }
    .kpi--danger  { border-top-color: var(--color-danger); }
    .kpi--info    { border-top-color: var(--color-info); }

    .kpi__label {
      font-size: var(--font-size-meta);
      color: var(--text-secondary);
      font-weight: var(--font-weight-medium);
      margin-bottom: var(--space-2);
    }
    .kpi__value {
      font-size: var(--font-size-kpi);
      font-weight: var(--font-weight-semibold);
      color: var(--text-primary);
      line-height: 1.15;
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
  `],
})
export class KpiCard {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly sublabel = input<string>();
  readonly tone = input<KpiTone>('neutral');
  readonly clickable = input(false);
  /** True when this number has no real backend source yet (see docs/frontend-scope.md). */
  readonly illustrative = input(false);
}
