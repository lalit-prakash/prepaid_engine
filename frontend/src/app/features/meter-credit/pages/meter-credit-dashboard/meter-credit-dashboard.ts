import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MeterCommandService } from '../../../../core/services/meter-command.service';
import { MeterCommandStatus, MeterCommandSummary } from '../../../../core/models/meter-command.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed Meter Credit dashboard (GET /api/v1/meter-commands) — every meter credit
 * command across all consumers, each traced back to the recharge that triggered it. A command
 * only ever means "the meter was actually credited" once it reaches Acknowledged — see
 * MeterCommand's doc comment on the backend.
 */
@Component({
  selector: 'pe-meter-credit-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe],
  templateUrl: './meter-credit-dashboard.html',
  styleUrl: './meter-credit-dashboard.scss',
})
export class MeterCreditDashboard implements OnInit {
  protected readonly commands = signal<MeterCommandSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');
  protected readonly MeterCommandStatus = MeterCommandStatus;

  constructor(
    private readonly meterCommandService: MeterCommandService,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.meterCommandService.list().subscribe({
      next: (commands) => {
        this.commands.set(commands);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load meter commands from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filtered(): MeterCommandSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.commands();
    return this.commands().filter(
      (c) =>
        c.accountNumber.toLowerCase().includes(term) ||
        c.name.toLowerCase().includes(term) ||
        c.rmsReferenceId.toLowerCase().includes(term),
    );
  }

  protected get acknowledgedCount(): number {
    return this.commands().filter((c) => c.status === MeterCommandStatus.Acknowledged).length;
  }
  protected get failedCount(): number {
    return this.commands().filter((c) => c.status === MeterCommandStatus.Failed).length;
  }
  protected get timedOutCount(): number {
    return this.commands().filter((c) => c.status === MeterCommandStatus.TimedOut).length;
  }
  protected get pendingCount(): number {
    return this.commands().filter(
      (c) => c.status === MeterCommandStatus.Queued || c.status === MeterCommandStatus.Sent,
    ).length;
  }
  protected get retriedCount(): number {
    return this.commands().filter((c) => c.retryCount > 0).length;
  }
  protected get successRate(): string {
    if (this.commands().length === 0) return '—';
    return `${((this.acknowledgedCount / this.commands().length) * 100).toFixed(1)}%`;
  }

  open(id: string): void {
    this.router.navigate(['/meter-credit', id]);
  }
}
