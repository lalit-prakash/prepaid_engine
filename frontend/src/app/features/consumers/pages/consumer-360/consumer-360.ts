import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ConsumerService } from '../../../../core/services/consumer.service';
import { BillStatus, ConnectionStatus, ConsumerDetail, WalletTransactionType } from '../../../../core/models/consumer.model';
import { RechargeService } from '../../../../core/services/recharge.service';
import { MeterCommandStatus, RechargeStatus, RechargeSummary } from '../../../../core/models/recharge.model';
import { ConnectivityCommandService } from '../../../../core/services/connectivity-command.service';
import { ConnectivityCommandStatus, ConnectivityCommandSummary } from '../../../../core/models/connectivity-command.model';
import { MeterDataService } from '../../../../core/services/meter-data.service';
import {
  DailyLoadProfileSummary, DailyProfileStatus, MeterAlarmSeverity, MeterAlarmStatus, MeterAlarmSummary, MeterEventSummary,
} from '../../../../core/models/meter-data.model';
import { AuditEntryService } from '../../../../core/services/audit-entry.service';
import { AuditEntrySummary } from '../../../../core/models/audit-entry.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';

export type ConsumerTab = 'overview' | 'wallet' | 'billing' | 'recharge' | 'meter-ops' | 'meter-data' | 'timeline';

interface TabState<T> {
  data: T[];
  loading: boolean;
  error: boolean;
}

const emptyTab = <T>(): TabState<T> => ({ data: [], loading: false, error: false });

interface TimelineItem {
  at: string;
  kind: string;
  title: string;
  detail: string;
  tone: 'success' | 'warning' | 'danger' | 'info' | 'neutral';
  link?: string;
}

type RechargeOutcome =
  | { kind: 'success'; replayed: boolean; balance: number; meterCommandStatus: string | null }
  | { kind: 'pending'; message: string }
  | { kind: 'declined'; message: string }
  | { kind: 'unavailable'; message: string }
  | { kind: 'invalid'; message: string };

type ConnectivityOutcome =
  | { kind: 'success'; commandStatus: string; consumerStatus: string }
  | { kind: 'error'; message: string };

/**
 * Consumer 360 — the primary investigation screen. Financial figures are
 * deliberately kept apart per the RMS-source-of-truth boundary: "RMS Wallet
 * Balance" is the only value ever labeled "wallet"; everything else
 * (charges, rebate, FPPAS, ...) is labeled as an engine-calculated figure.
 */
@Component({
  selector: 'pe-consumer-360',
  imports: [FormsModule, StatusBadge, DatePipe, DecimalPipe, RouterLink],
  templateUrl: './consumer-360.html',
  styleUrl: './consumer-360.scss',
})
export class Consumer360 implements OnInit {
  protected readonly consumer = signal<ConsumerDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly tabs: { id: ConsumerTab; label: string }[] = [
    { id: 'overview', label: 'Overview' },
    { id: 'wallet', label: 'Wallet' },
    { id: 'billing', label: 'Billing' },
    { id: 'recharge', label: 'Recharge' },
    { id: 'meter-ops', label: 'Meter operations' },
    { id: 'meter-data', label: 'Meter data' },
    { id: 'timeline', label: 'Timeline' },
  ];
  protected readonly activeTab = signal<ConsumerTab>('overview');

  protected readonly recharges = signal<TabState<RechargeSummary>>(emptyTab());
  protected readonly commands = signal<TabState<ConnectivityCommandSummary>>(emptyTab());
  protected readonly dlp = signal<TabState<DailyLoadProfileSummary>>(emptyTab());
  protected readonly events = signal<TabState<MeterEventSummary>>(emptyTab());
  protected readonly alarms = signal<TabState<MeterAlarmSummary>>(emptyTab());
  protected readonly audit = signal<TabState<AuditEntrySummary>>(emptyTab());
  private readonly loadedTabs = new Set<ConsumerTab>();

  protected readonly RechargeStatus = RechargeStatus;
  protected readonly MeterCommandStatus = MeterCommandStatus;
  protected readonly ConnectivityCommandStatus = ConnectivityCommandStatus;
  protected readonly DailyProfileStatus = DailyProfileStatus;
  protected readonly MeterAlarmSeverity = MeterAlarmSeverity;
  protected readonly MeterAlarmStatus = MeterAlarmStatus;
  protected readonly ConnectionStatus = ConnectionStatus;
  protected readonly BillStatus = BillStatus;
  protected readonly WalletTransactionType = WalletTransactionType;

  protected accountNumber = '';
  protected rechargeAmount = 300;
  protected idempotencyKey = '';
  protected readonly rechargeSubmitting = signal(false);
  protected readonly rechargeOutcome = signal<RechargeOutcome | null>(null);

  protected disconnectReason = '';
  protected reconnectReason = '';
  protected readonly confirmingDisconnect = signal(false);
  protected readonly confirmingReconnect = signal(false);
  protected readonly connectivitySubmitting = signal(false);
  protected readonly connectivityOutcome = signal<ConnectivityOutcome | null>(null);

