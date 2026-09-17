import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MeterDataService } from '../../../../core/services/meter-data.service';
import { DailyLoadProfileSummary, DailyProfileStatus } from '../../../../core/models/meter-data.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

/**
 * Real, API-backed Meter Data dashboard — the raw Daily Load Profile (DLP) stream the billing
 * pipeline runs on (GET /api/v1/meter-data/dlp). DLP is the sole driver of ongoing prepaid
 * billing now that the hourly Load Survey (LS) pipeline has been removed. Read-only by design —
 * this is ingested meter data, never edited from a UI. Capped at the 500 most-recent rows (this
 * project has no pagination anywhere).
 */
@Component({
  selector: 'pe-meter-data-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './meter-data-dashboard.html',
  styleUrl: './meter-data-dashboard.scss',
})
export class MeterDataDashboard implements OnInit {
  protected readonly profiles = signal<DailyLoadProfileSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');

  protected readonly DailyProfileStatus = DailyProfileStatus;

  constructor(
    private readonly meterDataService: MeterDataService,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    // Prefills from a cross-link (e.g. Billing Holds' "View DLP data →") — real reuse of this
    // page's own search state, not a separate filtered endpoint.
    const initialQuery = this.route.snapshot.queryParamMap.get('q');
    if (initialQuery) this.searchTerm.set(initialQuery);

    this.meterDataService.listDailyLoadProfiles().subscribe({
      next: (profiles) => {
        this.profiles.set(profiles);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load Daily Load Profile data from the API.');
        this.loading.set(false);
      },
    });
  }

  protected get filteredProfiles(): DailyLoadProfileSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.profiles();
    return this.profiles().filter(
      (p) =>
        p.accountNumber.toLowerCase().includes(term) ||
        p.name.toLowerCase().includes(term) ||
        p.meterNumber.toLowerCase().includes(term),
    );
  }

  protected get provisionalCount(): number {
    return this.profiles().filter((p) => p.isProvisional).length;
  }
  protected get billedCount(): number {
    return this.profiles().filter((p) => p.status === DailyProfileStatus.Billed).length;
  }

  /** Which of the two daily billing stages this profile's receipt time would have qualified
   * for — 8:30-9:30 AM bills anything received by 8:00 AM, 12:30-1:30 PM bills anything received
   * between 8:00 AM and 12:00 PM (plus provisional billing for what's still missing by then). A
   * display-only classification of the real `receivedAt` timestamp, not a separate field. */
  protected receivedStageLabel(receivedAt: string): string {
    const hour = new Date(receivedAt).getHours() + new Date(receivedAt).getMinutes() / 60;
    if (hour < 8) return 'Stage 1 (8:30-9:30 AM)';
    if (hour < 12) return 'Stage 2 (12:30-1:30 PM)';
    return 'After 12 PM cutoff';
  }
}
