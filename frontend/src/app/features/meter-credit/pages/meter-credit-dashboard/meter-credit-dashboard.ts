import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { MeterCommandService } from '../../../../core/services/meter-command.service';
import { MeterCommandStatus, MeterCommandSummary, MeterCommandSummaryStats } from '../../../../core/models/meter-command.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { PagedList } from '../../../../shared/utils/paged-list';

const PAGE_SIZE = 25;

/**
 * Meter Credit. KPIs come from GET /api/v1/meter-commands/summary (counted by the database) and the table from the
 * keyset-paged GET /api/v1/meter-commands/search, so the browser never holds more than one page. A command only means
 * "the meter was actually credited" once it reaches Acknowledged; see MeterCommand's doc comment on the backend.
 */
@Component({
  selector: 'pe-meter-credit-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe],
  templateUrl: './meter-credit-dashboard.html',
  styleUrl: './meter-credit-dashboard.scss',
})
export class MeterCreditDashboard implements OnInit, OnDestroy {
  protected readonly searchTerm = signal('');
  protected readonly statusFilter = signal<string | null>(null);
  protected readonly stats = signal<MeterCommandSummaryStats | null>(null);
  protected readonly statsError = signal(false);
  protected readonly MeterCommandStatus = MeterCommandStatus;

  protected readonly statusOptions = [
    { label: 'Acknowledged', value: 'Acknowledged' },
    { label: 'Failed', value: 'Failed' },
    { label: 'Timed out', value: 'TimedOut' },
    { label: 'Pending (queued or sent)', value: 'Pending' },
    { label: 'Retried', value: 'Retried' },
  ];

  protected readonly list = new PagedList<MeterCommandSummary>(
    (after) => this.meterCommandService.search({ q: this.searchTerm(), status: this.statusFilter(), after, pageSize: PAGE_SIZE }),
    'Could not load meter commands from the API.',
  );

  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;

  constructor(
    private readonly meterCommandService: MeterCommandService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    // Prefills from a cross-link (for example Consumer 360's Recharge panel: "View meter credit history").
    const initialQuery = this.route.snapshot.queryParamMap.get('q');
    if (initialQuery) this.searchTerm.set(initialQuery);

    this.searchSub = this.search$.pipe(debounceTime(300)).subscribe((term) => {
      if (term === this.searchTerm()) return;
      this.searchTerm.set(term);
      this.list.reload();
    });
    this.meterCommandService.summary().subscribe({ next: (s) => this.stats.set(s), error: () => this.statsError.set(true) });
    this.list.load();
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
    this.list.destroy();
  }

  protected onSearchInput(term: string): void {
    this.search$.next(term);
  }

  protected onStatusChange(value: string): void {
    this.statusFilter.set(value || null);
    this.list.reload();
  }

  /** KPI tile drill-down. */
  protected filterBy(value: string | null): void {
    this.statusFilter.set(value);
    this.list.reload();
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.statusFilter.set(null);
    this.list.reload();
  }

  protected get hasActiveFilters(): boolean {
    return !!this.searchTerm().trim() || !!this.statusFilter();
  }

  protected successRate(s: MeterCommandSummaryStats): string {
    return s.total === 0 ? '—' : `${((s.acknowledged / s.total) * 100).toFixed(1)}%`;
  }

  open(id: string): void {
    this.router.navigate(['/meter-credit', id]);
  }
}
