import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { ConnectivityCommandService } from '../../../../core/services/connectivity-command.service';
import {
  ConnectivityCommandStatus,
  ConnectivityCommandSummary,
  ConnectivityCommandSummaryStats,
  ConnectivityCommandType,
} from '../../../../core/models/connectivity-command.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { PagedList } from '../../../../shared/utils/paged-list';

const PAGE_SIZE = 25;

/**
 * RC/DC. KPIs come from GET /api/v1/connectivity-commands/summary (counted by the database) and the table from the
 * keyset-paged GET /api/v1/connectivity-commands/search, so the browser never holds more than one page.
 */
@Component({
  selector: 'pe-rc-dc-dashboard',
  imports: [StatusBadge, KpiCard, DatePipe, DecimalPipe, RouterLink],
  templateUrl: './rc-dc-dashboard.html',
  styleUrl: './rc-dc-dashboard.scss',
})
export class RcDcDashboard implements OnInit, OnDestroy {
  protected readonly searchTerm = signal('');
  protected readonly typeFilter = signal<ConnectivityCommandType | null>(null);
  protected readonly statusFilter = signal<string | null>(null);
  protected readonly stats = signal<ConnectivityCommandSummaryStats | null>(null);
  protected readonly statsError = signal(false);
  protected readonly ConnectivityCommandStatus = ConnectivityCommandStatus;
  protected readonly ConnectivityCommandType = ConnectivityCommandType;

  protected readonly statusOptions = [
    { label: 'Acknowledged', value: 'Acknowledged' },
    { label: 'Failed or timed out', value: 'FailedOrTimedOut' },
    { label: 'Pending (queued or sent)', value: 'Pending' },
  ];

  protected readonly list = new PagedList<ConnectivityCommandSummary>(
    (after) =>
      this.connectivityCommandService.search({ q: this.searchTerm(), type: this.typeFilter(), status: this.statusFilter(), after, pageSize: PAGE_SIZE }),
    'Could not load connectivity commands from the API.',
  );

  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;

  constructor(
    private readonly connectivityCommandService: ConnectivityCommandService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    // Prefills from a cross-link (for example Consumer 360's RC/DC panel: "View RC/DC history").
    const initialQuery = this.route.snapshot.queryParamMap.get('q');
    if (initialQuery) this.searchTerm.set(initialQuery);

    this.searchSub = this.search$.pipe(debounceTime(300)).subscribe((term) => {
      if (term === this.searchTerm()) return;
      this.searchTerm.set(term);
      this.list.reload();
    });
    this.connectivityCommandService.summary().subscribe({ next: (s) => this.stats.set(s), error: () => this.statsError.set(true) });
    this.list.load();
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
    this.list.destroy();
  }

  protected onSearchInput(term: string): void {
    this.search$.next(term);
  }

  protected onTypeChange(raw: string): void {
    this.typeFilter.set(raw === '' ? null : (Number(raw) as ConnectivityCommandType));
    this.list.reload();
  }

  protected onStatusChange(value: string): void {
    this.statusFilter.set(value || null);
    this.list.reload();
  }

  /** KPI tile drill-down: one filter at a time. */
  protected filterByType(type: ConnectivityCommandType | null): void {
    this.statusFilter.set(null);
    this.typeFilter.set(type);
    this.list.reload();
  }

  protected filterByStatus(status: string | null): void {
    this.typeFilter.set(null);
    this.statusFilter.set(status);
    this.list.reload();
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.typeFilter.set(null);
    this.statusFilter.set(null);
    this.list.reload();
  }

  protected get hasActiveFilters(): boolean {
    return !!this.searchTerm().trim() || this.typeFilter() !== null || !!this.statusFilter();
  }

  protected successRate(s: ConnectivityCommandSummaryStats): string {
    return s.total === 0 ? '—' : `${((s.acknowledged / s.total) * 100).toFixed(1)}%`;
  }

  open(id: string): void {
    this.router.navigate(['/rc-dc', id]);
  }
}
