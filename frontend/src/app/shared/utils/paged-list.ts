import { signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';

/** A page of a keyset-paged search: the rows, the cursor for the next page (null at the end) and the total that match. */
export interface Page<T> {
  items: T[];
  nextCursor: string | null;
  totalCount: number;
}

/**
 * State and paging for a server-searched list. It keeps one page in memory and a stack of cursors so "Previous" works;
 * a filter change calls <see cref="reload"/> to start again from the first page. A request still in flight is cancelled
 * when a newer one starts, so a slow answer can never overwrite a newer page.
 */
export class PagedList<T> {
  readonly items = signal<T[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly pageIndex = signal(0);
  readonly nextCursor = signal<string | null>(null);

  private cursors: (string | null)[] = [null];
  private request?: Subscription;

  constructor(
    private readonly fetch: (after: string | null) => Observable<Page<T>>,
    private readonly errorMessage: string,
  ) {}

  /** First page again, after a filter or search change. */
  reload(): void {
    this.cursors = [null];
    this.pageIndex.set(0);
    this.load();
  }

  next(): void {
    const cursor = this.nextCursor();
    if (!cursor) return;
    this.cursors = [...this.cursors.slice(0, this.pageIndex() + 1), cursor];
    this.pageIndex.update((i) => i + 1);
    this.load();
  }

  previous(): void {
    if (this.pageIndex() === 0) return;
    this.pageIndex.update((i) => i - 1);
    this.load();
  }

  load(): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.request = this.fetch(this.cursors[this.pageIndex()]).subscribe({
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

  destroy(): void {
    this.request?.unsubscribe();
  }
}
