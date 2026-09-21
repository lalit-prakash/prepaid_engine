import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TariffService } from '../../../../core/services/tariff.service';
import { TariffVersionService } from '../../../../core/services/tariff-version.service';
import { AuthService } from '../../../../core/services/auth.service';
import { FixedChargeBasis, TariffDetail as TariffDetailModel, TariffLineage, VoltageLevel } from '../../../../core/models/tariff.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { changeRequestStatusLabel, changeRequestStatusTone } from '../../../../shared/utils/tariff-change-request-status';
import { TariffVersionSummary } from '../../../../core/models/tariff-version.model';
import { categoryLabel } from '../../../../shared/utils/category-label';

/** One tariff version's full real configuration (GET /api/v1/tariffs/{id}) — slabs, ToD periods
 * (if any), vend limits and the rates bills against it were calculated with — plus its version
 * lineage (GET /api/v1/tariffs/{id}/lineage) and open change requests. A tariff row is never
 * edited; changes go through the governance workflow and create a new version. */
@Component({
  selector: 'pe-tariff-detail',
  imports: [DecimalPipe, DatePipe, RouterLink, StatusBadge],
  templateUrl: './tariff-detail.html',
  styleUrl: './tariff-detail.scss',
})
export class TariffDetail implements OnInit {
  protected readonly tariff = signal<TariffDetailModel | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly categoryLabel = categoryLabel;

  protected voltageName(t: TariffDetailModel): string {
    return t.voltageLevel === VoltageLevel.LT ? 'Low Tension' : t.voltageLevel === VoltageLevel.HT ? 'High Tension' : 'Extra High Tension';
  }

  protected fixedBasis(t: TariffDetailModel): string {
    switch (t.fixedChargeBasis) {
      case FixedChargeBasis.PerKva: return 'per kVA per month';
      case FixedChargeBasis.PerKwOrHp: return 'per kW or HP per month';
      case FixedChargeBasis.None: return 'no fixed charge';
      default: return 'per kW per month';
    }
  }

  protected readonly lineage = signal<TariffLineage | null>(null);
  protected readonly lineageLoading = signal(true);
  protected readonly lineageError = signal(false);
  protected readonly changeStatusLabel = changeRequestStatusLabel;
  protected readonly changeStatusTone = changeRequestStatusTone;

  protected readonly versions = signal<TariffVersionSummary[]>([]);
  protected readonly versionsLoading = signal(true);
  protected readonly isIt = computed(() => this.auth.role() === 'IT' || this.auth.role() === 'Admin');

  constructor(
    private readonly route: ActivatedRoute,
    private readonly tariffService: TariffService,
    private readonly tariffVersionService: TariffVersionService,
    private readonly auth: AuthService,
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.tariffService.getById(id).subscribe({
      next: (tariff) => {
        this.tariff.set(tariff);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(
          err?.status === 404 ? 'This tariff could not be found.' : 'Could not load this tariff from the API.',
        );
        this.loading.set(false);
      },
    });

    this.tariffService.lineage(id).subscribe({
      next: (l) => {
        this.lineage.set(l);
        this.lineageLoading.set(false);
      },
      error: () => {
        this.lineageError.set(true);
        this.lineageLoading.set(false);
      },
    });

    this.tariffVersionService.list(id).subscribe({
      next: (versions) => {
        this.versions.set(versions);
        this.versionsLoading.set(false);
      },
      error: () => {
        this.versionsLoading.set(false);
      },
    });
  }

  /** "HH:MM:SS" -> "HH:MM" for display. */
  protected formatTime(time: string): string {
    return time.slice(0, 5);
  }
}