  constructor(
    private readonly route: ActivatedRoute,
    private readonly consumerService: ConsumerService,
    private readonly rechargeService: RechargeService,
    private readonly connectivityService: ConnectivityCommandService,
    private readonly meterDataService: MeterDataService,
    private readonly auditService: AuditEntryService,
  ) {}

  selectTab(tab: ConsumerTab): void {
    this.activeTab.set(tab);
    this.ensureTabData(tab);
  }

  /** Each tab fetches only this consumer's rows, and only the first time it is opened. */
  private ensureTabData(tab: ConsumerTab, force = false): void {
    const c = this.consumer();
    if (!c) return;
    if (force) this.loadedTabs.delete(tab);
    if (this.loadedTabs.has(tab)) return;
    this.loadedTabs.add(tab);

    if (tab === 'recharge' || tab === 'timeline') this.fetchInto(this.recharges, this.rechargeService.list(c.accountNumber));
    if (tab === 'meter-ops' || tab === 'timeline') this.fetchInto(this.commands, this.connectivityService.list(c.accountNumber));
    if (tab === 'meter-data') {
      this.fetchInto(this.dlp, this.meterDataService.listDailyLoadProfiles(c.id));
      this.fetchInto(this.events, this.meterDataService.listMeterEvents(c.id));
      this.fetchInto(this.alarms, this.meterDataService.listMeterAlarms(c.id));
    }
    if (tab === 'timeline') this.fetchInto(this.audit, this.auditService.list(c.id));
  }

  private fetchInto<T>(target: ReturnType<typeof signal<TabState<T>>>, source: Observable<T[]>): void {
    target.update((s) => ({ ...s, loading: true, error: false }));
    source.subscribe({
      next: (data) => target.set({ data, loading: false, error: false }),
      error: () => target.set({ data: [], loading: false, error: true }),
    });
  }

  /** Wallet figures derived strictly from the real ledger and bills. */
  protected readonly walletStats = computed(() => {
    const c = this.consumer();
    if (!c) return null;
    const recharges = c.wallet.transactions.filter((t) => t.type === WalletTransactionType.Recharge);
    const debits = c.wallet.transactions.filter((t) => t.type === WalletTransactionType.BillDebit);
    const last = recharges.length ? recharges.reduce((a, b) => (a.occurredAt > b.occurredAt ? a : b)) : null;
    return {
      available: c.wallet.balance + c.wallet.emergencyCreditLimit,
      cumulativeRecharge: recharges.reduce((sum, t) => sum + t.amount, 0),
      cumulativeCharges: debits.reduce((sum, t) => sum + Math.abs(t.amount), 0),
      lastRechargeAt: last?.occurredAt ?? null,
      lastRechargeAmount: last?.amount ?? null,
      lowBalance: c.wallet.balance < c.wallet.emergencyCreditLimit,
    };
  });

  protected readonly timeline = computed<TimelineItem[]>(() => {
    const items: TimelineItem[] = [];
    for (const r of this.recharges().data) {
      const credit = r.meterCommandStatus === null ? 'not dispatched' : MeterCommandStatus[r.meterCommandStatus].toLowerCase();
      const creditFailed = r.meterCommandStatus === MeterCommandStatus.Failed || r.meterCommandStatus === MeterCommandStatus.TimedOut;
      items.push({
        at: r.initiatedAt,
        kind: 'Recharge',
        title: `Recharge of ₹${r.amount}`,
        detail: `${r.rmsReferenceId} - payment ${RechargeStatus[r.status].toLowerCase()}, meter credit ${credit}`,
        tone: r.status === RechargeStatus.Failed || creditFailed ? 'danger'
          : r.status === RechargeStatus.Success && r.meterCommandStatus === MeterCommandStatus.Acknowledged ? 'success' : 'warning',
        link: `/recharge/${r.id}`,
      });
    }
    for (const c of this.commands().data) {
      items.push({
        at: c.createdAt,
        kind: 'Meter operation',
        title: c.commandType === 0 ? 'Disconnect requested' : 'Reconnect requested',
        detail: `${c.reason} - ${ConnectivityCommandStatus[c.status].toLowerCase()}${c.errorMessage ? ': ' + c.errorMessage : ''}`,
        tone: c.status === ConnectivityCommandStatus.Acknowledged ? 'success'
          : c.status === ConnectivityCommandStatus.Failed || c.status === ConnectivityCommandStatus.TimedOut ? 'danger' : 'info',
        link: `/rc-dc/${c.id}`,
      });
    }
    for (const a of this.audit().data) {
      items.push({ at: a.occurredAt, kind: 'Audit', title: a.action, detail: `by ${a.actor}${a.details ? ' - ' + a.details : ''}`, tone: 'neutral' });
    }
    for (const b of this.consumer()?.bills ?? []) {
      items.push({ at: b.generatedAt, kind: 'Bill', title: `Bill generated: ₹${b.amount.toFixed(2)}`, detail: `Bill ${b.id.slice(0, 8)}`, tone: 'info', link: `/billing/${b.id}` });
    }
    return items.sort((x, y) => (x.at < y.at ? 1 : -1));
  });

