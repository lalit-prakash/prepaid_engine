import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { BillService } from '../../../../core/services/bill.service';
import { BillStatus } from '../../../../core/models/consumer.model';
import { BillSummary, ConsumerCategory } from '../../../../core/models/bill.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

const CATEGORY_LABELS: Record<ConsumerCategory, string> = {
  [ConsumerCategory.Domestic]: 'Domestic',
  [ConsumerCategory.Bpl]: 'BPL / Kutir Jyoti',
  [ConsumerCategory.Industrial]: 'Industrial',
  [ConsumerCategory.Commercial]: 'Commercial',
};

/**
 * Real, API-backed billing dashboard (GET /api/v1/bills) — every bill ever
 * generated across all consumers, joined with tariff and category. KPIs are
 * derived from this same real data; nothing here is illustrative.
 */
@Component({
  selector: 'pe-billing-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe],
  templateUrl: './billing-dashboard.html',
  styleUrl: './billing-dashboard.scss',
})
export class BillingDashboard implements OnInit {
  protected readonly bills = signal<BillSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly BillStatus = BillStatus;
  protected readonly categoryLabel = (c: ConsumerCategory) => CATEGORY_LABELS[c] ?? 'Unknown';

  constructor(
    private readonly billService: BillService,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.billService.list().subscribe({
      next: (bills) => {
        this.bills.set(bills);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load bills from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): BillSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.bills();
    return this.bills().filter(
      (b) =>
        b.accountNumber.toLowerCase().includes(term) ||
        b.name.toLowerCase().includes(term) ||
        b.tariffName.toLowerCase().includes(term),
    );
  }

  protected get totalCharges(): number {
    return this.bills().reduce((sum, b) => sum + b.amount, 0);
  }

  protected get paidCount(): number {
    return this.bills().filter((b) => b.status === BillStatus.Paid).length;
  }

  protected get pendingCount(): number {
    return this.bills().filter(
      (b) => b.status === BillStatus.Generated || b.status === BillStatus.PartiallyPaid,
    ).length;
  }

  protected get overdueCount(): number {
    return this.bills().filter((b) => b.status === BillStatus.Overdue).length;
  }

  open(id: string): void {
    this.router.navigate(['/billing', id]);
  }
}
