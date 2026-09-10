import { DatePipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ConnectivityCommandService } from '../../../../core/services/connectivity-command.service';
import {
  ConnectivityCommandStatus,
  ConnectivityCommandSummary,
  ConnectivityCommandType,
} from '../../../../core/models/connectivity-command.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed RC/DC dashboard (GET /api/v1/connectivity-commands) — every disconnect/
 * reconnect command across all consumers. A command only ever means "the consumer's connection
 * actually changed" once it reaches Acknowledged — see ConnectivityCommand's doc comment on the
 * backend, and Consumer.ConnectionStatus for the distinction between intent and reality.
 */
@Component({
  selector: 'pe-rc-dc-dashboard',
  imports: [StatusBadge, KpiCard, DatePipe],
  templateUrl: './rc-dc-dashboard.html',
  styleUrl: './rc-dc-dashboard.scss',
})
export class RcDcDashboard implements OnInit {
  protected readonly commands = signal<ConnectivityCommandSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly ConnectivityCommandStatus = ConnectivityCommandStatus;
  protected readonly ConnectivityCommandType = ConnectivityCommandType;

  constructor(
    private readonly connectivityCommandService: ConnectivityCommandService,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.connectivityCommandService.list().subscribe({
      next: (commands) => {
        this.commands.set(commands);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load connectivity commands from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): ConnectivityCommandSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.commands();
    return this.commands().filter(
      (c) => c.accountNumber.toLowerCase().includes(term) || c.name.toLowerCase().includes(term),
    );
  }

  protected get disconnectCount(): number {
    return this.commands().filter((c) => c.commandType === ConnectivityCommandType.Disconnect).length;
  }
  protected get reconnectCount(): number {
    return this.commands().filter((c) => c.commandType === ConnectivityCommandType.Reconnect).length;
  }
  protected get acknowledgedCount(): number {
    return this.commands().filter((c) => c.status === ConnectivityCommandStatus.Acknowledged).length;
  }
  protected get failedCount(): number {
    return this.commands().filter((c) => c.status === ConnectivityCommandStatus.Failed).length;
  }
  protected get timedOutCount(): number {
    return this.commands().filter((c) => c.status === ConnectivityCommandStatus.TimedOut).length;
  }
  protected get pendingCount(): number {
    return this.commands().filter(
      (c) => c.status === ConnectivityCommandStatus.Queued || c.status === ConnectivityCommandStatus.Sent,
    ).length;
  }
  protected get successRate(): string {
    if (this.commands().length === 0) return '—';
    return `${((this.acknowledgedCount / this.commands().length) * 100).toFixed(1)}%`;
  }

  open(id: string): void {
    this.router.navigate(['/rc-dc', id]);
  }
}
