import { Component, computed, input } from '@angular/core';

export type StatusTone = 'success' | 'warning' | 'danger' | 'info' | 'analytic' | 'neutral';

/**
 * A status pill that never relies on color alone (WCAG 2.1 AA §1.4.1) — every
 * tone pairs with a distinct leading glyph so status is legible without
 * color vision. Reused across every module (Overview, Consumers, Billing,
 * Recharge, ...) so the whole app shares one status vocabulary.
 */
@Component({
  selector: 'pe-status-badge',
  template: `
    <span class="badge" [class]="'badge--' + tone()">
      <span class="badge__glyph" aria-hidden="true">{{ glyph() }}</span>
      <span>{{ label() }}</span>
    </span>
  `,
  styles: [`
    .badge {
      display: inline-flex;
      align-items: center;
      gap: 5px;
      padding: 2px 10px;
      border-radius: var(--radius-pill);
      font-size: var(--font-size-meta);
      font-weight: var(--font-weight-medium);
      line-height: 1.6;
      white-space: nowrap;
    }
    .badge__glyph { font-size: 10px; }

    .badge--success { background: var(--color-success-light); color: var(--color-success-dark); }
    .badge--warning { background: var(--color-warning-light); color: var(--color-warning-dark); }
    .badge--danger  { background: var(--color-danger-light);  color: var(--color-danger-dark); }
    .badge--info    { background: var(--color-info-light);    color: var(--color-info-dark); }
    .badge--analytic{ background: var(--color-analytic-light);color: var(--color-analytic-dark); }
    .badge--neutral { background: var(--color-neutral-100);   color: var(--text-secondary); }
  `],
})
export class StatusBadge {
  readonly label = input.required<string>();
  readonly tone = input<StatusTone>('neutral');

  protected readonly glyph = computed(() => {
    switch (this.tone()) {
      case 'success': return '●';
      case 'warning': return '▲';
      case 'danger': return '✕';
      case 'info': return '◐';
      case 'analytic': return '◆';
      default: return '○';
    }
  });
}
