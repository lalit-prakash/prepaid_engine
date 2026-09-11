import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ReconciliationService } from '../../../../core/services/reconciliation.service';
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
export class ReconciliationDashboard implements OnInit {
  protected readonly adjustments = signal<ReconciliationAdjustmentSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');

  protected readonly accountNumber = signal('');
  protected readonly amount = signal<number | null>(null);
  protected readonly reference = signal('');
  protected readonly applying = signal(false);
  protected readonly applyError = signal<string | null>(null);
  protected readonly applySuccess = signal<string | null>(null);

  constructor(private readonly reconciliationService: ReconciliationService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.reconciliationService.list().subscribe({
      next: (adjustments) => {
        this.adjustments.set(adjustments);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load reconciliation adjustments from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): ReconciliationAdjustmentSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.adjustments();
    return this.adjustments().filter(
      (a) => a.accountNumber.toLowerCase().includes(term) || a.name.toLowerCase().includes(term),
    );
  }

  protected get totalCredited(): number {
    return this.adjustments().filter((a) => a.amount > 0).reduce((sum, a) => sum + a.amount, 0);
  }
  protected get totalDebited(): number {
    return this.adjustments().filter((a) => a.amount < 0).reduce((sum, a) => sum + Math.abs(a.amount), 0);
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
          this.load();
        },
        error: (err) => {
          this.applying.set(false);
          this.applyError.set(err?.error?.error ?? 'Could not apply this reconciliation adjustment.');
        },
      });
  }
}
