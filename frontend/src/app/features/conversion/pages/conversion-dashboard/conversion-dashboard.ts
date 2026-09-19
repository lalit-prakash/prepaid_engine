import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { ConversionService } from '../../../../core/services/conversion.service';
import { ConversionConsumerType, ConversionStatus, ConversionSummary, ConversionSummaryStats } from '../../../../core/models/conversion.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { PagedList } from '../../../../shared/utils/paged-list';

const PAGE_SIZE = 25;

/**
 * Conversion: every postpaid-to-prepaid conversion request RMS has pushed (AMISP integration requirement doc, section 1).
 * Counts and the credited total come from GET /api/v1/conversions/summary and the table from the keyset-paged
 * GET /api/v1/conversions/search. Read-only: a row only means the billing mode actually changed once it is Completed.
 */
@Component({
  selector: 'pe-conversion-dashboard',
  imports: [StatusBadge, KpiCard, DatePipe, DecimalPipe, RouterLink],
  templateUrl: './conversion-dashboard.html',
  styleUrl: './conversion-dashboard.scss',
})
export class ConversionDashboard implements OnInit, OnDestroy {
  protected readonly searchTerm = signal('');
  protected readonly statusFilter = signal<string | null>(null);
  protected readonly stats = signal<ConversionSummaryStats | null>(null);
  protected readonly statsError = signal(false);
  protected readonly ConversionStatus = ConversionStatus;
  protected readonly ConversionConsumerType = ConversionConsumerType;

  protected readonly statusOptions = [
    { label: 'Completed', value: 'Completed' },
    { label: 'Rejected', value: 'Rejected' },
    { label: 'Pending (requested or approved)', value: 'Pending' },
  ];

  protected readonly list = new PagedList<ConversionSummary>(
    (after) => this.conversionService.search({ q: this.searchTerm(), status: this.statusFilter(), after, pageSize: PAGE_SIZE }),
    'Could not load conversion requests from the API.',
  );

  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;

  constructor(private readonly conversionService: ConversionService) {}

  ngOnInit(): void {
    this.searchSub = this.search$.pipe(debounceTime(300)).subscribe((term) => {
      if (term === this.searchTerm()) return;
      this.searchTerm.set(term);
      this.list.reload();
    });
    this.conversionService.summary().subscribe({ next: (s) => this.stats.set(s), error: () => this.statsError.set(true) });
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

  protected filterByStatus(value: string | null): void {
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

  protected successRate(s: ConversionSummaryStats): string {
    return s.total === 0 ? '—' : `${((s.completed / s.total) * 100).toFixed(1)}%`;
  }

  protected consumerTypeLabel(type: ConversionConsumerType): string {
    switch (type) {
      case ConversionConsumerType.Vip: return 'VIP';
      case ConversionConsumerType.Hospital: return 'Hospital';
      case ConversionConsumerType.School: return 'School';
      case ConversionConsumerType.ShoppingComplex: return 'Shopping Complex';
      case ConversionConsumerType.Other: return 'Other';
      default: return 'Residential';
    }
  }
}
