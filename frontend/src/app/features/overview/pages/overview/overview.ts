import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { AnalyticsOverview } from '../../../../core/models/analytics.model';
import { DashboardSummary } from '../../../../core/models/dashboard.model';
import { MeterCommandStatus, RechargeListItem, RechargeStatus } from '../../../../core/models/recharge.model';
import { ConnectivityCommandStatus } from '../../../../core/models/connectivity-command.model';
import { RiskIndicatorsSummary } from '../../../../core/models/sla.model';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { AuthService } from '../../../../core/services/auth.service';
import { SystemService } from '../../../../core/services/system.service';
import { SystemHealth } from '../../../../core/models/system.model';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { RechargeService } from '../../../../core/services/recharge.service';
import { SlaService } from '../../../../core/services/sla.service';
import { Icon } from '../../../../shared/components/icon/icon';
import { LineChart } from '../../../../shared/components/line-chart/line-chart';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

interface DonutSlice {
  label: string;
  count: number;
  pct: number;
  tone: 'success' | 'warning' | 'danger';
}

/** One row of "Attention Required" - assembled from real open exceptions, active billing holds,
 * pending/failed notifications, failed meter credits and tariff changes. Never hand-authored. */
interface AttentionItem {
  id: string;
  severity: 'critical' | 'warning';
  title: string;
  detail: string;
  at: string;
  link: string;
}

type TrendTab = 'consumption' | 'billing' | 'recharge' | 'revenue';

/**
 * The operations dashboard. Every number is backed by a real endpoint or shown as
 * "Data unavailable"; nothing is estimated. The trend chart reads the server-side analytics
 * aggregates. Payment status and meter-credit status are always separate, and system health is a
 * live probe of the API (other services are labelled as mock or unmonitored).
 */
