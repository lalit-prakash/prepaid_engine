import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TariffService } from '../../../../core/services/tariff.service';
import { TariffChangeRequestService } from '../../../../core/services/tariff-change-request.service';
import { AuthService } from '../../../../core/services/auth.service';
import { BookCheck, EnergyUnit, FixedChargeBasis, FppasRate, TariffParameters, TariffSummary, VoltageLevel } from '../../../../core/models/tariff.model';
import { TariffChangeRequestStatus, TariffChangeRequestSummary } from '../../../../core/models/tariff-change-request.model';
import { categoryLabel } from '../../../../shared/utils/category-label';
import { changeRequestStatusLabel, changeRequestStatusTone } from '../../../../shared/utils/tariff-change-request-status';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

type ChangeRequestTab = 'pending' | 'drafts' | 'scheduled' | 'rejected' | 'history';
type Tab = 'active-tariffs' | 'book' | ChangeRequestTab;
type VoltageFilter = 'all' | VoltageLevel;

/**
 * Tariffs &amp; Parameters. The active tariffs (GET /api/v1/tariffs) laid out by supply voltage with their schedule code, energy rate, fixed charge and
 * how each compares with the FY 2026-27 tariff book (GET /api/v1/tariffs/book-check); the book's fixed figures (GET /api/v1/tariff-parameters); and the
 * tariff governance workflow (IT drafts, Utility approves) that is the only way to change a tariff. A tariff that differs from the book is shown, never
 * silently corrected: changing it goes through a revision.
 */
@Component({
  selector: 'pe-tariff-management',
  imports: [DecimalPipe, DatePipe, RouterLink, FormsModule, StatusBadge, KpiCard],
  templateUrl: './tariff-management.html',
  styleUrl: './tariff-management.scss',
})
export class TariffManagement implements OnInit {
  protected readonly tariffs = signal<TariffSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly categoryLabel = categoryLabel;
  protected readonly statusLabel = changeRequestStatusLabel;
  protected readonly statusTone = changeRequestStatusTone;
  protected readonly Status = TariffChangeRequestStatus;
  protected readonly VoltageLevel = VoltageLevel;

  protected readonly activeTab = signal<Tab>('active-tariffs');
  protected readonly retiredTariffs = signal<TariffSummary[]>([]);
  protected readonly changeRequests = signal<TariffChangeRequestSummary[]>([]);
  protected readonly changeRequestsLoading = signal(true);
  protected readonly changeRequestsError = signal<string | null>(null);

  protected readonly bookCheck = signal<BookCheck | null>(null);
  protected readonly parameters = signal<TariffParameters | null>(null);
  protected readonly fppasRates = signal<FppasRate[]>([]);
  protected readonly fppasSaving = signal(false);
  protected readonly fppasError = signal<string | null>(null);
  protected fppasRateInput: number | null = null;
  protected readonly bookError = signal<string | null>(null);

  protected readonly voltageFilter = signal<VoltageFilter>('all');
  protected search = '';
  protected readonly searchTerm = signal('');

  protected readonly isIt = computed(() => this.auth.role() === 'IT' || this.auth.role() === 'Admin');
  protected readonly isUtility = computed(() => this.auth.role() === 'Utility');
  protected readonly canGovernTariffs = computed(() => this.auth.canGovernTariffs());

  private count(status: TariffChangeRequestStatus): number {
    return this.changeRequests().filter((r) => r.status === status).length;
  }
  protected readonly pendingCount = computed(() => this.count(TariffChangeRequestStatus.PendingApproval));
  protected readonly draftCount = computed(() => this.count(TariffChangeRequestStatus.Draft));
  protected readonly scheduledCount = computed(() => this.count(TariffChangeRequestStatus.Scheduled));
  protected readonly rejectedCount = computed(() => this.count(TariffChangeRequestStatus.Rejected));

  /** Book status by tariff id, so the table can show a chip per row. */
  private readonly bookStatusById = computed(() => {
    const map = new Map<string, { status: string; differences: { field: string; book: string; current: string }[] }>();
    for (const r of this.bookCheck()?.rows ?? []) if (r.tariffId) map.set(r.tariffId, { status: r.status, differences: r.differences });
    return map;
  });

