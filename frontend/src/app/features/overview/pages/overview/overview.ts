import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ConsumerService } from '../../../../core/services/consumer.service';
import { ConnectionStatus, ConsumerSummary } from '../../../../core/models/consumer.model';
import { MeterDataService } from '../../../../core/services/meter-data.service';
import { DailyLoadProfileSummary } from '../../../../core/models/meter-data.model';
import { NotificationService } from '../../../../core/services/notification.service';
import { NotificationStatus, NotificationSummary } from '../../../../core/models/notification.model';
import { RechargeService } from '../../../../core/services/recharge.service';
import { RechargeStatus, RechargeSummary } from '../../../../core/models/recharge.model';
import { ConnectivityCommandService } from '../../../../core/services/connectivity-command.service';
import { ConnectivityCommandStatus, ConnectivityCommandSummary } from '../../../../core/models/connectivity-command.model';
import { BillingHoldService } from '../../../../core/services/billing-hold.service';
import { BillingHoldSummary } from '../../../../core/models/billing-hold.model';
import { OperationalExceptionService } from '../../../../core/services/operational-exception.service';
import { OperationalExceptionStatus, OperationalExceptionSummary } from '../../../../core/models/operational-exception.model';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { Icon } from '../../../../shared/components/icon/icon';

/** One slice of a donut chart built strictly from real rows (DLP billing status or consumer
 * connection/wallet state) — never a fabricated category like "Comm. Lost" or "Others". */
interface DonutSlice {
  label: string;
  count: number;
  pct: number;
  tone: 'success' | 'warning' | 'danger' | 'info' | 'neutral';
}

/** One row of the "Attention Required" list — assembled from real Open exceptions, active
 * billing holds, and Pending/Failed notifications. Never hand-authored copy. */
interface AttentionItem {
  id: string;
  severity: 'critical' | 'warning';
  title: string;
  detail: string;
  at: string;
}

/** One day's real consumption total, built from Daily Load Profile rows for that date. */
interface ConsumptionDay {
  date: string;
  totalKwh: number;
}

/**
 * The top-level operations dashboard. Every card below is either backed by a real endpoint
 * this app already exposes, or explicitly rendered as "Data unavailable" — nothing here is a
 * fabricated number wearing an "Illustrative" label. The billing-status donut is built strictly
 * from Daily Load Profile (DLP) data — the sole driver of ongoing prepaid billing.
 */
