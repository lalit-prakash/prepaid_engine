import { Component, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ConsumerService } from '../../../../core/services/consumer.service';
import { ConsumerSummary } from '../../../../core/models/consumer.model';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/**
 * "Is the prepaid operation healthy right now?" — the top-level dashboard.
 * Only the consumer-count KPI is backed by the real API today; every other
 * KPI here would require domain concepts (meter commands, reconciliation,
 * exceptions) that don't exist in the backend yet, so they're rendered as
 * explicitly-labeled illustrative placeholders rather than invented numbers
 * presented as real.
 */
@Component({
  selector: 'pe-overview',
  imports: [KpiCard, StatusBadge],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview implements OnInit {
  protected readonly consumers = signal<ConsumerSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor(
    private readonly consumerService: ConsumerService,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.consumerService.list().subscribe({
      next: (consumers) => {
        this.consumers.set(consumers);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load consumer data from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get lowCreditCount(): number {
    // "Low credit" threshold isn't a modeled concept yet — approximate as
    // balance below the consumer's own emergency-credit limit purely for
    // this illustrative view; not a production rule.
    return this.consumers().filter((c) => c.walletBalance < c.emergencyCreditLimit).length;
  }

  goToConsumers(): void {
    this.router.navigate(['/consumers']);
  }
}
