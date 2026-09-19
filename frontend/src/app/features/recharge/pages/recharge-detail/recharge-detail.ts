import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { RechargeService } from '../../../../core/services/recharge.service';
import {
  MeterCommandStatus,
  RechargeDetail as RechargeDetailModel,
  RechargeStatus,
} from '../../../../core/models/recharge.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/**
 * One recharge's real detail (GET /api/v1/recharges/{id}). Shows the workflow as distinct
 * steps (RMS confirmation vs. meter credit) rather than collapsing "recharge successful" into
 * "meter credit successful" — the two are tracked by entirely separate lifecycles (RechargeStatus
 * vs. MeterCommandStatus) now that the meter-credit flow is wired in, and this page renders
 * whichever real MeterCommand exists for this recharge (or its absence, for a recharge that
 * never reached RMS Success).
 */
@Component({
  selector: 'pe-recharge-detail',
  imports: [StatusBadge, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './recharge-detail.html',
  styleUrl: './recharge-detail.scss',
})
export class RechargeDetail implements OnInit, OnDestroy {
  protected readonly recharge = signal<RechargeDetailModel | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly RechargeStatus = RechargeStatus;
  protected readonly MeterCommandStatus = MeterCommandStatus;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly rechargeService: RechargeService,
  ) {}

  private id = '';
  private followUp?: ReturnType<typeof setTimeout>;
  private followUps = 0;

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    this.load();
  }

  ngOnDestroy(): void {
    clearTimeout(this.followUp);
  }

  private load(): void {
    this.rechargeService.getById(this.id).subscribe({
      next: (recharge) => {
        this.recharge.set(recharge);
        this.loading.set(false);
        // The meter credit is sent by a background worker within seconds of the recharge: look again a few times.
        const status = recharge.meterCommand?.status;
        if ((status === MeterCommandStatus.Queued || status === MeterCommandStatus.Sent) && this.followUps++ < 10) {
          this.followUp = setTimeout(() => this.load(), 3000);
        }
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
