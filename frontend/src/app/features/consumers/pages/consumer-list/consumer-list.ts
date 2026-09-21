import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ConsumerService } from '../../../../core/services/consumer.service';
import { NetworkNode, NetworkService } from '../../../../core/services/network.service';
import { ConnectionStatus, ConsumerListItem, ConsumerSummaryStats } from '../../../../core/models/consumer.model';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { PagedList } from '../../../../shared/utils/paged-list';

const PAGE_SIZE = 25;

/** The filters as typed into the Search & Filter card; they only take effect when Search is pressed. */
interface Filters {
  consumerNumber: string;
  meterNumber: string;
  /** The conversion view chosen with the cards: Total, Completed or Failed; empty is every consumer. */
  conversion: string;
  zoneId: string;
  circleId: string;
  divisionId: string;
  subDivisionId: string;
  from: string;
  to: string;
}

const EMPTY: Filters = {
  consumerNumber: '', meterNumber: '', conversion: 'Total', zoneId: '', circleId: '', divisionId: '', subDivisionId: '', from: '', to: '',
};

/**
 * Consumers. Search, filters and paging all run on the server (GET /api/v1/consumers/search, keyset-paged on account number),
 * so the browser only ever holds one page. The cards count postpaid-to-prepaid conversion requests (GET /consumers/summary) and
 * choose the view in place: Total shows when each was requested, Completed when it was converted, Failed why it failed.
 * Clicking the selected card again shows every consumer. Nothing here navigates away except opening one consumer.
 * A failure reason is the decision note recorded on the rejected request; when none was recorded it says so.
 */
@Component({
  selector: 'pe-consumer-list',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe, FormsModule],
  templateUrl: './consumer-list.html',
  styleUrl: './consumer-list.scss',
})
export class ConsumerList implements OnInit, OnDestroy {
  /** What the form shows. */
  protected form: Filters = { ...EMPTY };
  /** What the list is currently filtered by. */
  private applied: Filters = { ...EMPTY };

  protected readonly stats = signal<ConsumerSummaryStats | null>(null);
  protected readonly statsError = signal(false);
  protected readonly zones = signal<NetworkNode[]>([]);
  protected readonly circles = signal<NetworkNode[]>([]);
  protected readonly divisions = signal<NetworkNode[]>([]);
  protected readonly subDivisions = signal<NetworkNode[]>([]);
  protected readonly downloading = signal(false);
  protected readonly downloadError = signal<string | null>(null);
  protected readonly ConnectionStatus = ConnectionStatus;

  protected readonly list = new PagedList<ConsumerListItem>(
    (after) => this.consumerService.search({ ...this.toParams(this.applied), after, pageSize: PAGE_SIZE }),
    'Could not load consumers from the API.',
  );

  constructor(
    private readonly consumerService: ConsumerService,
    private readonly network: NetworkService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    // Prefills from a cross-link (the global search, or a dashboard tile).
    const params = this.route.snapshot.queryParamMap;
    const initialQuery = params.get('q');
    if (initialQuery) {
      this.form.consumerNumber = this.applied.consumerNumber = initialQuery;
      this.form.conversion = this.applied.conversion = ''; // a cross-link is a look-up of one consumer, so search every consumer
    }

    this.consumerService.summary().subscribe({ next: (s) => this.stats.set(s), error: () => this.statsError.set(true) });
    this.network.nodes('zone').subscribe({ next: (z) => this.zones.set(z), error: () => this.zones.set([]) });
    this.list.load();
  }

  ngOnDestroy(): void {
    this.list.destroy();
  }

  private toParams(f: Filters) {
    return {
      consumerNumber: f.consumerNumber,
      meterNumber: f.meterNumber,
      conversion: f.conversion,
      zoneId: f.zoneId,
      circleId: f.circleId,
      divisionId: f.divisionId,
      subDivisionId: f.subDivisionId,
      from: f.from,
      to: f.to,
    };
  }

  /** Each location list follows the one above it: zone, then circle, division and subdivision. */
  protected onZoneChange(): void {
    this.form.circleId = this.form.divisionId = this.form.subDivisionId = '';
    this.circles.set([]);
    this.divisions.set([]);
    this.subDivisions.set([]);
    if (this.form.zoneId) this.network.nodes('circle', this.form.zoneId).subscribe({ next: (c) => this.circles.set(c), error: () => this.circles.set([]) });
  }

  protected onCircleChange(): void {
    this.form.divisionId = this.form.subDivisionId = '';
    this.divisions.set([]);
    this.subDivisions.set([]);
    if (this.form.circleId) this.network.nodes('division', this.form.circleId).subscribe({ next: (d) => this.divisions.set(d), error: () => this.divisions.set([]) });
  }

  protected onDivisionChange(): void {
    this.form.subDivisionId = '';
    this.subDivisions.set([]);
    if (this.form.divisionId) this.network.nodes('subdivision', this.form.divisionId).subscribe({ next: (d) => this.subDivisions.set(d), error: () => this.subDivisions.set([]) });
  }

  protected search(): void {
    this.applied = { ...this.form };
    this.list.reload();
  }

  protected reset(): void {
    this.form = { ...EMPTY, conversion: this.form.conversion }; // clears the filters and keeps the chosen view
    this.circles.set([]);
    this.divisions.set([]);
    this.subDivisions.set([]);
    this.search();
  }

  /** A card chooses the conversion view in place, keeping the other filters; the selected card again shows every consumer. */
  protected selectConversion(state: string): void {
    this.form.conversion = this.applied.conversion === state ? '' : state;
    this.search();
  }

  protected get view(): string {
    return this.applied.conversion;
  }

  /** The date filter follows the view: requested time for Total and Failed, converted time for Completed. */
  protected get dateLabel(): string {
    return this.form.conversion === 'Completed' ? 'Converted' : 'Requested';
  }

  protected successRate(s: ConsumerSummaryStats): string {
    const decided = s.completed + s.failed;
    return decided === 0 ? '—' : `${((s.completed / decided) * 100).toFixed(1)}%`;
  }

  protected get hasActiveFilters(): boolean {
    const { conversion, ...rest } = this.applied;
    return Object.values(rest).some((v) => !!v && String(v).trim() !== '');
  }

  protected download(): void {
    if (this.downloading()) return;
    this.downloading.set(true);
    this.downloadError.set(null);
    this.consumerService.exportExcel(this.toParams(this.form)).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `consumers-${new Date().toISOString().slice(0, 10)}.xlsx`;
        a.click();
        URL.revokeObjectURL(url);
        this.downloading.set(false);
      },
      error: async (err) => {
        let message = 'Could not download the file.';
        try { message = JSON.parse(await (err.error as Blob).text()).error ?? message; } catch { /* keep the generic message */ }
        this.downloadError.set(message);
        this.downloading.set(false);
      },
    });
  }

  open(accountNumber: string): void {
    this.router.navigate(['/consumers', accountNumber]);
  }
}
