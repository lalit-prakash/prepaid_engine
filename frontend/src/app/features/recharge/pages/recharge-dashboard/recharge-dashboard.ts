import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { RechargeService } from '../../../../core/services/recharge.service';
import { RechargeStatus, RechargeSummary } from '../../../../core/models/recharge.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed Recharge Operations dashboard (GET /api/v1/recharges) —
 * every recharge attempt across all consumers. "Success" here means RMS
 * confirmed the payment; it does not imply a meter-credit command was ever
 * sent, since that workflow has no domain model yet (see docs/frontend-scope.md).
 */
@Component({
  selector: 'pe-recharge-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe],
  templateUrl: './recharge-dashboard.html',
  styleUrl: './recharge-dashboard.scss',
})
export class RechargeDashboard implements OnInit {
  protected readonly recharges = signal<RechargeSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly RechargeStatus = RechargeStatus;

  constructor(
    private readonly rechargeService: RechargeService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    // Prefills from a cross-link (e.g. Consumer 360's Recharge panel — "View recharge
    // history") — real reuse of this page's own search, not a separate filtered endpoint.
    const initialQuery = this.route.snapshot.queryParamMap.get('q');
    if (initialQuery) this.searchTerm.set(initialQuery);

    this.rechargeService.list().subscribe({
      next: (recharges) => {
        this.recharges.set(recharges);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load recharges from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): RechargeSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.recharges();
    return this.recharges().filter(
      (r) =>
        r.accountNumber.toLowerCase().includes(term) ||
        r.name.toLowerCase().includes(term) ||
        r.rmsReferenceId.toLowerCase().includes(term),
    );
  }

  protected get successCount(): number {
    return this.recharges().filter((r) => r.status === RechargeStatus.Success).length;
  }
  protected get failedCount(): number {
    return this.recharges().filter((r) => r.status === RechargeStatus.Failed).length;
  }
  protected get pendingCount(): number {
    return this.recharges().filter((r) => r.status === RechargeStatus.Initiated).length;
  }
  protected get reversedCount(): number {
    return this.recharges().filter((r) => r.status === RechargeStatus.Reversed).length;
  }
  protected get totalAmount(): number {
    return this.recharges()
      .filter((r) => r.status === RechargeStatus.Success)
      .reduce((sum, r) => sum + r.amount, 0);
  }
  protected get successRate(): string {
    if (this.recharges().length === 0) return '—';
    return `${((this.successCount / this.recharges().length) * 100).toFixed(1)}%`;
  }

  open(id: string): void {
    this.router.navigate(['/recharge', id]);
  }
}
