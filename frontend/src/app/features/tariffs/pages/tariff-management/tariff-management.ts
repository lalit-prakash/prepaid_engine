import { DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TariffService } from '../../../../core/services/tariff.service';
import { TariffSummary } from '../../../../core/models/tariff.model';
import { categoryLabel } from '../../../../shared/utils/category-label';

/**
 * Real, API-backed Tariff Management list (GET /api/v1/tariffs) — the actual
 * tariff configuration this engine bills against, sourced from the MePDCL
 * tariff book (see docs/tariff-validation-report.md). Read-only: there is no
 * create/update endpoint yet, since a real tariff-change workflow needs
 * versioning/effective-dating/approval this project hasn't built — see
 * TariffService's doc comment.
 */
@Component({
  selector: 'pe-tariff-management',
  imports: [DecimalPipe],
  templateUrl: './tariff-management.html',
  styleUrl: './tariff-management.scss',
})
export class TariffManagement implements OnInit {
  protected readonly tariffs = signal<TariffSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly categoryLabel = categoryLabel;

  constructor(
    private readonly tariffService: TariffService,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.tariffService.list().subscribe({
      next: (tariffs) => {
        this.tariffs.set(tariffs);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load tariffs from the API.');
        this.loading.set(false);
      },
    });
  }

  open(id: string): void {
    this.router.navigate(['/tariffs', id]);
  }
}
