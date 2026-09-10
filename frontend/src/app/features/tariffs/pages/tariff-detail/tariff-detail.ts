import { DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TariffService } from '../../../../core/services/tariff.service';
import { TariffDetail as TariffDetailModel } from '../../../../core/models/tariff.model';
import { categoryLabel } from '../../../../shared/utils/category-label';

/** One tariff's full real configuration (GET /api/v1/tariffs/{id}) — slabs, ToD periods
 * (if any), vend limits, and the rates this engine actually bills against. */
@Component({
  selector: 'pe-tariff-detail',
  imports: [DecimalPipe, RouterLink],
  templateUrl: './tariff-detail.html',
  styleUrl: './tariff-detail.scss',
})
export class TariffDetail implements OnInit {
  protected readonly tariff = signal<TariffDetailModel | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly categoryLabel = categoryLabel;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly tariffService: TariffService,
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
  }

  /** "HH:MM:SS" -> "HH:MM" for display. */
  protected formatTime(time: string): string {
    return time.slice(0, 5);
  }
}
