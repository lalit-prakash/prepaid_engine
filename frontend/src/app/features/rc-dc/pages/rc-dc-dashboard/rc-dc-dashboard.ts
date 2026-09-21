import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ConnectivityCommandService } from '../../../../core/services/connectivity-command.service';
import { NetworkNode, NetworkService } from '../../../../core/services/network.service';
import {
  ConnectivityCommandStatus,
  ConnectivityCommandSummary,
  ConnectivityCommandSummaryStats,
  ConnectivityCommandType,
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
  type: string;
  status: string;
  reason: string;
  balance: string;
  zoneId: string;
  circleId: string;
  from: string;
  to: string;
}

const EMPTY: Filters = { q: '', type: '', status: '', reason: '', balance: '', zoneId: '', circleId: '', from: '', to: '' };

/**
 * Disconnection / Reconnection. KPIs and tab counts come from GET /connectivity-commands/summary (counted by the database)
 * and the table from the keyset-paged GET /connectivity-commands/search, so the browser never holds more than one page.
 * Nothing here is estimated: there are no month-on-month trends because none are recorded, and no "scheduled" state
 * because a command is queued, sent, acknowledged, failed or timed out.
 */
@Component({
  selector: 'pe-rc-dc-dashboard',
  imports: [StatusBadge, KpiCard, DatePipe, DecimalPipe, RouterLink, FormsModule],
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
  protected readonly activeTab = signal<string | null>(null);
  protected readonly ConnectivityCommandStatus = ConnectivityCommandStatus;
  protected readonly ConnectivityCommandType = ConnectivityCommandType;

  protected readonly tabs: Tab[] = [
    { label: 'All Operations', status: null, count: (s) => s.total },
    { label: 'Pending', status: 'Queued', count: (s) => s.queued },
    { label: 'In Progress', status: 'Sent', count: (s) => s.sent },
    { label: 'Completed', status: 'Acknowledged', count: (s) => s.acknowledged },
    { label: 'Failed', status: 'FailedOrTimedOut', count: (s) => s.failedOrTimedOut },
  ];

  protected readonly list = new PagedList<ConnectivityCommandSummary>(
    (after) =>
      this.connectivityCommandService.search({
        q: this.applied.q,
        type: this.applied.type === '' ? null : (Number(this.applied.type) as ConnectivityCommandType),
        status: this.applied.status || null,
        reason: this.applied.reason,
        balance: this.applied.balance,
        zoneId: this.applied.zoneId,
        circleId: this.applied.circleId,
        from: this.applied.from,
        to: this.applied.to,
        after,
        pageSize: PAGE_SIZE,
      }),
    'Could not load connectivity commands from the API.',
  );

  constructor(
    private readonly connectivityCommandService: ConnectivityCommandService,
    private readonly network: NetworkService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    // Prefills from a cross-link (for example Consumer 360's RC/DC panel: "View RC/DC history").
    const initialQuery = this.route.snapshot.queryParamMap.get('q');
    if (initialQuery) this.form.q = this.applied.q = initialQuery;

    this.connectivityCommandService.summary().subscribe({ next: (s) => this.stats.set(s), error: () => this.statsError.set(true) });
    this.network.nodes('zone').subscribe({ next: (z) => this.zones.set(z), error: () => this.zones.set([]) });
    this.list.load();
  }

  ngOnDestroy(): void {
    this.list.destroy();
  }

  protected onZoneChange(): void {
    this.form.circleId = '';
    this.circles.set([]);
    if (this.form.zoneId) this.network.nodes('circle', this.form.zoneId).subscribe({ next: (c) => this.circles.set(c), error: () => this.circles.set([]) });
  }

  protected search(): void {
    this.applied = { ...this.form };
    this.activeTab.set(this.applied.status || null);
    this.list.reload();
  }

  protected reset(): void {
    this.form = { ...EMPTY };
    this.circles.set([]);
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
