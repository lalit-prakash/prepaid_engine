import { DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ConnectivityCommandService } from '../../../../core/services/connectivity-command.service';
import {
  ConnectivityCommandDetail as ConnectivityCommandDetailModel,
  ConnectivityCommandStatus,
  ConnectivityCommandType,
} from '../../../../core/models/connectivity-command.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/**
 * One connectivity command's real detail (GET /api/v1/connectivity-commands/{id}), with a
 * genuine Retry action (POST .../retry) for Failed/TimedOut commands — it resets the command and
 * dispatches it again through the same IConnectivityCommandClient the original disconnect/
 * reconnect used, never a fabricated status flip. Destructive/state-changing per the "critical
 * command confirmation" pattern — retry is a real operational action, so it's confirmed before firing.
 */
@Component({
  selector: 'pe-rc-dc-detail',
  imports: [StatusBadge, DatePipe, RouterLink],
  templateUrl: './rc-dc-detail.html',
  styleUrl: './rc-dc-detail.scss',
})
export class RcDcDetail implements OnInit {
  protected readonly command = signal<ConnectivityCommandDetailModel | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly retrying = signal(false);
  protected readonly retryError = signal<string | null>(null);
  protected readonly confirmingRetry = signal(false);
  protected readonly ConnectivityCommandStatus = ConnectivityCommandStatus;
  protected readonly ConnectivityCommandType = ConnectivityCommandType;

  private id = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly connectivityCommandService: ConnectivityCommandService,
  ) {}

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.connectivityCommandService.getById(this.id).subscribe({
      next: (command) => {
        this.command.set(command);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(
          err?.status === 404
            ? 'This connectivity command could not be found.'
            : 'Could not load this connectivity command from the API.',
        );
        this.loading.set(false);
      },
    });
  }

  protected get canRetry(): boolean {
    const c = this.command();
    return (
      !!c &&
      (c.status === ConnectivityCommandStatus.Failed || c.status === ConnectivityCommandStatus.TimedOut)
    );
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

    this.connectivityCommandService.retry(this.id).subscribe({
      next: () => {
        this.retrying.set(false);
        this.load(); // re-fetch so every field (status, retryCount, sentAt, ...) reflects the real post-retry state
      },
      error: (err) => {
        this.retrying.set(false);
        this.retryError.set(err?.error?.error ?? 'Could not retry this connectivity command.');
      },
    });
  }
}
