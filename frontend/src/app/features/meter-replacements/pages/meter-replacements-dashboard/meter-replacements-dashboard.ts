import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { MeterReplacementService } from '../../../../core/services/meter-replacement.service';
import { MeterAssignmentEventType, MeterReplacementSummary, MeterReplacementSummaryStats } from '../../../../core/models/meter-replacement.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { PagedList } from '../../../../shared/utils/paged-list';

const PAGE_SIZE = 25;

/**
 * Meter Replacement History: every recorded meter event. Counts come from GET /api/v1/meter-replacements/summary and the
 * table from the keyset-paged GET /api/v1/meter-replacements/search. Old and new meter readings are never compared.
 */
@Component({
  selector: 'pe-meter-replacements-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './meter-replacements-dashboard.html',
  styleUrl: './meter-replacements-dashboard.scss',
})
export class MeterReplacementsDashboard implements OnInit, OnDestroy {
  protected readonly searchTerm = signal('');
  protected readonly typeFilter = signal<MeterAssignmentEventType | null>(null);
  protected readonly stats = signal<MeterReplacementSummaryStats | null>(null);
  protected readonly statsError = signal(false);
  protected readonly MeterAssignmentEventType = MeterAssignmentEventType;

  protected readonly list = new PagedList<MeterReplacementSummary>(
    (after) => this.meterReplacementService.search({ q: this.searchTerm(), eventType: this.typeFilter(), after, pageSize: PAGE_SIZE }),
    'Could not load meter replacement history from the API.',
  );

  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;

  constructor(private readonly meterReplacementService: MeterReplacementService) {}

  ngOnInit(): void {
    this.searchSub = this.search$.pipe(debounceTime(300)).subscribe((term) => {
      if (term === this.searchTerm()) return;
      this.searchTerm.set(term);
      this.list.reload();
    });
    this.meterReplacementService.summary().subscribe({ next: (s) => this.stats.set(s), error: () => this.statsError.set(true) });
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
    this.typeFilter.set(raw === '' ? null : (Number(raw) as MeterAssignmentEventType));
    this.list.reload();
  }

  protected filterByType(type: MeterAssignmentEventType | null): void {
    this.typeFilter.set(type);
    this.list.reload();
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.typeFilter.set(null);
    this.list.reload();
  }

  protected get hasActiveFilters(): boolean {
    return !!this.searchTerm().trim() || this.typeFilter() !== null;
  }

  protected eventTypeLabel(type: MeterAssignmentEventType): string {
    switch (type) {
      case MeterAssignmentEventType.Installed: return 'Installed';
      case MeterAssignmentEventType.Removed: return 'Removed';
      default: return 'Replaced';
    }
  }
}
