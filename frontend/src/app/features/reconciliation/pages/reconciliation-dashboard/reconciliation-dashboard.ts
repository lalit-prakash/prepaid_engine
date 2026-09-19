import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { PagedList } from '../../../../shared/utils/paged-list';
import { ReconciliationService } from '../../../../core/services/reconciliation.service';
import { ReconciliationSummaryStats } from '../../../../core/models/reconciliation.model';
import { ReconciliationAdjustmentSummary } from '../../../../core/models/reconciliation.model';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed Billing Reconciliation dashboard (GET /api/v1/reconciliation-adjustments) —
 * every signed wallet adjustment RMS has pushed, per the AMISP integration requirement doc §7-8:
 * RMS reconciles AMISP's daily billing data against its own shadow monthly bill and pushes any
 * gap (or a consumer credit) as a signed amount, applied to the wallet exactly like a recharge.
 * The apply form below models the same real endpoint an actual RMS integration would call —
 * there is nothing simulated about what happens when you submit it: the consumer's real wallet
 * balance changes.
 */
@Component({
  selector: 'pe-reconciliation-dashboard',
  imports: [KpiCard, DecimalPipe, DatePipe, FormsModule, RouterLink],
  templateUrl: './reconciliation-dashboard.html',
  styleUrl: './reconciliation-dashboard.scss',
})
export class ReconciliationDashboard implements OnInit, OnDestroy {
  protected readonly searchTerm = signal('');
  protected readonly stats = signal<ReconciliationSummaryStats | null>(null);
  protected readonly statsError = signal(false);

  protected readonly accountNumber = signal('');
  protected readonly amount = signal<number | null>(null);
  protected readonly reference = signal('');
  protected readonly applying = signal(false);
  protected readonly applyError = signal<string | null>(null);
  protected readonly applySuccess = signal<string | null>(null);

  protected readonly list = new PagedList<ReconciliationAdjustmentSummary>(
    (after) => this.reconciliationService.search({ q: this.searchTerm(), after, pageSize: 25 }),
    'Could not load reconciliation adjustments from the API.',
  );

  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;

  constructor(private readonly reconciliationService: ReconciliationService) {}

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
    this.reconciliationService.summary().subscribe({ next: (s) => this.stats.set(s), error: () => this.statsError.set(true) });
  }

  protected onSearchInput(term: string): void {
    this.search$.next(term);
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.list.reload();
  }

  protected get hasActiveFilters(): boolean {
    return !!this.searchTerm().trim();
  }

  submitAdjustment(): void {
    const account = this.accountNumber().trim();
    const amount = this.amount();
    const reference = this.reference().trim();

    this.applyError.set(null);
    this.applySuccess.set(null);

    if (!account || amount === null || amount === 0 || !reference) {
      this.applyError.set('Account number, a non-zero amount, and a reference are all required.');
      return;
    }

    this.applying.set(true);
    this.reconciliationService
      .apply(account, { amount, reconciliationDate: new Date().toISOString(), reference })
      .subscribe({
        next: (adjustment) => {
          this.applying.set(false);
          this.applySuccess.set(
            `Applied ₹${adjustment.amount.toFixed(2)} to ${account} — new balance ₹${adjustment.balanceAfter.toFixed(2)}.`,
          );
          this.accountNumber.set('');
          this.amount.set(null);
          this.reference.set('');
          this.loadStats();
          this.list.reload(); // the new adjustment is the newest row, so go back to the first page
        },
        error: (err) => {
          this.applying.set(false);
          this.applyError.set(err?.error?.error ?? 'Could not apply this reconciliation adjustment.');
        },
      });
  }
}
