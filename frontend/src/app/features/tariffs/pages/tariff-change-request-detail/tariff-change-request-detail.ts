import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { TariffChangeRequestService } from '../../../../core/services/tariff-change-request.service';
import { TariffChangeRequestDetail, TariffChangeRequestStatus } from '../../../../core/models/tariff-change-request.model';
import { categoryLabel } from '../../../../shared/utils/category-label';
import { changeRequestStatusLabel, changeRequestStatusTone } from '../../../../shared/utils/tariff-change-request-status';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

interface ComparisonRow {
  field: string;
  current: string;
  proposed: string;
  changed: boolean;
}

/**
 * The Utility approval workspace: request details, a field-by-field Current vs Proposed
 * comparison, and Approve & Schedule / Reject actions — the only two ways a submitted change
 * ever leaves PendingApproval. IT users can view the same page read-only, and if the request is
 * Rejected can jump to the edit form to revise and resubmit. Reject always requires a reason
 * (never a silent edit of the submitted values) and Approve always requires a commencement date
 * (the change is never activated immediately) — both enforced again on the backend regardless of
 * what this page does.
 */
@Component({
  selector: 'pe-tariff-change-request-detail',
  imports: [DatePipe, FormsModule, RouterLink, StatusBadge],
  templateUrl: './tariff-change-request-detail.html',
  styleUrl: './tariff-change-request-detail.scss',
})
export class TariffChangeRequestDetailPage implements OnInit {
  protected readonly Status = TariffChangeRequestStatus;
  protected readonly categoryLabel = categoryLabel;
  protected readonly statusLabel = changeRequestStatusLabel;
  protected readonly statusTone = changeRequestStatusTone;

  protected readonly detail = signal<TariffChangeRequestDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly acting = signal(false);

  protected readonly isUtility = computed(() => this.auth.role() === 'Utility');
  protected readonly isIt = computed(() => this.auth.role() === 'IT');

  protected commencementDate = '';
  protected rejectionReason = '';

  private requestId = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly changeRequestService: TariffChangeRequestService,
    private readonly auth: AuthService,
  ) {}

  ngOnInit(): void {
    this.requestId = this.route.snapshot.paramMap.get('id') ?? '';
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.changeRequestService.getById(this.requestId).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.status === 404 ? 'This change request could not be found.' : 'Could not load this change request.');
        this.loading.set(false);
      },
    });
  }

  protected comparisonRows(): ComparisonRow[] {
    const d = this.detail();
    if (!d) return [];
    const c = d.current;
    const p = d.proposed;
    const rows: ComparisonRow[] = [
      { field: 'Name', current: c?.name ?? '—', proposed: p.proposedName, changed: c?.name !== p.proposedName },
      {
        field: 'Category',
        current: c ? categoryLabel(c.category) : '—',
        proposed: categoryLabel(p.proposedCategory),
        changed: c?.category !== p.proposedCategory,
      },
      {
        field: 'Fixed Charge (₹/unit/month)',
        current: c ? c.fixedChargePerUnitPerMonth.toFixed(2) : '—',
        proposed: p.proposedFixedChargePerUnitPerMonth.toFixed(2),
        changed: c?.fixedChargePerUnitPerMonth !== p.proposedFixedChargePerUnitPerMonth,
      },
      {
        field: 'Prepaid Rebate (%)',
        current: c ? `${c.prepaidEnergyRebatePercent}%` : '—',
        proposed: `${p.proposedPrepaidEnergyRebatePercent}%`,
        changed: c?.prepaidEnergyRebatePercent !== p.proposedPrepaidEnergyRebatePercent,
      },
      {
        field: 'Emergency Credit (₹)',
        current: c ? c.emergencyCreditLimit.toFixed(2) : '—',
        proposed: p.proposedEmergencyCreditLimit.toFixed(2),
        changed: c?.emergencyCreditLimit !== p.proposedEmergencyCreditLimit,
      },
      {
        field: 'Slabs',
        current: c ? this.formatSlabs(c.slabs) : '—',
        proposed: this.formatSlabs(p.slabs),
        changed: c ? this.formatSlabs(c.slabs) !== this.formatSlabs(p.slabs) : true,
      },
      {
        field: 'ToD Periods',
        current: c ? `${c.touPeriods.length}` : '—',
        proposed: `${p.touPeriods.length}`,
        changed: c?.touPeriods.length !== p.touPeriods.length,
      },
    ];
    return rows;
  }

  private formatSlabs(slabs: { fromKwh: number; upToKwh: number | null; ratePerKwh: number }[]): string {
    return slabs.map((s) => `${s.fromKwh}-${s.upToKwh ?? '∞'} @ ₹${s.ratePerKwh}`).join('; ');
  }

  approve(): void {
    if (!this.commencementDate) {
      this.actionError.set('A commencement date is required to approve.');
      return;
    }
    this.actionError.set(null);
    this.acting.set(true);
    this.changeRequestService.approve(this.requestId, this.commencementDate).subscribe({
      next: () => {
        this.acting.set(false);
        this.load();
      },
      error: (err) => {
        this.acting.set(false);
        this.actionError.set(err?.error?.error ?? 'Could not approve this change request.');
      },
    });
  }

  reject(): void {
    if (!this.rejectionReason.trim()) {
      this.actionError.set('A rejection reason is required.');
      return;
    }
    this.actionError.set(null);
    this.acting.set(true);
    this.changeRequestService.reject(this.requestId, this.rejectionReason).subscribe({
      next: () => {
        this.acting.set(false);
        this.load();
      },
      error: (err) => {
        this.acting.set(false);
        this.actionError.set(err?.error?.error ?? 'Could not reject this change request.');
      },
    });
  }

  reviseAndResubmit(): void {
    this.router.navigate(['/tariffs/change-requests', this.requestId, 'edit']);
  }
}
