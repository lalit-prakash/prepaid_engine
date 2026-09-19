import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ReportJob, ReportJobService } from '../../../../core/services/report-job.service';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { OperateOnly } from '../../../../shared/directives/operate-only';

const REPORT_TITLES: Record<string, string> = {
  billing: 'Daily Billing Report',
  'recharge-failures': 'Recharge Failure Report',
  'meter-credit-failures': 'Meter Credit Failure Report',
};

/**
 * Report exports: full CSV exports built in the background, so they are not limited to the rows a report page shows.
 * Request one from a report page; it appears here as Queued, then Running with a live row count, then Completed with a
 * download. Files are removed after the retention period. The list refreshes itself while anything is waiting or running.
 */
@Component({
  selector: 'pe-report-exports',
  imports: [StatusBadge, DatePipe, DecimalPipe, RouterLink, OperateOnly],
  templateUrl: './report-exports.html',
  styleUrl: './report-exports.scss',
})
export class ReportExports implements OnInit, OnDestroy {
  protected readonly jobs = signal<ReportJob[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly downloadError = signal<string | null>(null);
  protected readonly downloading = signal<string | null>(null);
  protected readonly busy = computed(() => this.jobs().some((j) => j.status === 'Queued' || j.status === 'Running'));
  private timer?: ReturnType<typeof setInterval>;

  constructor(private readonly reportJobs: ReportJobService) {}

  ngOnInit(): void {
    this.load();
    this.timer = setInterval(() => { if (this.busy()) this.load(); }, 4000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected load(): void {
    this.reportJobs.list().subscribe({
      next: (jobs) => { this.jobs.set(jobs); this.error.set(null); this.loading.set(false); },
      error: () => { this.error.set('Could not load your exports from the API.'); this.loading.set(false); },
    });
  }

  protected title(job: ReportJob): string {
    return REPORT_TITLES[job.report] ?? job.report;
  }

  protected tone(status: string): 'success' | 'warning' | 'danger' | 'info' | 'neutral' {
    switch (status) {
      case 'Completed': return 'success';
      case 'Running': return 'info';
      case 'Queued': return 'warning';
      case 'Failed': return 'danger';
      default: return 'neutral';
    }
  }

  protected size(bytes: number | null): string {
    if (bytes === null) return '—';
    return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  protected filters(job: ReportJob): string {
    const p = job.parameters as Record<string, unknown>;
    const parts: string[] = [];
    if (p['from'] || p['to']) parts.push(`${String(p['from'] ?? '…').slice(0, 10)} to ${String(p['to'] ?? '…').slice(0, 10)}`);
    if (p['status']) parts.push(String(p['status']));
    const levels = ['zoneId', 'circleId', 'divisionId', 'subDivisionId', 'substationId', 'feederId', 'dtrId'].filter((k) => p[k]);
    if (levels.length) parts.push('network filter');
    return parts.length ? parts.join(' · ') : 'No filters';
  }

  protected download(job: ReportJob): void {
    this.downloading.set(job.id);
    this.downloadError.set(null);
    this.reportJobs.download(job).subscribe({
      next: () => this.downloading.set(null),
      error: (err) => {
        this.downloading.set(null);
        this.downloadError.set(err?.status === 403 ? 'Your role cannot download exports.' : 'Could not download this export; it may have expired.');
        this.load();
      },
    });
  }
}
