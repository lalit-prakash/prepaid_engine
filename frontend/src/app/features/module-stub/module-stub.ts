import { Component, input } from '@angular/core';

/**
 * Honest placeholder for modules the MASTER PROMPT specifies (Billing,
 * Recharge Operations, Meter Credit, RC/DC, Conversion, Exceptions,
 * Reconciliation, Tariffs, Calculation Workbench, Reports, Automation,
 * Audit, System Health) that have no backing domain model or API yet.
 * Deliberately not a fake dashboard with invented data pretending those
 * workflows exist — see docs/ARCHITECTURE.md for what's real vs. planned.
 */
@Component({
  selector: 'pe-module-stub',
  template: `
    <div class="stub">
      <h1>{{ title() }}</h1>
      <p class="stub__body">
        This module is defined in the Prepaid Engine UI/UX specification but has no backing
        domain model or API endpoint in the backend yet. Building it now would mean inventing
        a shadow set of entities, services, and mock data with no real system behind them —
        see <code>docs/ARCHITECTURE.md</code> for the current real-vs-planned boundary and
        the build order this frontend is following.
      </p>
    </div>
  `,
  styles: [`
    .stub {
      background: var(--surface-card);
      border: 1px dashed var(--border-default);
      border-radius: var(--radius-lg);
      padding: var(--space-8);
      max-width: 640px;
    }
    h1 { font-size: var(--font-size-page-title); margin-bottom: var(--space-3); }
    .stub__body { color: var(--text-secondary); line-height: 1.6; }
    code {
      background: var(--color-neutral-100);
      padding: 1px 5px;
      border-radius: var(--radius-sm);
      font-size: 0.92em;
    }
  `],
})
export class ModuleStub {
  readonly title = input.required<string>();
}
