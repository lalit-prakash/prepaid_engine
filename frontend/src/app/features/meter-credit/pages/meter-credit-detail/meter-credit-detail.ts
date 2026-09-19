import { OperateOnly } from '../../../../shared/directives/operate-only';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MeterCommandService } from '../../../../core/services/meter-command.service';
import { MeterCommandDetail as MeterCommandDetailModel, MeterCommandStatus } from '../../../../core/models/meter-command.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/**
 * One meter command's real detail (GET /api/v1/meter-commands/{id}), with a genuine Retry
 * action (POST .../retry) for Failed/TimedOut commands — it resets the command and dispatches
 * it again through the same IMeterCommandClient the recharge flow uses, never a fabricated
 * status flip. Destructive/state-changing per the "critical command confirmation" pattern from
 * the original UI/UX request — retry is a real operational action, so it's confirmed before firing.
 */
@Component({
  selector: 'pe-meter-credit-detail',
  imports: [OperateOnly, StatusBadge, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './meter-credit-detail.html',
  styleUrl: './meter-credit-detail.scss',
})
export class MeterCreditDetail implements OnInit {
  protected readonly command = signal<MeterCommandDetailModel | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly retrying = signal(false);
  protected readonly retryError = signal<string | null>(null);
  protected readonly confirmingRetry = signal(false);
  protected readonly MeterCommandStatus = MeterCommandStatus;

  private id = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly meterCommandService: MeterCommandService,
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.meterCommandService.getById(this.id).subscribe({
      next: (command) => {
        this.command.set(command);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(
          err?.status === 404 ? 'This meter command could not be found.' : 'Could not load this meter command from the API.',
        );
        this.loading.set(false);
      },
    });
  }

  protected get canRetry(): boolean {
    const c = this.command();
    return !!c && (c.status === MeterCommandStatus.Failed || c.status === MeterCommandStatus.TimedOut);
  }

  requestRetry(): void {
    this.confirmingRetry.set(true);
  }

  cancelRetry(): void {
    this.confirmingRetry.set(false);
  }

  confirmRetry(): void {
    this.confirmingRetry.set(false);
    this.retrying.set(true);
    this.retryError.set(null);

    this.meterCommandService.retry(this.id).subscribe({
      next: () => {
        this.retrying.set(false);
        this.load(); // re-fetch so every field (status, retryCount, sentAt, ...) reflects the real post-retry state
      },
      error: (err) => {
        this.retrying.set(false);
        this.retryError.set(err?.error?.error ?? 'Could not retry this meter command.');
      },
    });
  }
}
