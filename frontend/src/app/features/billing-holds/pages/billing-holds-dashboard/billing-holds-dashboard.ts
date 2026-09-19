import { OperateOnly } from '../../../../shared/directives/operate-only';
import { DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BillingHoldService } from '../../../../core/services/billing-hold.service';
import { BillingHoldSummary } from '../../../../core/models/billing-hold.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed Billing Holds dashboard (GET /api/v1/meter-data/billing-holds) — every
 * MeterBillingControl hold. Clearing one is a genuine action gated behind a mandatory resolution
 * note, matching this project's mandatory-reason discipline — actual DLP billing for the meter
 * resumes the moment it clears.
 */
@Component({
  selector: 'pe-billing-holds-dashboard',
  imports: [OperateOnly, StatusBadge, KpiCard, DatePipe, FormsModule, RouterLink],
  templateUrl: './billing-holds-dashboard.html',
  styleUrl: './billing-holds-dashboard.scss',
})
export class BillingHoldsDashboard implements OnInit {
  protected readonly holds = signal<BillingHoldSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly showCleared = signal(false);

  protected readonly clearingMeterId = signal<string | null>(null);
  protected readonly resolutionNote = signal('');
  protected readonly clearError = signal<string | null>(null);
  protected readonly clearSubmitting = signal(false);

  protected readonly selectedMeterIds = signal<ReadonlySet<string>>(new Set());
  protected readonly bulkClearing = signal(false);
  protected readonly bulkNote = signal('');
  protected readonly bulkError = signal<string | null>(null);
  protected readonly bulkSubmitting = signal(false);
  protected readonly bulkResult = signal<string | null>(null);

  constructor(private readonly billingHoldService: BillingHoldService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.selectedMeterIds.set(new Set());
    this.billingHoldService.list(!this.showCleared()).subscribe({
      next: (holds) => {
        this.holds.set(holds);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load billing holds from the API.');
        this.loading.set(false);
      },
    });
  }

  toggleShowCleared(): void {
    this.showCleared.set(!this.showCleared());
    this.load();
  }

  protected get filtered(): BillingHoldSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.holds();
    return this.holds().filter(
      (h) =>
        h.accountNumber.toLowerCase().includes(term) ||
        h.name.toLowerCase().includes(term) ||
        h.meterNumber.toLowerCase().includes(term),
    );
  }

  protected get activeCount(): number {
    return this.holds().filter((h) => h.actualBillingBlocked).length;
  }
  protected get clearedCount(): number {
    return this.holds().filter((h) => !h.actualBillingBlocked).length;
  }

  requestClear(meterId: string): void {
    this.clearingMeterId.set(meterId);
    this.resolutionNote.set('');
    this.clearError.set(null);
  }

  cancelClear(): void {
    this.clearingMeterId.set(null);
  }

  confirmClear(): void {
    const meterId = this.clearingMeterId();
    const note = this.resolutionNote().trim();
    if (!meterId) return;
    if (!note) {
      this.clearError.set('A resolution note is required.');
      return;
    }

    this.clearSubmitting.set(true);
    this.billingHoldService.clear(meterId, note).subscribe({
      next: () => {
        this.clearSubmitting.set(false);
        this.clearingMeterId.set(null);
        this.load();
      },
      error: (err) => {
        this.clearSubmitting.set(false);
        this.clearError.set(err?.error?.error ?? 'Could not clear this billing hold.');
      },
    });
  }

  // ------------------------------------------------------------------ Bulk clear

  protected get activeHolds(): BillingHoldSummary[] {
    return this.filtered.filter((h) => h.actualBillingBlocked);
  }

  protected isSelected(meterId: string): boolean {
    return this.selectedMeterIds().has(meterId);
  }

  toggleSelect(meterId: string): void {
    const next = new Set(this.selectedMeterIds());
    if (next.has(meterId)) next.delete(meterId);
    else next.add(meterId);
    this.selectedMeterIds.set(next);
  }

  protected get allActiveSelected(): boolean {
    const active = this.activeHolds;
    return active.length > 0 && active.every((h) => this.isSelected(h.meterId));
  }

  toggleSelectAll(): void {
    if (this.allActiveSelected) {
      this.selectedMeterIds.set(new Set());
    } else {
      this.selectedMeterIds.set(new Set(this.activeHolds.map((h) => h.meterId)));
    }
  }

  requestBulkClear(): void {
    this.bulkClearing.set(true);
    this.bulkNote.set('');
    this.bulkError.set(null);
    this.bulkResult.set(null);
  }

  cancelBulkClear(): void {
    this.bulkClearing.set(false);
  }

  confirmBulkClear(): void {
    const meterIds = [...this.selectedMeterIds()];
    const note = this.bulkNote().trim();
    if (meterIds.length === 0) return;
    if (!note) {
      this.bulkError.set('A resolution note is required.');
      return;
    }

    this.bulkSubmitting.set(true);
    this.billingHoldService.clearBulk(meterIds, note).subscribe({
      next: (results) => {
        this.bulkSubmitting.set(false);
        this.bulkClearing.set(false);
        const clearedCount = results.filter((r) => r.cleared).length;
        const skippedCount = results.length - clearedCount;
        this.bulkResult.set(
          skippedCount === 0
            ? `Cleared ${clearedCount} billing hold(s).`
            : `Cleared ${clearedCount} billing hold(s); ${skippedCount} skipped (no active hold).`,
        );
        this.load();
      },
      error: (err) => {
        this.bulkSubmitting.set(false);
        this.bulkError.set(err?.error?.error ?? 'Could not clear the selected billing holds.');
      },
    });
  }
}
