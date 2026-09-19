import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { AnalyticsOverview } from '../../../../core/models/analytics.model';
import { ConnectionStatus, ConsumerSummary } from '../../../../core/models/consumer.model';
import { DailyLoadProfileSummary } from '../../../../core/models/meter-data.model';
import { NotificationStatus, NotificationSummary } from '../../../../core/models/notification.model';
import { MeterCommandStatus, RechargeListItem, RechargeStatus } from '../../../../core/models/recharge.model';
import { ConnectivityCommandStatus, ConnectivityCommandSummary } from '../../../../core/models/connectivity-command.model';
import { BillingHoldSummary } from '../../../../core/models/billing-hold.model';
import { OperationalExceptionStatus, OperationalExceptionSummary } from '../../../../core/models/operational-exception.model';
import { RiskIndicatorsSummary } from '../../../../core/models/sla.model';
import { TariffChangeRequestStatus, TariffChangeRequestSummary } from '../../../../core/models/tariff-change-request.model';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { AuthService } from '../../../../core/services/auth.service';
import { BillingHoldService } from '../../../../core/services/billing-hold.service';
import { ConnectivityCommandService } from '../../../../core/services/connectivity-command.service';
import { ConsumerService } from '../../../../core/services/consumer.service';
import { MeterDataService } from '../../../../core/services/meter-data.service';
import { NotificationService } from '../../../../core/services/notification.service';
import { OperationalExceptionService } from '../../../../core/services/operational-exception.service';
import { RechargeService } from '../../../../core/services/recharge.service';
import { SlaService } from '../../../../core/services/sla.service';
import { TariffChangeRequestService } from '../../../../core/services/tariff-change-request.service';
import { Icon } from '../../../../shared/components/icon/icon';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
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
  imports: [KpiCard, StatusBadge, Icon, LineChart, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './overview.html',
  styleUrls: ['./overview.scss', './overview-lists.scss'],
})
export class Overview implements OnInit, OnDestroy {
  protected readonly now = signal(new Date());
  private clockTimer?: ReturnType<typeof setInterval>;

  protected readonly consumers = signal<ConsumerSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly dlpProfiles = signal<DailyLoadProfileSummary[]>([]);
  protected readonly dlpLoading = signal(true);
  protected readonly dlpError = signal(false);

  protected readonly notifications = signal<NotificationSummary[]>([]);
  protected readonly notificationsLoading = signal(true);
  protected readonly notificationsError = signal(false);

  protected readonly recharges = signal<RechargeListItem[]>([]);
  protected readonly rechargesLoading = signal(true);
  protected readonly rechargesError = signal(false);

  protected readonly connectivityCommands = signal<ConnectivityCommandSummary[]>([]);
  protected readonly connectivityLoading = signal(true);
  protected readonly connectivityError = signal(false);

  protected readonly billingHolds = signal<BillingHoldSummary[]>([]);
  protected readonly billingHoldsLoading = signal(true);
  protected readonly billingHoldsError = signal(false);

  protected readonly exceptions = signal<OperationalExceptionSummary[]>([]);
  protected readonly exceptionsLoading = signal(true);
  protected readonly exceptionsError = signal(false);

  protected readonly tariffRequests = signal<TariffChangeRequestSummary[]>([]);
  protected readonly tariffRequestsLoading = signal(true);
  protected readonly tariffRequestsError = signal(false);

  protected readonly risk = signal<RiskIndicatorsSummary | null>(null);
  protected readonly riskError = signal(false);

  protected readonly analytics = signal<AnalyticsOverview | null>(null);
  protected readonly analyticsLoading = signal(true);
  protected readonly analyticsError = signal(false);
  protected readonly trendTab = signal<TrendTab>('consumption');
  protected readonly trendDays = signal(30);

  /** Result of a real GET /health probe, with the measured round-trip time. */
  protected readonly apiHealth = signal<'checking' | 'healthy' | 'down'>('checking');
  protected readonly apiLatencyMs = signal<number | null>(null);

  protected readonly attentionFilter = signal<'all' | 'critical' | 'warning'>('all');

  protected readonly NotificationStatus = NotificationStatus;
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
    private readonly consumerService: ConsumerService,
    private readonly meterDataService: MeterDataService,
    private readonly notificationService: NotificationService,
    private readonly rechargeService: RechargeService,
    private readonly connectivityCommandService: ConnectivityCommandService,
    private readonly billingHoldService: BillingHoldService,
    private readonly exceptionService: OperationalExceptionService,
    private readonly tariffRequestService: TariffChangeRequestService,
    private readonly slaService: SlaService,
    private readonly analyticsService: AnalyticsService,
    private readonly auth: AuthService,
    private readonly http: HttpClient,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.clockTimer = setInterval(() => this.now.set(new Date()), 30_000);

    this.consumerService.list().subscribe({
      next: (consumers) => {
        this.consumers.set(consumers);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load consumer data from the API.');
        this.loading.set(false);
      },
    });