  protected readonly timelineLoading = computed(() => this.recharges().loading || this.commands().loading || this.audit().loading);
  protected readonly timelineError = computed(() => this.recharges().error || this.commands().error || this.audit().error);

  ngOnInit(): void {
    this.accountNumber = this.route.snapshot.paramMap.get('accountNumber') ?? '';
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.consumerService.getByAccountNumber(this.accountNumber).subscribe({
      next: (consumer) => {
        this.consumer.set(consumer);
        this.loading.set(false);
        // Re-fetch any tab the user has already opened so it reflects the post-action state.
        for (const t of Array.from(this.loadedTabs)) this.ensureTabData(t, true);
        this.idempotencyKey = this.freshKey();
      },
      error: (err) => {
        this.error.set(
          err?.status === 404
            ? `No consumer found for account "${this.accountNumber}".`
            : 'Could not load this consumer from the API.',
        );
        this.loading.set(false);
      },
    });
  }

  private freshKey(): string {
    return `ui-${Date.now()}`;
  }

  protected get latestBill() {
    const bills = this.consumer()?.bills ?? [];
    return bills.length ? bills[bills.length - 1] : null;
  }

  submitRecharge(): void {
    if (!(this.rechargeAmount > 0) || !this.idempotencyKey.trim()) {
      this.rechargeOutcome.set({ kind: 'invalid', message: 'Enter a valid amount and idempotency key.' });
      return;
    }
    this.rechargeSubmitting.set(true);
    this.rechargeOutcome.set(null);

    this.consumerService
      .recharge(this.accountNumber, { amount: this.rechargeAmount, idempotencyKey: this.idempotencyKey.trim() })
      .subscribe({
        next: (response) => {
          this.rechargeSubmitting.set(false);
          const body = response.body!;
          if (response.status === 202) {
            this.rechargeOutcome.set({ kind: 'pending', message: (body as any).message ?? 'RMS is still processing this recharge.' });
          } else {
            this.rechargeOutcome.set({
              kind: 'success',
              replayed: !!body.replayed,
              balance: body.walletBalance,
              meterCommandStatus: body.meterCommandStatus ?? null,
            });
            this.idempotencyKey = this.freshKey();
            this.load(); // re-fetch so the ledger/bill table reflect the real, post-recharge state
          }
        },
        error: (err) => {
          this.rechargeSubmitting.set(false);
          const status = err?.status;
          if (status === 402) {
            this.rechargeOutcome.set({ kind: 'declined', message: err?.error?.message ?? 'RMS declined this recharge.' });
          } else if (status === 503) {
            this.rechargeOutcome.set({ kind: 'unavailable', message: err?.error?.detail ?? 'RMS is unavailable — try again shortly.' });
          } else if (status === 400) {
            this.rechargeOutcome.set({ kind: 'invalid', message: err?.error?.error ?? 'Invalid request.' });
          } else {
            this.rechargeOutcome.set({ kind: 'invalid', message: `Unexpected response (${status ?? 'network error'}).` });
          }
        },
      });
  }

  requestDisconnect(): void {
    this.confirmingDisconnect.set(true);
  }
  cancelDisconnect(): void {
    this.confirmingDisconnect.set(false);
  }

  confirmDisconnect(): void {
    if (!this.disconnectReason.trim()) {
      this.connectivityOutcome.set({ kind: 'error', message: 'A reason is required to disconnect.' });
      return;
    }
    this.confirmingDisconnect.set(false);
    this.connectivitySubmitting.set(true);
    this.connectivityOutcome.set(null);

    this.consumerService.disconnect(this.accountNumber, { reason: this.disconnectReason.trim() }).subscribe({
      next: (result) => {
        this.connectivitySubmitting.set(false);
        this.connectivityOutcome.set({ kind: 'success', commandStatus: result.commandStatus, consumerStatus: result.consumerConnectionStatus });
        this.disconnectReason = '';
        this.load();
      },
      error: (err) => {
        this.connectivitySubmitting.set(false);
        this.connectivityOutcome.set({ kind: 'error', message: err?.error?.error ?? 'Could not disconnect this consumer.' });
      },
    });
  }

  requestReconnect(): void {
    this.confirmingReconnect.set(true);
  }
  cancelReconnect(): void {
    this.confirmingReconnect.set(false);
  }

  confirmReconnect(): void {
    if (!this.reconnectReason.trim()) {
      this.connectivityOutcome.set({ kind: 'error', message: 'A reason is required to reconnect.' });
      return;
    }
    this.confirmingReconnect.set(false);
    this.connectivitySubmitting.set(true);
    this.connectivityOutcome.set(null);

    this.consumerService.reconnect(this.accountNumber, { reason: this.reconnectReason.trim() }).subscribe({
      next: (result) => {
        this.connectivitySubmitting.set(false);
        this.connectivityOutcome.set({ kind: 'success', commandStatus: result.commandStatus, consumerStatus: result.consumerConnectionStatus });
        this.reconnectReason = '';
        this.load();
      },
      error: (err) => {
        this.connectivitySubmitting.set(false);
        this.connectivityOutcome.set({ kind: 'error', message: err?.error?.error ?? 'Could not reconnect this consumer.' });
      },
    });
  }
}
