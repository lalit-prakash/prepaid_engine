import { DatePipe } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, signal, viewChild } from '@angular/core';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { AuditEntryService } from '../../../../core/services/audit-entry.service';
import { AuditEntrySummary, AuditSummaryStats } from '../../../../core/models/audit-entry.model';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

const PAGE_SIZE = 25;

/**
 * Audit Logs. Search, filters and paging run on the server (GET /api/v1/audit-entries/search,
 * keyset-paginated newest first). Read-only by design: an audit entry is never edited or deleted
 * from the UI, and the API has no endpoint that could. Entries record actor, action, entity and
 * old/new values, plus the actor's role, source address and a correlation ID that ties together everything
 * recorded for one request (system actions and older entries have none of these).
 */
@Component({
  selector: 'pe-audit-dashboard',
  imports: [KpiCard, DatePipe],
  templateUrl: './audit-dashboard.html',
  styleUrl: './audit-dashboard.scss',
})
export class AuditDashboard implements OnInit, OnDestroy {
  protected readonly items = signal<AuditEntrySummary[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly stats = signal<AuditSummaryStats | null>(null);
  protected readonly statsError = signal(false);

  protected readonly searchTerm = signal('');
  protected readonly entityTypeFilter = signal('');
  protected readonly actorFilter = signal('');
  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');

  protected readonly expandedId = signal<string | null>(null);

  private readonly cursorStack = signal<(string | null)[]>([null]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly pageIndex = signal(0);

  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');
  private readonly fromBox = viewChild<ElementRef<HTMLInputElement>>('fromBox');
  private readonly toBox = viewChild<ElementRef<HTMLInputElement>>('toBox');
  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;
  private requestSub?: Subscription;

  constructor(private readonly auditEntryService: AuditEntryService) {}

  ngOnInit(): void {
    this.searchSub = this.search$.pipe(debounceTime(300)).subscribe((term) => {
      if (term === this.searchTerm()) return;
      this.searchTerm.set(term);
      this.resetAndLoad();
    });
    this.auditEntryService.summary().subscribe({
      next: (s) => this.stats.set(s),
      error: () => this.statsError.set(true),
    });
    this.load();
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
    this.requestSub?.unsubscribe();
  }

  onSearchInput(term: string): void {
    this.search$.next(term);
  }

  onEntityTypeChange(value: string): void {
    this.entityTypeFilter.set(value);
    this.resetAndLoad();
  }

  onActorChange(value: string): void {
    this.actorFilter.set(value);
    this.resetAndLoad();
  }

  onDateChange(which: 'from' | 'to', value: string): void {
    (which === 'from' ? this.fromDate : this.toDate).set(value);
    this.resetAndLoad();
  }

  clearFilters(): void {
    for (const box of [this.searchBox(), this.fromBox(), this.toBox()]) {
      if (box) box.nativeElement.value = '';
    }
    this.searchTerm.set('');
    this.entityTypeFilter.set('');
    this.actorFilter.set('');
    this.fromDate.set('');
    this.toDate.set('');
    this.resetAndLoad();
  }

  protected get hasActiveFilters(): boolean {
    return !!(this.searchTerm().trim() || this.entityTypeFilter() || this.actorFilter() || this.fromDate() || this.toDate());
  }

  toggle(id: string): void {
    this.expandedId.update((current) => (current === id ? null : id));
  }

  nextPage(): void {
    const cursor = this.nextCursor();
    if (!cursor) return;
    this.cursorStack.update((stack) => [...stack.slice(0, this.pageIndex() + 1), cursor]);
    this.pageIndex.update((i) => i + 1);
    this.load();
  }

  previousPage(): void {
    if (this.pageIndex() === 0) return;
    this.pageIndex.update((i) => i - 1);
    this.load();
  }

  retry(): void {
    this.load();
  }

  private resetAndLoad(): void {
    this.cursorStack.set([null]);
    this.pageIndex.set(0);
    this.expandedId.set(null);
    this.load();
  }

  private load(): void {
    this.requestSub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.requestSub = this.auditEntryService
      .search({
        q: this.searchTerm(),
        entityType: this.entityTypeFilter(),
        actor: this.actorFilter(),
        from: this.fromDate(),
        to: this.toDate(),
        after: this.cursorStack()[this.pageIndex()],
        pageSize: PAGE_SIZE,
      })
      .subscribe({
        next: (page) => {
          this.items.set(page.items);
          this.totalCount.set(page.totalCount);
          this.nextCursor.set(page.nextCursor);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Could not load audit entries from the API.');
          this.loading.set(false);
        },
      });
  }
}