    this.meterDataService.listDailyLoadProfiles().subscribe({
      next: (rows) => { this.dlpProfiles.set(rows); this.dlpLoading.set(false); },
      error: () => { this.dlpError.set(true); this.dlpLoading.set(false); },
    });

    this.notificationService.list().subscribe({
      next: (rows) => { this.notifications.set(rows); this.notificationsLoading.set(false); },
      error: () => { this.notificationsError.set(true); this.notificationsLoading.set(false); },
    });

    this.rechargeService.search({ pageSize: 4 }).subscribe({
      next: (page) => { this.recharges.set(page.items); this.rechargesLoading.set(false); },
      error: () => { this.rechargesError.set(true); this.rechargesLoading.set(false); },
    });

    this.connectivityCommandService.list().subscribe({
      next: (rows) => { this.connectivityCommands.set(rows); this.connectivityLoading.set(false); },
      error: () => { this.connectivityError.set(true); this.connectivityLoading.set(false); },
    });

    this.billingHoldService.list(true).subscribe({
      next: (rows) => { this.billingHolds.set(rows); this.billingHoldsLoading.set(false); },
      error: () => { this.billingHoldsError.set(true); this.billingHoldsLoading.set(false); },
    });

    this.exceptionService.list().subscribe({
      next: (rows) => { this.exceptions.set(rows); this.exceptionsLoading.set(false); },
      error: () => { this.exceptionsError.set(true); this.exceptionsLoading.set(false); },
    });

    this.tariffRequestService.list().subscribe({
      next: (rows) => { this.tariffRequests.set(rows); this.tariffRequestsLoading.set(false); },
      error: () => { this.tariffRequestsError.set(true); this.tariffRequestsLoading.set(false); },
    });

    this.slaService.getRiskIndicators().subscribe({
      next: (r) => this.risk.set(r),
      error: () => this.riskError.set(true),
    });

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
    return this.auth.username() ?? 'Operator';
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

  protected get activeCount(): number {
    return this.consumers().filter((c) => c.connectionStatus === ConnectionStatus.Active).length;
  }

  protected get disconnectedCount(): number {
    return this.consumers().filter((c) => c.connectionStatus === ConnectionStatus.Disconnected).length;
  }

  protected get totalWalletBalance(): number {
    return this.consumers().reduce((sum, c) => sum + c.walletBalance, 0);
  }

  protected get lowBalanceCount(): number {
    return this.consumers().filter((c) => c.walletBalance < c.emergencyCreditLimit).length;
  }

  protected get healthyMetersPct(): number {
    const rows = this.consumers();
    return rows.length === 0 ? 0 : Math.round((this.activeCount / rows.length) * 100);
  }

  goToConsumers(queryParams?: Record<string, string>): void {
    this.router.navigate(['/consumers'], { queryParams });
  }

  goTo(link: string): void {
    this.router.navigateByUrl(link);
  }

  // ---------------------------------------------------------------- DLP billing progress
  private get latestDlp(): DailyLoadProfileSummary[] {
    const rows = this.dlpProfiles();
    if (rows.length === 0) return [];
    const latest = rows.reduce((max, p) => (p.profileDate > max ? p.profileDate : max), rows[0].profileDate);
    return rows.filter((p) => p.profileDate === latest);
  }

  protected get billingAvailable(): boolean {
    return !this.dlpLoading() && !this.dlpError() && this.latestDlp.length > 0;
  }

