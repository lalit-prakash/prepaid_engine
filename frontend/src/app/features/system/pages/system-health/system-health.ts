import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SystemService } from '../../../../core/services/system.service';
import { SystemHealth } from '../../../../core/models/system.model';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/**
 * System Health: what is actually running, measured by the API. It times a real database round trip, shows when each
 * background worker last succeeded or failed, and counts the queues that would back up if something stopped. The page
 * refreshes itself every 15 seconds. Nothing here is simulated; worker state is per API instance and starts empty
 * after a restart.
 */
@Component({
  selector: 'pe-system-health',
  imports: [KpiCard, StatusBadge, DatePipe, DecimalPipe, RouterLink],
  templateUrl: './system-health.html',
  styleUrl: './system-health.scss',
})
export class SystemHealthPage implements OnInit, OnDestroy {
  protected readonly health = signal<SystemHealth | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  private timer?: ReturnType<typeof setInterval>;

  constructor(private readonly systemService: SystemService) {}

  ngOnInit(): void {
    this.load();
    this.timer = setInterval(() => this.load(), 15_000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected load(): void {
    this.systemService.health().subscribe({
      next: (h) => {
        this.health.set(h);
        this.error.set(null);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not reach the API to read its health.');
        this.loading.set(false);
      },
    });
  }

  protected tone(state: string): 'success' | 'warning' | 'danger' | 'neutral' {
    switch (state) {
      case 'Healthy': return 'success';
      case 'Degraded':
      case 'Stale': return 'warning';
      case 'Down':
      case 'Failing': return 'danger';
      default: return 'neutral';
    }
  }

  protected workerLabel(state: string): string {
    return state === 'NotYetRun' ? 'Not yet run' : state;
  }

  protected uptime(seconds: number): string {
    const d = Math.floor(seconds / 86400), h = Math.floor((seconds % 86400) / 3600), m = Math.floor((seconds % 3600) / 60);
    return d > 0 ? `${d}d ${h}h` : h > 0 ? `${h}h ${m}m` : `${m}m`;
  }

  protected age(seconds: number | null): string {
    if (seconds === null) return 'none waiting';
    return seconds < 90 ? `${seconds} s` : `${Math.round(seconds / 60)} min`;
  }

  protected runLabel(type: string): string {
    return type === 'DLP_STAGE1' ? 'DLP Stage 1' : type === 'DLP_STAGE2' ? 'DLP Stage 2' : type;
  }
}
