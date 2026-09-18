import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TariffService } from '../../../../core/services/tariff.service';
import { TariffVersionService } from '../../../../core/services/tariff-version.service';
import { AuthService } from '../../../../core/services/auth.service';
import { TariffDetail as TariffDetailModel } from '../../../../core/models/tariff.model';
import { TariffVersionSummary } from '../../../../core/models/tariff-version.model';
import { categoryLabel } from '../../../../shared/utils/category-label';

/** One tariff's full real configuration (GET /api/v1/tariffs/{id}) — slabs, ToD periods
 * (if any), vend limits, and the rates this engine actually bills against — plus its recorded
 * version history (GET /api/v1/tariffs/{id}/versions), if any parameter changes have been
 * logged. `Tariff` itself has no update endpoint yet, so a version here reflects a change
 * recorded independently, not something this page can trigger. */
@Component({
  selector: 'pe-tariff-detail',
  imports: [DecimalPipe, DatePipe, RouterLink],
  templateUrl: './tariff-detail.html',
  styleUrl: './tariff-detail.scss',
})
export class TariffDetail implements OnInit {
  protected readonly tariff = signal<TariffDetailModel | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly categoryLabel = categoryLabel;

  protected readonly versions = signal<TariffVersionSummary[]>([]);
  protected readonly versionsLoading = signal(true);
  protected readonly isIt = computed(() => this.auth.role() === 'IT');

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
