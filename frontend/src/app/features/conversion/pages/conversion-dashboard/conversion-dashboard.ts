import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConversionService } from '../../../../core/services/conversion.service';
import { ConversionConsumerType, ConversionStatus, ConversionSummary } from '../../../../core/models/conversion.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed Conversion dashboard (GET /api/v1/conversions) — every postpaid-to-prepaid
 * conversion request RMS has pushed, per the AMISP integration requirement doc §1. RMS submits
 * these as a batch from its own side; this page is read-only (the decision trail is applied
 * synchronously by the backend the moment a request arrives — see the doc comment on
 * ConversionRequest). A row only ever means "the consumer's billing mode actually changed" once
 * its status reaches Completed.
 */
@Component({
  selector: 'pe-conversion-dashboard',
  imports: [StatusBadge, KpiCard, DatePipe, DecimalPipe, RouterLink],
  templateUrl: './conversion-dashboard.html',
  styleUrl: './conversion-dashboard.scss',
})
export class ConversionDashboard implements OnInit {
  protected readonly conversions = signal<ConversionSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly ConversionStatus = ConversionStatus;
  protected readonly ConversionConsumerType = ConversionConsumerType;

  constructor(private readonly conversionService: ConversionService) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.conversionService.list().subscribe({
      next: (conversions) => {
        this.conversions.set(conversions);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load conversion requests from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): ConversionSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.conversions();
    return this.conversions().filter(
      (c) =>
        c.accountNumber.toLowerCase().includes(term) ||
        c.name.toLowerCase().includes(term) ||
        c.transactionId.toLowerCase().includes(term),
    );
  }

  protected get completedCount(): number {
    return this.conversions().filter((c) => c.status === ConversionStatus.Completed).length;
  }
  protected get rejectedCount(): number {
    return this.conversions().filter((c) => c.status === ConversionStatus.Rejected).length;
  }
  protected get pendingCount(): number {
    return this.conversions().filter(
      (c) => c.status === ConversionStatus.Requested || c.status === ConversionStatus.Approved,
    ).length;
  }
  protected get successRate(): string {
    if (this.conversions().length === 0) return '—';
    return `${((this.completedCount / this.conversions().length) * 100).toFixed(1)}%`;
  }

  /** Total FOA+DIA actually credited into consumers' wallets so far — zero for any conversion
   * whose outstanding balance exceeded the Rs. 10,000 zeroing threshold, per RMS's own rule. */
  protected get totalFoaDiaCredited(): number {
    return this.conversions()
      .filter((c) => c.status === ConversionStatus.Completed)
      .reduce((sum, c) => sum + c.foaAmount + c.diaAmount, 0);
  }

  protected consumerTypeLabel(type: ConversionConsumerType): string {
    switch (type) {
      case ConversionConsumerType.Vip: return 'VIP';
      case ConversionConsumerType.Hospital: return 'Hospital';
      case ConversionConsumerType.School: return 'School';
      case ConversionConsumerType.ShoppingComplex: return 'Shopping Complex';
      case ConversionConsumerType.Other: return 'Other';
      default: return 'Residential';
    }
  }
}
