import { DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { SlaService } from '../../../../core/services/sla.service';
import { RiskIndicatorsSummary, SlaMetric } from '../../../../core/models/sla.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed SLA Monitoring dashboard (GET /api/v1/sla, GET /api/v1/risk-indicators).
 * Every target is configurable on the backend (appsettings.json), and a workflow with zero
 * completed samples shows "Unavailable" rather than a fabricated percentage — see SlaMetric's
 * own doc comment. The risk indicators are real open-condition counts, deliberately never a
 * monetary "revenue protected" figure.
 */
@Component({
  selector: 'pe-sla-monitoring-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe],
  templateUrl: './sla-monitoring-dashboard.html',
  styleUrl: './sla-monitoring-dashboard.scss',
})
export class SlaMonitoringDashboard implements OnInit {
  protected readonly metrics = signal<SlaMetric[]>([]);
  protected readonly slaLoading = signal(true);
  protected readonly slaError = signal<string | null>(null);

  protected readonly riskIndicators = signal<RiskIndicatorsSummary | null>(null);
  protected readonly riskLoading = signal(true);
  protected readonly riskError = signal<string | null>(null);

  constructor(private readonly slaService: SlaService) {}

  ngOnInit(): void {
    this.slaService.getSlaSummary().subscribe({
      next: (metrics) => { this.metrics.set(metrics); this.slaLoading.set(false); },
      error: () => { this.slaError.set('Could not load SLA data from the API.'); this.slaLoading.set(false); },
    });

    this.slaService.getRiskIndicators().subscribe({
      next: (summary) => { this.riskIndicators.set(summary); this.riskLoading.set(false); },
      error: () => { this.riskError.set('Could not load risk-indicator data from the API.'); this.riskLoading.set(false); },
    });
  }

  protected statusTone(status: SlaMetric['status']): 'success' | 'warning' | 'danger' | 'neutral' {
    switch (status) {
      case 'Met': return 'success';
      case 'AtRisk': return 'warning';
      case 'Breached': return 'danger';
      default: return 'neutral';
    }
  }
}
