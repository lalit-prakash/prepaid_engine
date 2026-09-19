import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { ConsumerService } from '../../../../core/services/consumer.service';
import { ConnectionStatus, ConsumerListItem } from '../../../../core/models/consumer.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

const PAGE_SIZE = 25;

/**
 * Consumers list. Search, filters and paging all run on the server
 * (GET /api/v1/consumers/search, keyset-paginated on account number) - the browser only ever
 * holds one page. Only columns the backend actually models are shown; circle/division/feeder/
 * tariff/category from the wider spec are omitted rather than faked.
 */
@Component({
  selector: 'pe-consumer-list',
  imports: [StatusBadge, DecimalPipe, DatePipe],
  templateUrl: './consumer-list.html',
  styleUrl: './consumer-list.scss',
})
export class ConsumerList implements OnInit, OnDestroy {
  protected readonly items = signal<ConsumerListItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly statusFilter = signal<ConnectionStatus | null>(null);
  protected readonly lowBalanceOnly = signal(false);
  protected readonly ConnectionStatus = ConnectionStatus;

  /** Cursors used to reach each page; index 0 is the first page (no cursor). */
  private readonly cursorStack = signal<(string | null)[]>([null]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly pageIndex = signal(0);

  protected readonly statusOptions = [
    { label: 'Active', value: ConnectionStatus.Active },
    { label: 'Disconnected', value: ConnectionStatus.Disconnected },
    { label: 'Disconnection pending', value: ConnectionStatus.DisconnectionPending },
    { label: 'Reconnection pending', value: ConnectionStatus.ReconnectionPending },
  ];

  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');
  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;
  private requestSub?: Subscription;

  constructor(
    private readonly consumerService: ConsumerService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    const initialQuery = params.get('q');
    if (initialQuery) this.searchTerm.set(initialQuery);
    if (params.get('lowBalance') === 'true') this.lowBalanceOnly.set(true);
    const initialStatus = params.get('status');
    if (initialStatus !== null && initialStatus in ConnectionStatus) {
      this.statusFilter.set(ConnectionStatus[initialStatus as keyof typeof ConnectionStatus]);
    }

    this.searchSub = this.search$
      .pipe(debounceTime(300))
      .subscribe((term) => {
        if (term === this.searchTerm()) return;
        this.searchTerm.set(term);
        this.resetAndLoad();
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
    this.statusFilter.set(raw === '' ? null : (Number(raw) as ConnectionStatus));
    this.resetAndLoad();
  }

  onLowBalanceChange(checked: boolean): void {
    this.lowBalanceOnly.set(checked);
    this.resetAndLoad();
  }

  clearFilters(): void {
    const box = this.searchBox();
    if (box) box.nativeElement.value = '';
    this.searchTerm.set('');
    this.statusFilter.set(null);
    this.lowBalanceOnly.set(false);
    this.resetAndLoad();
  }

  protected get hasActiveFilters(): boolean {
    return !!this.searchTerm().trim() || this.statusFilter() !== null || this.lowBalanceOnly();
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

  open(accountNumber: string): void {
    this.router.navigate(['/consumers', accountNumber]);
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
    this.requestSub = this.consumerService
      .search({
        q: this.searchTerm(),
        status: this.statusFilter(),
        lowBalance: this.lowBalanceOnly(),
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
          this.error.set('Could not load consumers from the API.');
          this.loading.set(false);
        },
      });
  }
}