  /** Successful = Billed/Reconciled, Failed = Rejected, Pending = everything else (received, validated, provisional). */
  protected readonly billing = computed(() => {
    const rows = this.latestDlp;
    const successful = rows.filter((r) => r.status === 3 || r.status === 5).length;
    const failed = rows.filter((r) => r.status === 2).length;
    const total = rows.length;
    const latestReceived = rows.reduce<string | null>((max, r) => (max === null || r.receivedAt > max ? r.receivedAt : max), null);
    return {
      total,
      successful,
      failed,
      pending: total - successful - failed,
      pct: total === 0 ? 0 : Math.round((successful / total) * 100),
      profileDate: rows[0]?.profileDate ?? null,
      latestReceived,
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
  protected readonly networkHealthAvailable = computed(() => !this.loading() && !this.error() && this.consumers().length > 0);

  protected readonly networkHealth = computed<DonutSlice[]>(() => {
    const rows = this.consumers();
    const total = rows.length;
    if (total === 0) return [];
    const disconnected = rows.filter((c) => c.connectionStatus === ConnectionStatus.Disconnected).length;
    const low = rows.filter((c) => c.connectionStatus !== ConnectionStatus.Disconnected && c.walletBalance < c.emergencyCreditLimit).length;
    const healthy = total - disconnected - low;
    const slice = (label: string, count: number, tone: DonutSlice['tone']): DonutSlice => ({ label, count, pct: Math.round((count / total) * 100), tone });
    return [slice('Healthy', healthy, 'success'), slice('Low Balance', low, 'warning'), slice('Disconnected', disconnected, 'danger')].filter((s) => s.count > 0);
  });

  protected readonly networkGradient = computed(() => {
    const colour = { success: 'var(--color-success)', warning: 'var(--color-warning)', danger: 'var(--color-danger)' };
    const total = this.consumers().length || 1;
    let acc = 0;
    const stops = this.networkHealth().map((s) => {
      const start = acc;
      acc += (s.count / total) * 100;
      return `${colour[s.tone]} ${start}% ${acc}%`;
    });
    return `conic-gradient(${stops.join(', ')})`;
  });

  protected readonly networkHealthyPct = computed(() => {
    const total = this.consumers().length;
    const healthy = this.networkHealth().find((s) => s.label === 'Healthy')?.count ?? 0;
    return total === 0 ? 0 : Math.round((healthy / total) * 100);
  });

  // ---------------------------------------------------------------- attention required
  protected readonly attentionItems = computed<AttentionItem[]>(() => {
    const items: AttentionItem[] = [];

    for (const e of this.exceptions()) {
      if (e.status !== OperationalExceptionStatus.Open) continue;
      items.push({ id: `exc-${e.id}`, severity: 'critical', title: e.description, detail: `${e.name} (${e.accountNumber})`, at: e.createdAt, link: '/exceptions' });
    }
    for (const h of this.billingHolds()) {
      if (h.clearedAt) continue;
      items.push({ id: `hold-${h.id}`, severity: 'warning', title: h.blockReason, detail: `${h.name} (${h.accountNumber}) - Meter ${h.meterNumber}`, at: h.blockedAt, link: '/billing-holds' });
    }
    for (const n of this.notifications()) {
      if (n.status === NotificationStatus.Sent) continue;
      items.push({
        id: `notif-${n.id}`,
        severity: n.status === NotificationStatus.Failed ? 'critical' : 'warning',
        title: n.message,
        detail: `${n.name} (${n.accountNumber})`,
        at: n.createdAt,
        link: '/notifications',
      });
    }
    // Payment received but the meter never confirmed the credit: always critical.
    for (const r of this.recharges()) {
      if (r.meterCommandStatus === MeterCommandStatus.Failed || r.meterCommandStatus === MeterCommandStatus.TimedOut) {
        items.push({
          id: `credit-${r.id}`,
          severity: 'critical',
          title: `Meter credit ${r.meterCommandStatus === MeterCommandStatus.Failed ? 'failed' : 'timed out'} for ₹${r.amount}`,
          detail: `${r.name} (${r.accountNumber}) - payment received, meter not credited`,
          at: r.completedAt ?? r.initiatedAt,
          link: `/recharge/${r.id}`,
        });
      }
    }
    for (const t of this.tariffRequests()) {
      if (t.status === TariffChangeRequestStatus.PendingApproval) {
        items.push({ id: `tariff-${t.id}`, severity: 'warning', title: `Tariff approval pending: ${t.proposedName}`, detail: `Submitted by ${t.submittedBy ?? t.createdBy}`, at: t.submittedAt ?? t.createdAt, link: `/tariffs/change-requests/${t.id}` });
      } else if (t.status === TariffChangeRequestStatus.Scheduled) {
        items.push({ id: `tariff-${t.id}`, severity: 'warning', title: `Tariff activation scheduled: ${t.proposedName}`, detail: `Commences ${t.commencementDate ? t.commencementDate.slice(0, 10) : 'on a date not recorded'}`, at: t.approvedAt ?? t.createdAt, link: `/tariffs/change-requests/${t.id}` });
      }
    }
    return items.sort((a, b) => (a.at < b.at ? 1 : -1));
  });

  protected readonly attentionCritical = computed(() => this.attentionItems().filter((i) => i.severity === 'critical').length);
  protected readonly attentionWarning = computed(() => this.attentionItems().filter((i) => i.severity === 'warning').length);
  protected readonly attentionShown = computed(() => {
    const f = this.attentionFilter();
    const all = this.attentionItems();
    return (f === 'all' ? all : all.filter((i) => i.severity === f)).slice(0, 3);
  });
  protected readonly attentionReady = computed(
    () => !this.exceptionsLoading() && !this.billingHoldsLoading() && !this.notificationsLoading() && !this.rechargesLoading() && !this.tariffRequestsLoading(),
  );
  protected readonly attentionFailed = computed(
    () => this.exceptionsError() || this.billingHoldsError() || this.notificationsError() || this.rechargesError() || this.tariffRequestsError(),
  );

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
  protected readonly meterNumberByAccount = computed(() => new Map(this.consumers().map((c) => [c.accountNumber, c.meterNumber])));
  protected readonly recentMeterOps = computed(() => this.connectivityCommands().slice(0, 4));

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
