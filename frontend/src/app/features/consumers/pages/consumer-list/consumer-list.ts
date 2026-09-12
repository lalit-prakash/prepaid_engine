import { DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ConsumerService } from '../../../../core/services/consumer.service';
import { ConnectionStatus, ConsumerSummary } from '../../../../core/models/consumer.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/**
 * Real, API-backed consumer list. Only the columns the API actually returns
 * are shown — category/tariff/circle/division/etc. from the full spec aren't
 * modeled in the backend yet, so they're omitted rather than faked.
 */
@Component({
  selector: 'pe-consumer-list',
  imports: [StatusBadge, DecimalPipe],
  templateUrl: './consumer-list.html',
  styleUrl: './consumer-list.scss',
})
export class ConsumerList implements OnInit {
  protected readonly consumers = signal<ConsumerSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly ConnectionStatus = ConnectionStatus;

  constructor(
    private readonly consumerService: ConsumerService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    // Prefills from the header's global search (see Shell.submitSearch) —
    // real reuse of this page's own search, not a separate search endpoint.
    const initialQuery = this.route.snapshot.queryParamMap.get('q');
    if (initialQuery) this.searchTerm.set(initialQuery);

    this.consumerService.list().subscribe({
      next: (consumers) => {
        this.consumers.set(consumers);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load consumers from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): ConsumerSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.consumers();
    return this.consumers().filter(
      (c) =>
        c.accountNumber.toLowerCase().includes(term) ||
        c.name.toLowerCase().includes(term) ||
        c.meterNumber.toLowerCase().includes(term),
    );
  }

  open(accountNumber: string): void {
    this.router.navigate(['/consumers', accountNumber]);
  }
}
