import { Observable, Subscription } from 'rxjs';
import { signal } from '@angular/core';
import { MeterDataPage, MeterDataQuery } from '../../../../core/models/meter-data.model';

const PAGE_SIZE = 25;

/**
 * State for one server-paginated Meter Data tab: current page rows, total, cursor stack for
 * Previous/Next, and loading/error. The component supplies the fetch function; this class owns
 * only the paging bookkeeping so all five profile tabs behave identically.
 */
export class PagedTab<T> {
  readonly items = signal<T[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly pageIndex = signal(0);
  readonly nextCursor = signal<string | null>(null);
  loaded = false;

  private cursors: (string | null)[] = [null];
  private request?: Subscription;

  constructor(
    private readonly fetch: (query: MeterDataQuery) => Observable<MeterDataPage<T>>,
    private readonly errorMessage: string,
  ) {}

  load(filters: Omit<MeterDataQuery, 'after' | 'pageSize'>): void {
    this.loaded = true;
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.request = this.fetch({ ...filters, after: this.cursors[this.pageIndex()], pageSize: PAGE_SIZE }).subscribe({
      next: (page) => {
        this.items.set(page.items);
        this.totalCount.set(page.totalCount);
        this.nextCursor.set(page.nextCursor);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(this.errorMessage);
        this.loading.set(false);
      },
    });
  }

  reset(filters: Omit<MeterDataQuery, 'after' | 'pageSize'>): void {
    this.cursors = [null];
    this.pageIndex.set(0);
    this.load(filters);
  }

  next(filters: Omit<MeterDataQuery, 'after' | 'pageSize'>): void {
    const cursor = this.nextCursor();
    if (!cursor) return;
    this.cursors = [...this.cursors.slice(0, this.pageIndex() + 1), cursor];
    this.pageIndex.update((i) => i + 1);
    this.load(filters);
  }

  previous(filters: Omit<MeterDataQuery, 'after' | 'pageSize'>): void {
    if (this.pageIndex() === 0) return;
    this.pageIndex.update((i) => i - 1);
    this.load(filters);
  }

  destroy(): void {
    this.request?.unsubscribe();
  }
}
