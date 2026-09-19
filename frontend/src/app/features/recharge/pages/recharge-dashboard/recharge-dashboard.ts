import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { RechargeService } from '../../../../core/services/recharge.service';
import {
  MeterCommandStatus,
  RechargeListItem,
  RechargeStatus,
  RechargeSummaryStats,
} from '../../../../core/models/recharge.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

const PAGE_SIZE = 25;

/**
 * Recharge Operations. KPIs come from GET /api/v1/recharges/summary and the table from the
 * keyset-paginated GET /api/v1/recharges/search, so the browser never holds more than one page.
 * Payment status (RMS) and meter-credit status are shown and filtered separately: RMS confirming
 * a payment never implies the meter was credited.
 */
@Component({
  selector: 'pe-recharge-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe],
  templateUrl: './recharge-dashboard.html',
  styleUrl: './recharge-dashboard.scss',
})
export class RechargeDashboard implements OnInit, OnDestroy {
  protected readonly items = signal<RechargeListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly stats = signal<RechargeSummaryStats | null>(null);
  protected readonly statsError = signal(false);

  protected readonly searchTerm = signal('');
  protected readonly paymentFilter = signal<RechargeStatus | null>(null);
  protected readonly meterCreditFilter = signal<string | null>(null);

  protected readonly RechargeStatus = RechargeStatus;
  protected readonly MeterCommandStatus = MeterCommandStatus;

  protected readonly paymentOptions = [
    { label: 'Payment received', value: RechargeStatus.Success },
    { label: 'Payment failed', value: RechargeStatus.Failed },
    { label: 'Payment pending', value: RechargeStatus.Initiated },
    { label: 'Reversed', value: RechargeStatus.Reversed },
  ];
  protected readonly meterCreditOptions = [
    { label: 'Meter credited', value: 'Acknowledged' },
    { label: 'Awaiting meter (queued)', value: 'Queued' },
    { label: 'Awaiting meter (sent)', value: 'Sent' },
    { label: 'Meter credit failed or timed out', value: 'FailedOrTimedOut' },
    { label: 'Meter credit failed', value: 'Failed' },
    { label: 'Meter credit timed out', value: 'TimedOut' },
    { label: 'No meter credit', value: 'None' },
  ];

  private readonly cursorStack = signal<(string | null)[]>([null]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly pageIndex = signal(0);

  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');
  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;
  private requestSub?: Subscription;

  constructor(
    private readonly rechargeService: RechargeService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    const q = params.get('q');
    if (q) this.searchTerm.set(q);
    const payment = params.get('paymentStatus');
    if (payment !== null && payment in RechargeStatus) {
      this.paymentFilter.set(RechargeStatus[payment as keyof typeof RechargeStatus]);
    }
    const credit = params.get('meterCredit');
    if (credit && this.meterCreditOptions.some((o) => o.value === credit)) this.meterCreditFilter.set(credit);

    this.searchSub = this.search$.pipe(debounceTime(300)).subscribe((term) => {
      if (term === this.searchTerm()) return;
      this.searchTerm.set(term);
      this.resetAndLoad();
    });

    this.rechargeService.summary().subscribe({
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

  onPaymentChange(raw: string): void {
    this.paymentFilter.set(raw === '' ? null : (Number(raw) as RechargeStatus));
    this.resetAndLoad();
  }

  onMeterCreditChange(raw: string): void {
    this.meterCreditFilter.set(raw === '' ? null : raw);
    this.resetAndLoad();
  }

  /** KPI drill-down: apply one filter from a tile. */
  filterByMeterCredit(value: string): void {
    this.paymentFilter.set(null);
    this.meterCreditFilter.set(value);
    this.resetAndLoad();
  }

  filterByPayment(value: RechargeStatus): void {
    this.meterCreditFilter.set(null);
    this.paymentFilter.set(value);
    this.resetAndLoad();
  }

  clearFilters(): void {
    const box = this.searchBox();
    if (box) box.nativeElement.value = '';
    this.searchTerm.set('');
    this.paymentFilter.set(null);
    this.meterCreditFilter.set(null);
    this.resetAndLoad();
  }

  protected get hasActiveFilters(): boolean {
    return !!this.searchTerm().trim() || this.paymentFilter() !== null || this.meterCreditFilter() !== null;
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
    this.router.navigate(['/recharge', id]);
  }

  protected successRate(s: RechargeSummaryStats): string {
    return s.total === 0 ? '—' : `${((s.paymentSuccess / s.total) * 100).toFixed(1)}%`;
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
    this.requestSub = this.rechargeService
      .search({
        q: this.searchTerm(),
        paymentStatus: this.paymentFilter(),
        meterCredit: this.meterCreditFilter(),
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
          this.error.set('Could not load recharges from the API.');
          this.loading.set(false);
        },
      });
  }
}
