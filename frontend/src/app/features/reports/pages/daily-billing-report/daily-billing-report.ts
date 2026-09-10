import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { BillService } from '../../../../core/services/bill.service';
import { BillStatus } from '../../../../core/models/consumer.model';
import { BillSummary } from '../../../../core/models/bill.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { categoryLabel } from '../../../../shared/utils/category-label';
import { exportToCsv } from '../../../../shared/utils/csv-export';

/**
 * Daily Billing Report — real data (GET /api/v1/bills) filtered by date range/status/search,
 * with a summary and CSV export. No invented KPI: consumption totals aren't shown here since
 * BillSummary doesn't carry consumption (only Bill Detail does) — better to omit a figure than
 * show a wrong one.
 */
@Component({
  selector: 'pe-daily-billing-report',
  imports: [FormsModule, StatusBadge, KpiCard, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './daily-billing-report.html',
  styleUrl: './daily-billing-report.scss',
})
export class DailyBillingReport implements OnInit {
  protected readonly bills = signal<BillSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly generatedAt = signal<Date | null>(null);

  protected fromDate = '';
  protected toDate = '';
  protected statusFilter = 'all';
  protected searchTerm = '';

  protected readonly BillStatus = BillStatus;
  protected readonly categoryLabel = categoryLabel;

  constructor(private readonly billService: BillService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.billService.list().subscribe({
      next: (bills) => {
        this.bills.set(bills);
        this.generatedAt.set(new Date());
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load bills from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): BillSummary[] {
    const term = this.searchTerm.trim().toLowerCase();
    const from = this.fromDate ? new Date(this.fromDate) : null;
    const to = this.toDate ? new Date(this.toDate) : null;

    return this.bills().filter((b) => {
      const generated = new Date(b.generatedAt);
      if (from && generated < from) return false;
      if (to && generated > new Date(to.getTime() + 24 * 60 * 60 * 1000 - 1)) return false;
      if (this.statusFilter !== 'all' && String(b.status) !== this.statusFilter) return false;
      if (term && !b.accountNumber.toLowerCase().includes(term) && !b.name.toLowerCase().includes(term)) {
        return false;
      }
      return true;
    });
  }

  protected get totalConsumers(): number {
    return new Set(this.filtered.map((b) => b.accountNumber)).size;
  }
  protected get totalCharges(): number {
    return this.filtered.reduce((sum, b) => sum + b.amount, 0);
  }
  protected get billedCount(): number {
    return this.filtered.filter((b) => b.status !== BillStatus.Cancelled).length;
  }
  protected get failedCount(): number {
    return this.filtered.filter((b) => b.status === BillStatus.Overdue).length;
  }

  resetFilters(): void {
    this.fromDate = '';
    this.toDate = '';
    this.statusFilter = 'all';
    this.searchTerm = '';
  }

  export(): void {
    exportToCsv(
      `daily-billing-report-${new Date().toISOString().slice(0, 10)}.csv`,
      ['Generated', 'Account', 'Consumer', 'Category', 'Tariff', 'Net Energy', 'Fixed', 'Duty', 'FPPAS', 'Total', 'Status'],
      this.filtered.map((b) => [
        b.generatedAt,
        b.accountNumber,
        b.name,
        categoryLabel(b.category),
        b.tariffName,
        b.energyChargeNet.toFixed(2),
        b.fixedCharge.toFixed(2),
        b.electricityDutyAmount.toFixed(2),
        b.fppasAmount.toFixed(2),
        b.amount.toFixed(2),
        BillStatus[b.status] ?? String(b.status),
      ]),
    );
  }
}
