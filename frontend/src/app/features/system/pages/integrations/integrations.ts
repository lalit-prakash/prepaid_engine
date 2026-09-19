import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SystemService } from '../../../../core/services/system.service';
import { SystemIntegrations } from '../../../../core/models/system.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/**
 * Integrations: the systems this one talks to. Outbound adapters are listed with the class behind each and whether it
 * is a simulator (Mock) or live, plus what the database shows it has done recently. Inbound feeds (MDMS data and RMS
 * pushes) are pushed to this system, so the honest signal is when each last arrived. All read from
 * GET /api/v1/system/integrations. While an adapter is a Mock, results from it are simulated, not real meter or
 * payment activity.
 */
@Component({
  selector: 'pe-integrations',
  imports: [StatusBadge, DatePipe, DecimalPipe, RouterLink],
  templateUrl: './integrations.html',
  styleUrl: './integrations.scss',
})
export class Integrations implements OnInit {
  protected readonly data = signal<SystemIntegrations | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  constructor(private readonly systemService: SystemService) {}

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.systemService.integrations().subscribe({
      next: (d) => { this.data.set(d); this.error.set(null); this.loading.set(false); },
      error: () => { this.error.set('Could not load integrations from the API.'); this.loading.set(false); },
    });
  }

  protected get anyMock(): boolean {
    return !!this.data()?.outbound.some((o) => o.mode === 'Mock');
  }
}
