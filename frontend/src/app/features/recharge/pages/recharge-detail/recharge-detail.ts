import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { RechargeService } from '../../../../core/services/recharge.service';
import { RechargeDetail as RechargeDetailModel, RechargeStatus } from '../../../../core/models/recharge.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/**
 * One recharge's real detail (GET /api/v1/recharges/{id}). Shows the
 * workflow as distinct steps (RMS confirmation vs. meter credit) rather than
 * collapsing "recharge successful" into "meter credit successful" — the
 * downstream meter-credit stage has no domain model yet, so it's shown as
 * an explicit "not modeled" step, never a fabricated success.
 */
@Component({
  selector: 'pe-recharge-detail',
  imports: [StatusBadge, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './recharge-detail.html',
  styleUrl: './recharge-detail.scss',
})
export class RechargeDetail implements OnInit {
  protected readonly recharge = signal<RechargeDetailModel | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly RechargeStatus = RechargeStatus;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly rechargeService: RechargeService,
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.rechargeService.getById(id).subscribe({
      next: (recharge) => {
        this.recharge.set(recharge);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(
          err?.status === 404 ? 'This recharge could not be found.' : 'Could not load this recharge from the API.',
        );
        this.loading.set(false);
      },
    });
  }
}
