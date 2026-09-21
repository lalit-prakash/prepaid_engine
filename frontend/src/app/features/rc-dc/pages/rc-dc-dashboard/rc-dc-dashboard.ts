import { DatePipe, DecimalPipe, Location } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ConnectivityCommandService } from '../../../../core/services/connectivity-command.service';
import { NetworkNode, NetworkService } from '../../../../core/services/network.service';
import {
  ConnectivityCommandStatus,
  ConnectivityCommandSummary,
  ConnectivityCommandSummaryStats,
  ConnectivityCommandType,
  LiveRcDcRow,
} from '../../../../core/models/connectivity-command.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { PagedList } from '../../../../shared/utils/paged-list';

const PAGE_SIZE = 25;

interface Tab {
  label: string;
  /** The `status` the API takes; null is all. */
  status: string | null;
  count: (s: ConnectivityCommandSummaryStats) => number;
}

/** The filters as typed into the Search & Filter card; they only take effect when Search is pressed. */
interface Filters {
  q: string;
  meterNumber: string;
  type: string;
  status: string;
  zoneId: string;
  circleId: string;
  divisionId: string;
  subDivisionId: string;
  from: string;
  to: string;
}

/** yyyy-mm-dd for a date input, in local time. */
function isoDay(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

const EMPTY: Filters = { q: '', meterNumber: '', type: '', status: '', zoneId: '', circleId: '', divisionId: '', subDivisionId: '', from: '', to: '' };

/** A download covers at most this many days. */
const EXPORT_MAX_DAYS = 30;

/**
 * Disconnection / Reconnection. KPIs and tab counts come from GET /connectivity-commands/summary (counted by the database)
 * and the table from the keyset-paged GET /connectivity-commands/search, so the browser never holds more than one page.
 * Nothing here is estimated: there are no month-on-month trends because none are recorded, and no "scheduled" state
 * because a command is queued, sent, acknowledged, failed or timed out.
 */
@Component({
  selector: 'pe-rc-dc-dashboard',
  imports: [StatusBadge, KpiCard, DatePipe, DecimalPipe, FormsModule],
  templateUrl: './rc-dc-dashboard.html',
  styleUrl: './rc-dc-dashboard.scss',
})
export class RcDcDashboard implements OnInit, OnDestroy {
  /** What the form shows. */
  protected form: Filters = { ...EMPTY };
  /** What the list is currently filtered by. */
  private applied: Filters = { ...EMPTY };

  protected readonly stats = signal<ConnectivityCommandSummaryStats | null>(null);
  protected readonly statsError = signal(false);
  protected readonly zones = signal<NetworkNode[]>([]);
  protected readonly circles = signal<NetworkNode[]>([]);
  protected readonly divisions = signal<NetworkNode[]>([]);
  protected readonly subDivisions = signal<NetworkNode[]>([]);
  protected readonly downloading = signal(false);
  protected readonly downloadError = signal<string | null>(null);
  protected readonly activeTab = signal<string | null>(null);

  /** The Live RC DC Status view opens in place (?view=live), so the browser Back button returns to the operations list. */
  protected readonly view = signal<'operations' | 'live'>('operations');
  protected readonly live = signal<LiveRcDcRow[]>([]);
  protected readonly liveLoading = signal(false);
  protected readonly liveError = signal<string | null>(null);
  protected readonly liveGeneratedAt = signal<string | null>(null);
  protected liveFrom = isoDay(new Date(Date.now() - 6 * 86_400_000));
  protected liveTo = isoDay(new Date());
  protected liveZoneId = '';
  private openedFromList = false;
  private querySub?: Subscription;
  protected readonly ConnectivityCommandStatus = ConnectivityCommandStatus;
  protected readonly ConnectivityCommandType = ConnectivityCommandType;

  protected readonly tabs: Tab[] = [
    { label: 'All Operations', status: null, count: (s) => s.total },
    { label: 'Success', status: 'Acknowledged', count: (s) => s.acknowledged },
    { label: 'Fail', status: 'FailedOrTimedOut', count: (s) => s.failedOrTimedOut },
  ];

  protected readonly list = new PagedList<ConnectivityCommandSummary>(
    (after) =>
      this.connectivityCommandService.search({ ...this.toParams(this.applied), after, pageSize: PAGE_SIZE }),
    'Could not load connectivity commands from the API.',
  );

  constructor(
    private readonly connectivityCommandService: ConnectivityCommandService,
    private readonly network: NetworkService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly location: Location,
  ) {}

  ngOnInit(): void {
    // Prefills from a cross-link (for example Consumer 360's RC/DC panel: "View RC/DC history").
    const initialQuery = this.route.snapshot.queryParamMap.get('q');
    if (initialQuery) this.form.q = this.applied.q = initialQuery;

    this.connectivityCommandService.summary().subscribe({ next: (s) => this.stats.set(s), error: () => this.statsError.set(true) });
    this.network.nodes('zone').subscribe({ next: (z) => this.zones.set(z), error: () => this.zones.set([]) });
    this.list.load();

    this.querySub = this.route.queryParamMap.subscribe((params) => {
      const wantLive = params.get('view') === 'live';
      if (wantLive && this.view() !== 'live') this.loadLive();
      this.view.set(wantLive ? 'live' : 'operations');
    });
  }

  ngOnDestroy(): void {
    this.querySub?.unsubscribe();
    this.list.destroy();
  }

  protected openLive(): void {
    this.openedFromList = true;
    this.router.navigate([], { relativeTo: this.route, queryParams: { view: 'live' } });
  }

  /** Back: the browser's own Back when this view was opened from the list, otherwise straight to the list. */
  protected closeLive(): void {
    if (this.openedFromList) this.location.back();
    else this.router.navigate([], { relativeTo: this.route, queryParams: { view: null }, replaceUrl: true });
  }

  protected loadLive(): void {
    this.liveLoading.set(true);
    this.liveError.set(null);
    this.connectivityCommandService.liveStatus({ from: this.liveFrom, to: this.liveTo, zoneId: this.liveZoneId }).subscribe({
      next: (r) => {
        this.live.set(r.rows);
        this.liveGeneratedAt.set(r.generatedAt);
        this.liveLoading.set(false);
      },
      error: (err) => {
        this.liveError.set(err?.error?.error ?? 'Could not load the Live RC DC Status report from the API.');
        this.liveLoading.set(false);
      },
    });
  }

  /** Clicking a DC count shows those disconnect commands in the operations list. */
  protected drillDc(day: string, status: string): void {
    const date = day.slice(0, 10);
    this.form = { ...EMPTY, type: String(ConnectivityCommandType.Disconnect), status, from: date, to: date };
    this.circles.set([]);
    this.divisions.set([]);
    this.subDivisions.set([]);
    this.search();
    this.router.navigate([], { relativeTo: this.route, queryParams: { view: null } });
  }

  private toParams(f: Filters) {
    return {
      q: f.q,
      meterNumber: f.meterNumber,
      type: f.type === '' ? null : (Number(f.type) as ConnectivityCommandType),
      status: f.status || null,
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

  /** The Excel download needs a From and To date no more than 30 days apart; null when the range is fine, otherwise what to fix. */
  protected get downloadHint(): string | null {
    const { from, to } = this.form;
    if (!from || !to) return 'Choose a From and To date to download.';
    const days = (Date.parse(to) - Date.parse(from)) / 86_400_000;
    if (days < 0) return 'The To date is before the From date.';
    if (days >= EXPORT_MAX_DAYS) return `Download covers at most ${EXPORT_MAX_DAYS} days.`;
    return null;
  }

  protected download(): void {
    if (this.downloadHint || this.downloading()) return;
    this.downloading.set(true);
    this.downloadError.set(null);
    this.connectivityCommandService.exportExcel(this.toParams(this.form)).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `rc-dc-${this.form.from}-to-${this.form.to}.xlsx`;
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

  protected search(): void {
    this.applied = { ...this.form };
    this.activeTab.set(this.applied.status || null);
    this.list.reload();
  }

  protected reset(): void {
    this.form = { ...EMPTY };
    this.circles.set([]);
    this.divisions.set([]);
    this.subDivisions.set([]);
    this.search();
  }

  /** A tab (or KPI tile) narrows to one status and keeps the other filters. */
  protected selectStatus(status: string | null): void {
    this.form.status = status ?? '';
    this.search();
  }

  protected get hasActiveFilters(): boolean {
    return Object.values(this.applied).some((v) => !!v && String(v).trim() !== '');
  }

  protected successRate(s: ConnectivityCommandSummaryStats): string {
    const finished = s.acknowledged + s.failedOrTimedOut;
    return finished === 0 ? '—' : `${((s.acknowledged / finished) * 100).toFixed(1)}%`;
  }

  open(id: string): void {
    this.router.navigate(['/rc-dc', id]);
  }
}