@Component({
  selector: 'pe-overview',
  imports: [KpiCard, StatusBadge, Icon, DecimalPipe, DatePipe, RouterLink],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview implements OnInit, OnDestroy {
  /** Real, client-computed wall-clock time — never a fabricated/stale timestamp. */
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

  protected readonly recharges = signal<RechargeSummary[]>([]);
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

  protected readonly attentionFilter = signal<'all' | 'critical' | 'warning'>('all');

  protected readonly NotificationStatus = NotificationStatus;
  protected readonly RechargeStatus = RechargeStatus;
  protected readonly ConnectivityCommandStatus = ConnectivityCommandStatus;

  protected readonly quickReports = [
    { label: 'Daily Billing Report', path: '/reports/daily-billing' },
    { label: 'Charge Calculation Report', path: '/reports/charge-calculation' },
    { label: 'Reconciliation Summary', path: '/reconciliation' },
    { label: 'Exceptions Register', path: '/exceptions' },
  ];

  constructor(
    private readonly consumerService: ConsumerService,
    private readonly meterDataService: MeterDataService,
    private readonly notificationService: NotificationService,
    private readonly rechargeService: RechargeService,
    private readonly connectivityCommandService: ConnectivityCommandService,
    private readonly billingHoldService: BillingHoldService,
    private readonly exceptionService: OperationalExceptionService,
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
      next: (profiles) => {
        this.dlpProfiles.set(profiles);
        this.dlpLoading.set(false);
      },
      error: () => {
        this.dlpError.set(true);
        this.dlpLoading.set(false);
      },
    });

    this.notificationService.list().subscribe({
      next: (notifications) => {
        this.notifications.set(notifications);
        this.notificationsLoading.set(false);
      },
      error: () => {
        this.notificationsError.set(true);
        this.notificationsLoading.set(false);
      },
    });

    this.rechargeService.list().subscribe({
      next: (recharges) => {
        this.recharges.set(recharges);
        this.rechargesLoading.set(false);
      },
      error: () => {
        this.rechargesError.set(true);
        this.rechargesLoading.set(false);
      },
    });

    this.connectivityCommandService.list().subscribe({
      next: (commands) => {
        this.connectivityCommands.set(commands);
        this.connectivityLoading.set(false);
      },
      error: () => {
        this.connectivityError.set(true);
        this.connectivityLoading.set(false);
      },
    });

    this.billingHoldService.list(true).subscribe({
      next: (holds) => {
        this.billingHolds.set(holds);
        this.billingHoldsLoading.set(false);
      },
      error: () => {
        this.billingHoldsError.set(true);
        this.billingHoldsLoading.set(false);
      },
    });

    this.exceptionService.list().subscribe({
      next: (rows) => {
        this.exceptions.set(rows);
        this.exceptionsLoading.set(false);
      },
      error: () => {
        this.exceptionsError.set(true);
        this.exceptionsLoading.set(false);
      },
    });
  }

  ngOnDestroy(): void {
    if (this.clockTimer) clearInterval(this.clockTimer);
  }

  protected get greeting(): string {
    const hour = this.now().getHours();
    if (hour < 12) return 'Good morning';
    if (hour < 17) return 'Good afternoon';
    return 'Good evening';
  }

  protected get formattedDateTime(): string {
    return this.now().toLocaleString('en-IN', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  /** Share of consumers with an Active connection status — a real, derivable proxy
   * for "healthy meters" from actual consumer data. Never a fabricated percentage. */
  protected get healthyMetersAvailable(): boolean {
    return !this.loading() && !this.error() && this.consumers().length > 0;
  }

  protected get healthyMetersPct(): number {
    const rows = this.consumers();
    if (rows.length === 0) return 0;
    const healthy = rows.filter((c) => c.connectionStatus === ConnectionStatus.Active).length;
    return Math.round((healthy / rows.length) * 100);
  }

  protected get healthyMetersFraction(): string {
    const rows = this.consumers();
    const healthy = rows.filter((c) => c.connectionStatus === ConnectionStatus.Active).length;
    return `${healthy} / ${rows.length}`;
  }

  protected get totalWalletBalance(): number {
    return this.consumers().reduce((sum, c) => sum + c.walletBalance, 0);
  }

  protected get lowBalanceCount(): number {
    return this.consumers().filter((c) => c.walletBalance < c.emergencyCreditLimit).length;
  }

  protected get disconnectedCount(): number {
    return this.consumers().filter((c) => c.connectionStatus === ConnectionStatus.Disconnected).length;
  }

  /** Today's DLP rows, if any have been generated yet — used for the billing-progress KPI and
   * donut. A brand-new demo environment or a day with no DLP ingested yet genuinely has none;
   * that is shown as "Data unavailable", never backfilled with a guess. */
  private get todaysDlpProfiles(): DailyLoadProfileSummary[] {
    const profiles = this.dlpProfiles();
    if (profiles.length === 0) return [];
    const latestDate = profiles.reduce((max, p) => (p.profileDate > max ? p.profileDate : max), profiles[0].profileDate);
    return profiles.filter((p) => p.profileDate === latestDate);
  }

  protected get billingProgressAvailable(): boolean {
    return !this.dlpLoading() && !this.dlpError() && this.todaysDlpProfiles.length > 0;
  }

  protected get billingProgressPct(): number {
    const rows = this.todaysDlpProfiles;
    if (rows.length === 0) return 0;
    const billed = rows.filter((r) => r.status === 3 /* Billed */ || r.status === 5 /* Reconciled */).length;
    return Math.round((billed / rows.length) * 100);
  }

  /** DLP status breakdown for the most recent profile date. */
  protected readonly dlpStatusDonut = computed<DonutSlice[]>(() => {
    const rows = this.todaysDlpProfiles;
    if (rows.length === 0) return [];
    const counts = { billed: 0, provisional: 0, received: 0, other: 0 };
    for (const row of rows) {
      if (row.status === 3 || row.status === 5) counts.billed++;
      else if (row.isProvisional || row.status === 4) counts.provisional++;
      else if (row.status === 0 || row.status === 1) counts.received++;
      else counts.other++;
    }
    const total = rows.length;
    const slices: DonutSlice[] = [
      { label: 'Billed', count: counts.billed, pct: Math.round((counts.billed / total) * 100), tone: 'success' },
      { label: 'Provisional', count: counts.provisional, pct: Math.round((counts.provisional / total) * 100), tone: 'warning' },
      { label: 'Received', count: counts.received, pct: Math.round((counts.received / total) * 100), tone: 'info' },
    ];
    if (counts.other > 0) {
      slices.push({ label: 'Rejected / other', count: counts.other, pct: Math.round((counts.other / total) * 100), tone: 'neutral' });
    }
    return slices.filter((s) => s.count > 0);
  });

  /** CSS conic-gradient stops built from the donut slices above. */
  protected readonly dlpDonutGradient = computed(() => this.buildGradient(this.dlpStatusDonut()));

  protected readonly recentRecharges = computed(() => this.recharges().slice(0, 6));
  protected readonly recentNotifications = computed(() => this.notifications().slice(0, 6));
  protected readonly recentMeterOps = computed(() => this.connectivityCommands().slice(0, 6));

  /** "Prepaid Network Health" donut — real categories only, computed from each consumer's
   * actual connection status and wallet balance. No "Comm. Lost"/"Others" bucket exists here
   * because this app has no such backend concept to report on. */
  protected readonly networkHealthAvailable = computed(() => !this.loading() && !this.error() && this.consumers().length > 0);

  protected readonly networkHealthDonut = computed<DonutSlice[]>(() => {
    const rows = this.consumers();
    const total = rows.length;
    if (total === 0) return [];
    const disconnected = rows.filter((c) => c.connectionStatus === ConnectionStatus.Disconnected).length;
    const lowBalance = rows.filter(
      (c) => c.connectionStatus !== ConnectionStatus.Disconnected && c.walletBalance < c.emergencyCreditLimit,
    ).length;
    const healthy = total - disconnected - lowBalance;
    const slices: DonutSlice[] = [
      { label: 'Healthy', count: healthy, pct: Math.round((healthy / total) * 100), tone: 'success' },
      { label: 'Low Balance', count: lowBalance, pct: Math.round((lowBalance / total) * 100), tone: 'warning' },
      { label: 'Disconnected', count: disconnected, pct: Math.round((disconnected / total) * 100), tone: 'danger' },
    ];
    return slices.filter((s) => s.count > 0);
  });

  protected readonly networkHealthGradient = computed(() => this.buildGradient(this.networkHealthDonut()));

  protected readonly networkHealthPct = computed(() => {
    const total = this.consumers().length;
    if (total === 0) return 0;
    const healthy = this.networkHealthDonut().find((s) => s.label === 'Healthy')?.count ?? 0;
    return Math.round((healthy / total) * 100);
  });

  /** Real daily consumption totals from Daily Load Profile rows, grouped by profile date —
   * replaces the hourly consumption chart the old Load Survey pipeline used to drive, which
   * this app no longer has any data source for. */
  protected readonly consumptionTrendAvailable = computed(() => !this.dlpLoading() && !this.dlpError() && this.dlpProfiles().length > 0);

  protected readonly dailyConsumptionTrend = computed<ConsumptionDay[]>(() => {
    const byDate = new Map<string, number>();
    for (const p of this.dlpProfiles()) {
      byDate.set(p.profileDate, (byDate.get(p.profileDate) ?? 0) + p.totalKwh);
    }
    return Array.from(byDate.entries())
      .sort(([a], [b]) => (a < b ? -1 : 1))
      .slice(-7)
      .map(([date, totalKwh]) => ({ date, totalKwh }));
  });

  protected readonly consumptionTrendMaxKwh = computed(() => {
    const days = this.dailyConsumptionTrend();
    return days.reduce((max, d) => Math.max(max, d.totalKwh), 0) || 1;
  });

  /** "Attention Required" — assembled strictly from real Open exceptions, active billing
   * holds, and Pending/Failed notifications. Never hand-authored placeholder copy. */
  protected readonly attentionItems = computed<AttentionItem[]>(() => {
    const items: AttentionItem[] = [];

    for (const e of this.exceptions()) {
      if (e.status !== OperationalExceptionStatus.Open) continue;
      items.push({ id: `exc-${e.id}`, severity: 'critical', title: e.description, detail: `${e.name} (${e.accountNumber})`, at: e.createdAt });
    }
    for (const h of this.billingHolds()) {
      if (h.clearedAt) continue;
      items.push({ id: `hold-${h.id}`, severity: 'warning', title: h.blockReason, detail: `${h.name} (${h.accountNumber}) — Meter ${h.meterNumber}`, at: h.blockedAt });
    }
    for (const n of this.notifications()) {
      if (n.status === NotificationStatus.Sent) continue;
      items.push({
        id: `notif-${n.id}`,
        severity: n.status === NotificationStatus.Failed ? 'critical' : 'warning',
        title: n.message,
        detail: `${n.name} (${n.accountNumber})`,
        at: n.createdAt,
      });
    }

    return items.sort((a, b) => (a.at < b.at ? 1 : -1));
  });

  protected readonly attentionCriticalCount = computed(() => this.attentionItems().filter((i) => i.severity === 'critical').length);
  protected readonly attentionWarningCount = computed(() => this.attentionItems().filter((i) => i.severity === 'warning').length);

  protected readonly filteredAttentionItems = computed(() => {
    const filter = this.attentionFilter();
    const items = this.attentionItems();
    const filtered = filter === 'all' ? items : items.filter((i) => i.severity === filter);
    return filtered.slice(0, 8);
  });

  protected readonly attentionAvailable = computed(
    () => !this.exceptionsLoading() && !this.billingHoldsLoading() && !this.notificationsLoading(),
  );
  protected readonly attentionError = computed(() => this.exceptionsError() || this.billingHoldsError() || this.notificationsError());

  setAttentionFilter(filter: 'all' | 'critical' | 'warning'): void {
    this.attentionFilter.set(filter);
  }

  private buildGradient(slices: DonutSlice[]): string {
    const toneColor: Record<DonutSlice['tone'], string> = {
      success: 'var(--color-success)',
      warning: 'var(--color-warning)',
      danger: 'var(--color-danger)',
      info: 'var(--color-info)',
      neutral: 'var(--color-neutral-400)',
    };
    let acc = 0;
    const stops = slices.map((s) => {
      const start = acc;
      acc += s.pct;
      return `${toneColor[s.tone]} ${start}% ${acc}%`;
    });
    return `conic-gradient(${stops.join(', ')})`;
  }

  goToConsumers(): void {
    this.router.navigate(['/consumers']);
  }
}
