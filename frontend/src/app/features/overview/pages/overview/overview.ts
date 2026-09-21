import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AnalyticsOverview, MeterCommunication, WalletDistribution } from '../../../../core/models/analytics.model';
import { DashboardSummary } from '../../../../core/models/dashboard.model';
import { MeterCommandStatus, RechargeListItem, RechargeStatus } from '../../../../core/models/recharge.model';
import { ConnectivityCommandStatus } from '../../../../core/models/connectivity-command.model';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { AuthService } from '../../../../core/services/auth.service';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { RechargeService } from '../../../../core/services/recharge.service';
import { Icon } from '../../../../shared/components/icon/icon';
import { LineChart } from '../../../../shared/components/line-chart/line-chart';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

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

  protected readonly comm = signal<MeterCommunication | null>(null);
  protected readonly commLoading = signal(true);
  protected readonly wallet = signal<WalletDistribution | null>(null);
  protected readonly walletLoading = signal(true);

  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly recharges = signal<RechargeListItem[]>([]);
  protected readonly rechargesLoading = signal(true);
  protected readonly rechargesError = signal(false);


  protected readonly analytics = signal<AnalyticsOverview | null>(null);
  protected readonly analyticsLoading = signal(true);
  protected readonly analyticsError = signal(false);
  protected readonly trendTab = signal<TrendTab>('consumption');
  protected readonly trendDays = signal(30);

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
    private readonly rechargeService: RechargeService,
    private readonly analyticsService: AnalyticsService,
    private readonly auth: AuthService,
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

    this.analyticsService.meterCommunication().subscribe({ next: (c) => { this.comm.set(c); this.commLoading.set(false); }, error: () => this.commLoading.set(false) });
    this.analyticsService.walletDistribution().subscribe({ next: (w) => { this.wallet.set(w); this.walletLoading.set(false); }, error: () => this.walletLoading.set(false) });

    this.loadTrend();
  }

  ngOnDestroy(): void {
    if (this.clockTimer) clearInterval(this.clockTimer);
  }

  // ---------------------------------------------------------------- analytics boxes
  /** Silent for 3 to 7 days: the 3 day group less the 7 day group it contains. */
  protected silent3To7(c: MeterCommunication): number {
    return Math.max(0, c.nonCommunicating3Days - c.nonCommunicating7Days);
  }

  protected pct(part: number, whole: number): string {
    return whole === 0 ? '0' : ((part / whole) * 100).toFixed(1);
  }

  /** Bar width relative to the largest band, so small bands stay visible next to a large one. */
  protected bandWidth(count: number, w: WalletDistribution): number {
    const max = Math.max(...w.bands.map((b) => b.count), 1);
    return (count / max) * 100;
  }

  protected walletTotal(w: WalletDistribution): number {
    return w.bands.reduce((sum, b) => sum + b.totalBalance, 0);
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

  protected timeAgo(at: string): string {
    const minutes = Math.max(0, Math.round((this.now().getTime() - new Date(at).getTime()) / 60000));
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes} min ago`;
    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours} hr ago`;
    const days = Math.round(hours / 24);
    return days === 1 ? '1 day ago' : `${days} days ago`;
  }

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
}
