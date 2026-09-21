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
  q: string;
  status: string;
  conversion: string;
  zoneId: string;
  circleId: string;
  divisionId: string;
  subDivisionId: string;
  from: string;
  to: string;
}

const EMPTY: Filters = {
  consumerNumber: '', meterNumber: '', q: '', status: '', conversion: '', zoneId: '', circleId: '', divisionId: '', subDivisionId: '', from: '', to: '',
};

/**
 * Consumers. Search, filters and paging all run on the server (GET /api/v1/consumers/search, keyset-paged on account number),
 * so the browser only ever holds one page. The cards count consumers by postpaid-to-prepaid conversion state
 * (GET /consumers/summary) and narrow this list in place; nothing on this page navigates away except opening one consumer.
 * Only what the backend models is shown: a conversion date appears only for consumers with a completed conversion.
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

  protected readonly statusOptions = [
    { label: 'Active', value: ConnectionStatus.Active },
    { label: 'Disconnected', value: ConnectionStatus.Disconnected },
    { label: 'Disconnection pending', value: ConnectionStatus.DisconnectionPending },
    { label: 'Reconnection pending', value: ConnectionStatus.ReconnectionPending },
  ];

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
    if (initialQuery) this.form.q = this.applied.q = initialQuery;
    const initialStatus = params.get('status');
    if (initialStatus !== null && initialStatus in ConnectionStatus) {
      this.form.status = this.applied.status = String(ConnectionStatus[initialStatus as keyof typeof ConnectionStatus]);
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
      q: f.q,
      status: f.status === '' ? null : (Number(f.status) as ConnectionStatus),
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
    this.form = { ...EMPTY };
    this.circles.set([]);
    this.divisions.set([]);
    this.subDivisions.set([]);
    this.search();
  }

  /** A card narrows the list to one conversion state in place, keeping the other filters. */
  protected selectConversion(state: string): void {
    this.form.conversion = state;
    this.search();
  }

  protected get activeConversion(): string {
    return this.applied.conversion;
  }

  protected get hasActiveFilters(): boolean {
    return Object.values(this.applied).some((v) => !!v && String(v).trim() !== '');
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
