import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
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
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

/** One slice of the "Daily Billing Status" donut — built strictly from Daily Load Profile
 * (DLP) rows for the most recent profile date present in the feed. */
interface DlpStatusSlice {
  label: string;
  count: number;
  pct: number;
  tone: 'success' | 'warning' | 'info' | 'neutral';
}

/**
 * The top-level operations dashboard. Every card below is either backed by a real endpoint
 * this app already exposes, or explicitly rendered as "Data unavailable" — nothing here is a
 * fabricated number wearing an "Illustrative" label. The billing-status donut is built strictly
 * from Daily Load Profile (DLP) data — the sole driver of ongoing prepaid billing.
 */
@Component({
  selector: 'pe-overview',
  imports: [KpiCard, StatusBadge, DecimalPipe, RouterLink],
  templateUrl: './overview.html',
  styleUrl: './overview.scss',
})
export class Overview implements OnInit {
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
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
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
  protected readonly dlpStatusDonut = computed<DlpStatusSlice[]>(() => {
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
    const slices: DlpStatusSlice[] = [
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
  protected readonly dlpDonutGradient = computed(() => {
    const slices = this.dlpStatusDonut();
    const toneColor: Record<DlpStatusSlice['tone'], string> = {
      success: 'var(--color-success)',
      warning: 'var(--color-warning)',
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
  });

  protected readonly recentRecharges = computed(() => this.recharges().slice(0, 6));
  protected readonly recentNotifications = computed(() => this.notifications().slice(0, 6));
  protected readonly recentMeterOps = computed(() => this.connectivityCommands().slice(0, 6));

  goToConsumers(): void {
    this.router.navigate(['/consumers']);
  }
}
