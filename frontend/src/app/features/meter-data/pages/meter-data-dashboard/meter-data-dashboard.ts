import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MeterDataService } from '../../../../core/services/meter-data.service';
import {
  DailyLoadProfileSummary,
  DailyProfileStatus,
  LoadSurveyIntervalSummary,
  LoadSurveyQuality,
  LoadSurveyStatus,
} from '../../../../core/models/meter-data.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

type Tab = 'ls' | 'dlp';

/**
 * Real, API-backed Meter Data dashboard — the raw LS (30-minute Load Survey) and DLP (Daily Load
 * Profile) streams the LS/DLP billing pipeline runs on (GET /api/v1/meter-data/ls,
 * GET /api/v1/meter-data/dlp), per the pipeline spec's own core rule: LS and DLP are two
 * different meter-data products and must never be shown as one. Read-only by design — this is
 * ingested meter data, never edited from a UI. Both lists are capped at the 500 most recent rows
 * (this project has no pagination anywhere).
 */
@Component({
  selector: 'pe-meter-data-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './meter-data-dashboard.html',
  styleUrl: './meter-data-dashboard.scss',
})
export class MeterDataDashboard implements OnInit {
  protected readonly activeTab = signal<Tab>('ls');

  protected readonly intervals = signal<LoadSurveyIntervalSummary[]>([]);
  protected readonly profiles = signal<DailyLoadProfileSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly searchTerm = signal('');

  protected readonly LoadSurveyQuality = LoadSurveyQuality;
  protected readonly LoadSurveyStatus = LoadSurveyStatus;
  protected readonly DailyProfileStatus = DailyProfileStatus;

  constructor(
    private readonly meterDataService: MeterDataService,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    // Prefills from a cross-link (e.g. Billing Holds' "View LS data →") — real reuse of
    // this page's own tab/search state, not a separate filtered endpoint.
    const initialTab = this.route.snapshot.queryParamMap.get('tab');
    if (initialTab === 'ls' || initialTab === 'dlp') this.activeTab.set(initialTab);
    const initialQuery = this.route.snapshot.queryParamMap.get('q');
    if (initialQuery) this.searchTerm.set(initialQuery);

    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.meterDataService.listLoadSurvey().subscribe({
      next: (intervals) => {
        this.intervals.set(intervals);
        this.loadDlp();
      },
      error: () => {
        this.error.set('Could not load Load Survey data from the API.');
        this.loading.set(false);
      },
    });
  }

  private loadDlp(): void {
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

  setTab(tab: Tab): void {
    this.activeTab.set(tab);
    this.searchTerm.set('');
  }

  protected get filteredIntervals(): LoadSurveyIntervalSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.intervals();
    return this.intervals().filter(
      (i) =>
        i.accountNumber.toLowerCase().includes(term) ||
        i.name.toLowerCase().includes(term) ||
        i.meterNumber.toLowerCase().includes(term),
    );
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

  protected get validCount(): number {
    return this.intervals().filter((i) => i.quality === LoadSurveyQuality.Valid).length;
  }
  protected get rejectedCount(): number {
    return this.intervals().filter((i) => i.status === LoadSurveyStatus.Rejected).length;
  }
  protected get processedCount(): number {
    return this.intervals().filter((i) => i.status === LoadSurveyStatus.Processed).length;
  }

  protected get provisionalCount(): number {
    return this.profiles().filter((p) => p.isProvisional).length;
  }
  protected get billedCount(): number {
    return this.profiles().filter((p) => p.status === DailyProfileStatus.Billed).length;
  }

  protected qualityLabel(quality: LoadSurveyQuality): string {
    return LoadSurveyQuality[quality] ?? 'Unknown';
  }
  protected statusLabel(status: LoadSurveyStatus): string {
    return LoadSurveyStatus[status] ?? 'Unknown';
  }
}