@Component({
  selector: 'pe-overview',
  imports: [StatusBadge, Icon, LineChart, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './overview.html',
  styleUrls: ['./overview.scss', './overview-lists.scss'],
})
export class Overview implements OnInit, OnDestroy {
  protected readonly now = signal(new Date());
  private clockTimer?: ReturnType<typeof setInterval>;

  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly recharges = signal<RechargeListItem[]>([]);
  protected readonly rechargesLoading = signal(true);
  protected readonly rechargesError = signal(false);

  protected readonly risk = signal<RiskIndicatorsSummary | null>(null);
  protected readonly riskError = signal(false);

  protected readonly analytics = signal<AnalyticsOverview | null>(null);
  protected readonly analyticsLoading = signal(true);
  protected readonly analyticsError = signal(false);
  protected readonly trendTab = signal<TrendTab>('consumption');
  protected readonly trendDays = signal(30);

  /** Result of a real GET /health probe, with the measured round-trip time. */
  /** The API's own measurements: database round trip, workers and queues (GET /api/v1/system/health). */
  protected readonly systemHealth = signal<SystemHealth | null>(null);
  protected readonly apiHealth = signal<'checking' | 'healthy' | 'down'>('checking');
  protected readonly apiLatencyMs = signal<number | null>(null);

  protected readonly attentionFilter = signal<'all' | 'critical' | 'warning'>('all');

  protected readonly RechargeStatus = RechargeStatus;
  protected readonly MeterCommandStatus = MeterCommandStatus;
  protected readonly ConnectivityCommandStatus = ConnectivityCommandStatus;

  protected readonly trendTabs: { id: TrendTab; label: string }[] = [
    { id: 'consumption', label: 'Consumption' },
    { id: 'billing', label: 'Billing' },
    { id: 'recharge', label: 'Recharge' },
    { id: 'revenue', label: 'Revenue' },
  ];
  protected readonly trendRanges = [
    { days: 7, label: 'Last 7 days' },
    { days: 30, label: 'Last 30 days' },
    { days: 90, label: 'Last 90 days' },
  ];

  protected readonly money = (n: number) => '₹' + Math.round(n).toLocaleString('en-IN');
  protected readonly kwh = (n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(1));

  constructor(
    private readonly dashboardService: DashboardService,
    private readonly systemService: SystemService,
    private readonly rechargeService: RechargeService,
    private readonly slaService: SlaService,
    private readonly analyticsService: AnalyticsService,
    private readonly auth: AuthService,
    private readonly http: HttpClient,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.clockTimer = setInterval(() => this.now.set(new Date()), 30_000);




    this.dashboardService.summary().subscribe({
      next: (summary) => { this.summary.set(summary); this.loading.set(false); },
      error: () => { this.error.set('Could not load dashboard data from the API.'); this.loading.set(false); },
    });
    this.rechargeService.search({ pageSize: 4 }).subscribe({
      next: (page) => { this.recharges.set(page.items); this.rechargesLoading.set(false); },
      error: () => { this.rechargesError.set(true); this.rechargesLoading.set(false); },
    });





    this.slaService.getRiskIndicators().subscribe({
      next: (r) => this.risk.set(r),
      error: () => this.riskError.set(true),
    });

    this.systemService.health().subscribe({ next: (h) => this.systemHealth.set(h), error: () => this.systemHealth.set(null) });
    const started = performance.now();
    this.http.get(`${environment.apiBaseUrl}/health`).subscribe({
      next: () => {
        this.apiLatencyMs.set(Math.round(performance.now() - started));
        this.apiHealth.set('healthy');
      },
      error: () => this.apiHealth.set('down'),
    });

    this.loadTrend();
  }

  ngOnDestroy(): void {
    if (this.clockTimer) clearInterval(this.clockTimer);
  }

  // ---------------------------------------------------------------- hero
  protected get operatorName(): string {
    const first = this.auth.username()?.split(/\s+/)[0];
    return first ? first.charAt(0).toUpperCase() + first.slice(1).toLowerCase() : 'Operator';
  }

  protected get greeting(): string {
    const hour = this.now().getHours();
    if (hour < 12) return 'Good Morning';
    if (hour < 17) return 'Good Afternoon';
    return 'Good Evening';
  }

  protected get heroDate(): string {
    return this.now().toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
  }

  protected get heroTime(): string {
    return this.now().toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
  }

  // ---------------------------------------------------------------- KPIs
  protected get consumersAvailable(): boolean {
    return !this.loading() && !this.error();
  }

  protected get totalConsumers(): number {
    return this.summary()?.consumers.total ?? 0;
  }

  protected get activeCount(): number {
    return this.summary()?.consumers.active ?? 0;
  }

  protected get singlePhaseCount(): number {
    return this.summary()?.consumers.singlePhase ?? 0;
  }

  protected get threePhaseCount(): number {
    return this.summary()?.consumers.threePhase ?? 0;
  }

  protected get disconnectedCount(): number {
    return this.summary()?.consumers.disconnected ?? 0;
  }

  /** Whether the daily Happy Hours window (9 AM-2 PM IST, when manual disconnects are allowed) is open right now. Public holidays are not modelled. */
  protected readonly happyHourOpen = computed(() => {
    const istHour = new Date(this.now().getTime() + 5.5 * 3_600_000).getUTCHours();
    return istHour >= 9 && istHour < 14;
  });

  protected get totalWalletBalance(): number {
    return this.summary()?.consumers.walletTotal ?? 0;
  }

  protected get lowBalanceCount(): number {
    return this.summary()?.consumers.lowBalance ?? 0;
  }

  protected get healthyMetersPct(): number {
    const total = this.totalConsumers;
    return total === 0 ? 0 : Math.round((this.activeCount / total) * 100);
  }

  goToConsumers(queryParams?: Record<string, string>): void {
    this.router.navigate(['/consumers'], { queryParams });
  }

  goTo(link: string): void {
    this.router.navigateByUrl(link);
  }

  // ---------------------------------------------------------------- DLP billing progress
  protected get billingAvailable(): boolean {
    return !this.loading() && !this.error() && this.summary()?.billing != null;
  }

  /** Successful = Billed/Reconciled, Failed = Rejected, Pending = everything else; counted by the database for the latest profile date. */
  protected readonly billing = computed(() => {
    const b = this.summary()?.billing;
    if (!b) return { total: 0, successful: 0, failed: 0, pending: 0, pct: 0, profileDate: null as string | null, latestReceived: null as string | null };
    return {
      total: b.total,
      successful: b.successful,
      failed: b.failed,
      pending: b.pending,
      pct: b.total === 0 ? 0 : Math.round((b.successful / b.total) * 100),
      profileDate: b.profileDate,
      latestReceived: b.latestReceivedAt,
    };
  });

  // ---------------------------------------------------------------- energy trend
  private loadTrend(): void {
    const to = new Date();
    const from = new Date();
    from.setDate(to.getDate() - (this.trendDays() - 1));
    const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    this.analyticsLoading.set(true);
    this.analyticsError.set(false);
    this.analyticsService.overview(iso(from), iso(to)).subscribe({
      next: (a) => { this.analytics.set(a); this.analyticsLoading.set(false); },
      error: () => { this.analyticsError.set(true); this.analyticsLoading.set(false); },
    });
  }

  protected get trendTitle(): string {
    const tab = this.trendTab();
    return tab === 'consumption' ? 'Energy Consumption Trend' : this.trendTabs.find((t) => t.id === tab)!.label + ' Trend';
  }

  setTrendTab(tab: TrendTab): void {
    this.trendTab.set(tab);
  }

  onTrendRange(days: string): void {
    this.trendDays.set(Number(days));
    this.loadTrend();
  }

  private dayLabel(date: string): string {
    return new Date(date.slice(0, 10) + 'T00:00:00').toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
  }

  protected readonly trend = computed(() => {
    const a = this.analytics();
    const tab = this.trendTab();
    if (!a) return { labels: [] as string[], values: [] as number[], total: 0, unit: '', name: '', colour: 'var(--color-primary)', money: false };
    const pick = (rows: { date: string }[], value: (r: any) => number) => ({ labels: rows.map((r) => this.dayLabel(r.date)), values: rows.map(value) });
    switch (tab) {
      case 'billing':
        return { ...pick(a.billing, (r) => r.billed), total: a.totals.billed, unit: '₹', name: 'Billed', colour: 'var(--color-primary)', money: true };
      case 'recharge':
        return { ...pick(a.recharges, (r) => r.amountReceived), total: a.totals.rechargeReceived, unit: '₹', name: 'Payments received', colour: 'var(--color-success)', money: true };
      case 'revenue':
        return { ...pick(a.billing, (r) => r.settled), total: a.totals.settled, unit: '₹', name: 'Settled from wallets', colour: 'var(--color-analytic)', money: true };
      default:
        return { ...pick(a.consumption, (r) => r.totalKwh), total: a.totals.consumptionKwh, unit: 'kWh', name: 'Consumption (kWh)', colour: 'var(--color-primary)', money: false };
    }
  });

  protected readonly trendFormat = computed(() => (this.trend().money ? this.money : this.kwh));

  protected readonly trendStats = computed(() => {
    const t = this.trend();
    if (t.values.length === 0) return null;
    let peakIndex = 0;
    t.values.forEach((v, i) => { if (v > t.values[peakIndex]) peakIndex = i; });
    return { peakValue: t.values[peakIndex], peakLabel: t.labels[peakIndex], average: t.values.reduce((s, v) => s + v, 0) / t.values.length };
  });

  // ---------------------------------------------------------------- network health
  protected readonly networkHealthAvailable = computed(() => !this.loading() && !this.error() && this.totalConsumers > 0);

  protected readonly networkHealth = computed<DonutSlice[]>(() => {
    const c = this.summary()?.consumers;
    if (!c || c.total === 0) return [];
    const total = c.total;
    const healthy = total - c.disconnected - c.lowBalanceConnected;
    const slice = (label: string, count: number, tone: DonutSlice['tone']): DonutSlice => ({ label, count, pct: Math.round((count / total) * 100), tone });
    return [slice('Healthy', healthy, 'success'), slice('Low Balance', c.lowBalanceConnected, 'warning'), slice('Disconnected', c.disconnected, 'danger')].filter((x) => x.count > 0);
  });
  protected readonly networkGradient = computed(() => {
    const colour = { success: 'var(--color-success)', warning: 'var(--color-warning)', danger: 'var(--color-danger)' };
    const total = this.totalConsumers || 1;
    let acc = 0;
    const stops = this.networkHealth().map((s) => {
      const start = acc;
      acc += (s.count / total) * 100;
      return `${colour[s.tone]} ${start}% ${acc}%`;
    });
    return `conic-gradient(${stops.join(', ')})`;
  });

  protected readonly networkHealthyPct = computed(() => {
    const total = this.totalConsumers;
    const healthy = this.networkHealth().find((s) => s.label === 'Healthy')?.count ?? 0;
    return total === 0 ? 0 : Math.round((healthy / total) * 100);
  });

  // ---------------------------------------------------------------- attention required
  protected readonly attentionItems = computed<AttentionItem[]>(() => this.summary()?.attention.items ?? []);
  protected readonly attentionCritical = computed(() => this.summary()?.attention.critical ?? 0);
  protected readonly attentionWarning = computed(() => this.summary()?.attention.warning ?? 0);
  protected readonly attentionTotal = computed(() => this.attentionCritical() + this.attentionWarning());

  protected readonly attentionShown = computed(() => {
    const f = this.attentionFilter();
    const all = this.attentionItems();
    return (f === 'all' ? all : all.filter((i) => i.severity === f)).slice(0, 3);
  });
  protected readonly attentionReady = computed(() => !this.loading());
  protected readonly attentionFailed = computed(() => !!this.error());

  setAttentionFilter(filter: 'all' | 'critical' | 'warning'): void {
    this.attentionFilter.set(filter);
  }

  protected timeAgo(at: string): string {
    const minutes = Math.max(0, Math.round((this.now().getTime() - new Date(at).getTime()) / 60000));
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes} min ago`;
    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours} hr ago`;
    const days = Math.round(hours / 24);
    return days === 1 ? '1 day ago' : `${days} days ago`;
  }

  // ---------------------------------------------------------------- revenue protection (real open conditions)
  protected readonly riskRows = computed(() => {
    const r = this.risk();
    if (!r) return [];
    return [
      { label: 'Open exceptions', count: r.openExceptions, link: '/exceptions' },
      { label: 'Active billing holds', count: r.activeBillingHolds, link: '/billing-holds' },
      { label: 'Unresolved meter alarms', count: r.unresolvedMeterAlarms, link: '/meter-data' },
      { label: 'Failed energy validations', count: r.failedEnergyValidations, link: '/exceptions' },
      { label: 'Disconnected consumers', count: r.disconnectedConsumers, link: '/consumers?status=Disconnected' },
    ];
  });

  protected readonly riskTotal = computed(() => this.riskRows().reduce((sum, r) => sum + r.count, 0));

  // ---------------------------------------------------------------- recent tables
  protected readonly recentMeterOps = computed(() => this.summary()?.recentConnectivity ?? []);

  protected meterCreditLabel(status: MeterCommandStatus | null): string {
    switch (status) {
      case MeterCommandStatus.Acknowledged: return 'Credited';
      case MeterCommandStatus.Failed: return 'Failed';
      case MeterCommandStatus.TimedOut: return 'Timed out';
      case MeterCommandStatus.Sent: return 'Awaiting ack';
      case MeterCommandStatus.Queued: return 'Queued';
      default: return 'None';
    }
  }

  protected meterCreditTone(status: MeterCommandStatus | null): 'success' | 'danger' | 'info' | 'neutral' {
    switch (status) {
      case MeterCommandStatus.Acknowledged: return 'success';
      case MeterCommandStatus.Failed:
      case MeterCommandStatus.TimedOut: return 'danger';
      case MeterCommandStatus.Sent:
      case MeterCommandStatus.Queued: return 'info';
      default: return 'neutral';
    }
  }

  protected get workersHealthy(): string {
    const w = this.systemHealth()?.workers ?? [];
    return `${w.filter((x) => x.state === 'Healthy').length} of ${w.length} healthy`;
  }

  protected get workersOk(): boolean {
    const w = this.systemHealth()?.workers ?? [];
    return w.length > 0 && w.every((x) => x.state === 'Healthy');
  }
}
