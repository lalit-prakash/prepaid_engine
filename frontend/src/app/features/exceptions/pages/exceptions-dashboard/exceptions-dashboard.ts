import { OperateOnly } from '../../../../shared/directives/operate-only';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { OperationalExceptionService } from '../../../../core/services/operational-exception.service';
import {
  OperationalExceptionSourceType,
  OperationalExceptionStatus,
  OperationalExceptionSummary,
  OperationalExceptionSummaryStats,
} from '../../../../core/models/operational-exception.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { PagedList } from '../../../../shared/utils/paged-list';

const PAGE_SIZE = 25;

/**
 * Exceptions: every operational exception, auto-raised the moment a MeterCommand or ConnectivityCommand reaches
 * Failed/TimedOut (never hand-entered). Counts come from GET /api/v1/exceptions/summary and the table from the
 * keyset-paged GET /api/v1/exceptions/search. Resolving is a real action gated behind a mandatory note.
 */
@Component({
  selector: 'pe-exceptions-dashboard',
  imports: [OperateOnly, StatusBadge, KpiCard, DatePipe, DecimalPipe, FormsModule, RouterLink],
  templateUrl: './exceptions-dashboard.html',
  styleUrl: './exceptions-dashboard.scss',
})
export class ExceptionsDashboard implements OnInit, OnDestroy {
  protected readonly searchTerm = signal('');
  protected readonly statusFilter = signal<OperationalExceptionStatus | null>(null);
  protected readonly stats = signal<OperationalExceptionSummaryStats | null>(null);
  protected readonly statsError = signal(false);
  protected readonly OperationalExceptionStatus = OperationalExceptionStatus;
  protected readonly OperationalExceptionSourceType = OperationalExceptionSourceType;

  protected readonly resolvingId = signal<string | null>(null);
  protected readonly resolutionNote = signal('');
  protected readonly resolveError = signal<string | null>(null);
  protected readonly resolveSubmitting = signal(false);

  protected readonly list = new PagedList<OperationalExceptionSummary>(
    (after) => this.exceptionService.search({ q: this.searchTerm(), status: this.statusFilter(), after, pageSize: PAGE_SIZE }),
    'Could not load operational exceptions from the API.',
  );

  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;

  constructor(private readonly exceptionService: OperationalExceptionService) {}

  ngOnInit(): void {
    this.searchSub = this.search$.pipe(debounceTime(300)).subscribe((term) => {
      if (term === this.searchTerm()) return;
      this.searchTerm.set(term);
      this.list.reload();
    });
    this.loadStats();
    this.list.load();
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
    this.list.destroy();
  }

  private loadStats(): void {
    this.exceptionService.summary().subscribe({ next: (s) => this.stats.set(s), error: () => this.statsError.set(true) });
  }

  protected onSearchInput(term: string): void {
    this.search$.next(term);
  }

  protected onStatusChange(raw: string): void {
    this.statusFilter.set(raw === '' ? null : (Number(raw) as OperationalExceptionStatus));
    this.list.reload();
  }

  protected filterByStatus(status: OperationalExceptionStatus | null): void {
    this.statusFilter.set(status);
    this.list.reload();
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.statusFilter.set(null);
    this.list.reload();
  }

  protected get hasActiveFilters(): boolean {
    return !!this.searchTerm().trim() || this.statusFilter() !== null;
  }

  requestResolve(id: string): void {
    this.resolvingId.set(id);
    this.resolutionNote.set('');
    this.resolveError.set(null);
  }

  cancelResolve(): void {
    this.resolvingId.set(null);
  }

  confirmResolve(): void {
    const id = this.resolvingId();
    const note = this.resolutionNote().trim();
    if (!id) return;
    if (!note) {
      this.resolveError.set('A resolution note is required.');
      return;
    }

    this.resolveSubmitting.set(true);
    this.exceptionService.resolve(id, note).subscribe({
      next: () => {
        this.resolveSubmitting.set(false);
        this.resolvingId.set(null);
        this.list.load(); // stay on the same page: the resolved row stays visible unless a filter now excludes it
        this.loadStats();
      },
      error: (err) => {
        this.resolveSubmitting.set(false);
        this.resolveError.set(err?.error?.error ?? 'Could not resolve this exception.');
      },
    });
  }
}