  protected readonly visibleTariffs = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const v = this.voltageFilter();
    return this.tariffs()
      .filter((t) => (v === 'all' || t.voltageLevel === v) && (!term || t.name.toLowerCase().includes(term) || (t.scheduleCode ?? '').toLowerCase().includes(term) || categoryLabel(t.category).toLowerCase().includes(term)))
      .sort((a, b) => a.voltageLevel - b.voltageLevel || (a.scheduleCode ?? 'zz').localeCompare(b.scheduleCode ?? 'zz') || a.name.localeCompare(b.name));
  });

  protected readonly attentionRows = computed(() => (this.bookCheck()?.rows ?? []).filter((r) => r.status !== 'Matches'));

  protected readonly visibleChangeRequests = computed(() => {
    const all = this.changeRequests();
    switch (this.activeTab()) {
      case 'pending':
        return all.filter((r) => r.status === TariffChangeRequestStatus.PendingApproval);
      case 'drafts':
        return all.filter((r) => r.status === TariffChangeRequestStatus.Draft);
      case 'scheduled':
        return all.filter((r) => r.status === TariffChangeRequestStatus.Scheduled);
      case 'rejected':
        return all.filter((r) => r.status === TariffChangeRequestStatus.Rejected);
      case 'history':
        return all.filter((r) => r.status === TariffChangeRequestStatus.Activated || r.status === TariffChangeRequestStatus.Cancelled);
      default:
        return [];
    }
  });

  constructor(
    private readonly tariffService: TariffService,
    private readonly changeRequestService: TariffChangeRequestService,
    private readonly auth: AuthService,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.tariffService.list('Active').subscribe({
      next: (tariffs) => {
        this.tariffs.set(tariffs);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load tariffs from the API.');
        this.loading.set(false);
      },
    });
    this.tariffService.list('Retired').subscribe({ next: (rows) => this.retiredTariffs.set(rows), error: () => this.retiredTariffs.set([]) });
    this.tariffService.bookCheck().subscribe({ next: (b) => this.bookCheck.set(b), error: () => this.bookError.set('Could not compare with the tariff book.') });
    this.tariffService.parameters().subscribe({ next: (p) => this.parameters.set(p), error: () => this.parameters.set(null) });
    this.loadFppasRates();
    this.changeRequestService.list().subscribe({
      next: (requests) => {
        this.changeRequests.set(requests);
        this.changeRequestsLoading.set(false);
      },
      error: () => {
        this.changeRequestsError.set('Could not load tariff change requests from the API.');
        this.changeRequestsLoading.set(false);
      },
    });
  }

  selectTab(tab: Tab): void {
    this.activeTab.set(tab);
  }

  private loadFppasRates(): void {
    this.tariffService.fppasRates().subscribe({ next: (rates) => this.fppasRates.set(rates), error: () => this.fppasRates.set([]) });
  }

  protected notifyFppasRate(): void {
    if (this.fppasRateInput === null || Number.isNaN(this.fppasRateInput)) return;
    this.fppasSaving.set(true);
    this.fppasError.set(null);
    this.tariffService.notifyFppasRate(this.fppasRateInput / 100).subscribe({
      next: () => {
        this.fppasSaving.set(false);
        this.fppasRateInput = null;
        this.loadFppasRates();
      },
      error: (err) => {
        this.fppasSaving.set(false);
        this.fppasError.set(err?.error?.error ?? 'Could not notify this FPPAS rate.');
      },
    });
  }

  applySearch(): void {
    this.searchTerm.set(this.search);
  }

  protected voltageLabel(v: VoltageLevel): string {
    return v === VoltageLevel.LT ? 'LT' : v === VoltageLevel.HT ? 'HT' : 'EHT';
  }

  /** The energy rate as the book quotes it: one rate, the slab rates, or the Time-of-Day normal rate. */
  protected energyRate(t: TariffSummary): string {
    const unit = t.energyUnit === EnergyUnit.Kvah ? 'kVAh' : 'kWh';
    if (t.slabCount === 0 && t.normalTouRate !== null) return `₹${t.normalTouRate.toFixed(2)} / ${unit} (Time-of-Day)`;
    if (t.firstSlabRate === null) return '—';
    return t.slabCount > 1 ? `from ₹${t.firstSlabRate.toFixed(2)} / ${unit} (${t.slabCount} slabs)` : `₹${t.firstSlabRate.toFixed(2)} / ${unit}`;
  }

  /** The fixed charge with its basis, for example "₹90 / kW / month". */
  protected fixedCharge(t: TariffSummary): string {
    if (t.fixedChargeBasis === FixedChargeBasis.None || t.fixedChargePerUnitPerMonth === 0) return 'None';
    const unit = t.fixedChargeBasis === FixedChargeBasis.PerKva ? 'kVA' : t.fixedChargeBasis === FixedChargeBasis.PerKwOrHp ? 'kW or HP' : 'kW';
    return `₹${t.fixedChargePerUnitPerMonth.toLocaleString('en-IN', { maximumFractionDigits: 2 })} / ${unit} / month`;
  }

  protected bookStatus(t: TariffSummary): { status: string; differences: { field: string; book: string; current: string }[] } | null {
    return this.bookStatusById().get(t.id) ?? null;
  }

  protected differenceText(t: TariffSummary): string {
    return (this.bookStatus(t)?.differences ?? []).map((d) => `${d.field}: book ${d.book}, current ${d.current}`).join('\n');
  }

  open(id: string): void {
    this.router.navigate(['/tariffs', id]);
  }

  openChangeRequest(id: string): void {
    this.router.navigate(['/tariffs/change-requests', id]);
  }

  reviseTariff(tariffId: string): void {
    this.router.navigate(['/tariffs/change-requests/new'], { queryParams: { supersedes: tariffId } });
  }
}
