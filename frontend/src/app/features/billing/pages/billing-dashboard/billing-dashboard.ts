import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { BillService } from '../../../../core/services/bill.service';
import { BillStatus } from '../../../../core/models/consumer.model';
import { BillListItem, BillSummaryStats } from '../../../../core/models/bill.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { categoryLabel } from '../../../../shared/utils/category-label';

const PAGE_SIZE = 25;

/**
 * Billing. KPIs come from GET /api/v1/bills/summary and the table from the keyset-paginated
 * GET /api/v1/bills/search, so the browser only ever holds one page. Every bill row carries the
 * tariff it was billed under; historical bills are never recalculated against the current tariff.
 */
@Component({
  selector: 'pe-billing-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe],
  templateUrl: './billing-dashboard.html',
  styleUrl: './billing-dashboard.scss',
})
export class BillingDashboard implements OnInit, OnDestroy {
  protected readonly items = signal<BillListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly stats = signal<BillSummaryStats | null>(null);
  protected readonly statsError = signal(false);

  protected readonly searchTerm = signal('');
  protected readonly statusFilter = signal<BillStatus | null>(null);
  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');

  protected readonly BillStatus = BillStatus;
  protected readonly categoryLabel = categoryLabel;
  protected readonly statusOptions = [
    { label: 'Generated', value: BillStatus.Generated },
    { label: 'Partially paid', value: BillStatus.PartiallyPaid },
    { label: 'Paid', value: BillStatus.Paid },
    { label: 'Overdue', value: BillStatus.Overdue },
    { label: 'Cancelled', value: BillStatus.Cancelled },
  ];

  private readonly cursorStack = signal<(string | null)[]>([null]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly pageIndex = signal(0);

  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');
  private readonly fromBox = viewChild<ElementRef<HTMLInputElement>>('fromBox');
  private readonly toBox = viewChild<ElementRef<HTMLInputElement>>('toBox');
  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;
  private requestSub?: Subscription;

  constructor(
    private readonly billService: BillService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    const q = params.get('q');
    if (q) this.searchTerm.set(q);
    const status = params.get('status');
    if (status !== null && status in BillStatus) this.statusFilter.set(BillStatus[status as keyof typeof BillStatus]);

    this.searchSub = this.search$.pipe(debounceTime(300)).subscribe((term) => {
      if (term === this.searchTerm()) return;
      this.searchTerm.set(term);
      this.resetAndLoad();
    });

    this.billService.summary().subscribe({
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

  onStatusChange(raw: string): void {
    this.statusFilter.set(raw === '' ? null : (Number(raw) as BillStatus));
    this.resetAndLoad();
  }

  onDateChange(which: 'from' | 'to', value: string): void {
    (which === 'from' ? this.fromDate : this.toDate).set(value);
    this.resetAndLoad();
  }

  /** KPI drill-down. */
  filterByStatus(status: BillStatus): void {
    this.statusFilter.set(status);
    this.resetAndLoad();
  }

  clearFilters(): void {
    for (const box of [this.searchBox(), this.fromBox(), this.toBox()]) {
      if (box) box.nativeElement.value = '';
    }
    this.searchTerm.set('');
    this.statusFilter.set(null);
    this.fromDate.set('');
    this.toDate.set('');
    this.resetAndLoad();
  }

  protected get hasActiveFilters(): boolean {
    return !!this.searchTerm().trim() || this.statusFilter() !== null || !!this.fromDate() || !!this.toDate();
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

  open(id: string): void {
    this.router.navigate(['/billing', id]);
  }

  private resetAndLoad(): void {
    this.cursorStack.set([null]);
    this.pageIndex.set(0);
    this.load();
  }

  private load(): void {
    this.requestSub?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.requestSub = this.billService
      .search({
        q: this.searchTerm(),
        status: this.statusFilter(),
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
          this.error.set('Could not load bills from the API.');
          this.loading.set(false);
        },
      });
  }
}
