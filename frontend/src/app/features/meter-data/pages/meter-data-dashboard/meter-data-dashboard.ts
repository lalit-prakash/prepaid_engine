import { OperateOnly } from '../../../../shared/directives/operate-only';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, ElementRef, OnDestroy, OnInit, signal, viewChild } from '@angular/core';
import { Subject, Subscription, debounceTime } from 'rxjs';
import { PagedTab } from './paged-tab';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MeterDataService } from '../../../../core/services/meter-data.service';
import {
  DailyLoadProfileSummary,
  DailyProfileStatus,
  RegisterReadingSummary,
  LoadSurveyIntervalSummary,
  InstantaneousReadingSummary,
  MeterEventSummary,
  MeterEventCode,
  MeterAlarmSummary,
  MeterAlarmStatus,
  MeterAlarmCode,
  MeterDataQuery,
} from '../../../../core/models/meter-data.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

type MeterDataTab = 'dlp' | 'bp' | 'ls' | 'ip' | 'events' | 'alarms';
type PagedTabId = Exclude<MeterDataTab, 'ip'>;

/**
 * Meter Data views - one tab per MDMS profile type, each with its own role (see each entity's
 * doc comment on the backend): DLP is the sole driver of ongoing prepaid billing; BP/LS/IP are
 * register-validation / consumption-intelligence / meter-health only, never a billing input;
 * Events and Alarms stay separate, matching their separate backend tables. DLP, BP, LS, Events and
 * Alarms are searched, filtered and paged on the server (keyset pagination), so the browser holds
 * one page at a time and a search can never miss rows that a row cap would have hidden. IP is the
 * latest reading per meter. Read-only except acknowledging/resolving an alarm.
 */
@Component({
  selector: 'pe-meter-data-dashboard',
  imports: [OperateOnly, StatusBadge, DecimalPipe, DatePipe, FormsModule, RouterLink],
  templateUrl: './meter-data-dashboard.html',
  styleUrl: './meter-data-dashboard.scss',
})
export class MeterDataDashboard implements OnInit, OnDestroy {
  protected readonly activeTab = signal<MeterDataTab>('dlp');

  protected readonly searchTerm = signal('');
  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');
  protected readonly statusFilter = signal('');

  protected readonly dlp: PagedTab<DailyLoadProfileSummary>;
  protected readonly bp: PagedTab<RegisterReadingSummary>;
  protected readonly ls: PagedTab<LoadSurveyIntervalSummary>;
  protected readonly events: PagedTab<MeterEventSummary>;
  protected readonly alarms: PagedTab<MeterAlarmSummary>;

  protected readonly instantaneousReadings = signal<InstantaneousReadingSummary[]>([]);
  protected readonly ipLoading = signal(false);
  protected readonly ipError = signal<string | null>(null);
  protected ipLoaded = false;

  protected readonly actingAlarmId = signal<string | null>(null);
  protected readonly actingAlarmMode = signal<'acknowledge' | 'resolve' | null>(null);
  protected readonly alarmNote = signal('');
  protected readonly alarmActionError = signal<string | null>(null);
  protected readonly alarmActionSubmitting = signal(false);

  protected readonly DailyProfileStatus = DailyProfileStatus;
  protected readonly MeterAlarmStatus = MeterAlarmStatus;

  /** Status filter options per tab (enum names understood by the API); tabs without one omit it. */
  protected readonly statusOptions: Partial<Record<MeterDataTab, { label: string; value: string }[]>> = {
    dlp: [
      { label: 'Received', value: 'Received' },
      { label: 'Validated', value: 'Validated' },
      { label: 'Rejected', value: 'Rejected' },
      { label: 'Billed', value: 'Billed' },
      { label: 'Provisional', value: 'Provisional' },
      { label: 'Reconciled', value: 'Reconciled' },
    ],
    bp: [
      { label: 'Received', value: 'Received' },
      { label: 'Validated', value: 'Validated' },
      { label: 'Rejected', value: 'Rejected' },
    ],
    alarms: [
      { label: 'Open', value: 'Open' },
      { label: 'Acknowledged', value: 'Acknowledged' },
      { label: 'Resolved', value: 'Resolved' },
    ],
  };

  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');
  private readonly fromBox = viewChild<ElementRef<HTMLInputElement>>('fromBox');
  private readonly toBox = viewChild<ElementRef<HTMLInputElement>>('toBox');
  private readonly search$ = new Subject<string>();
  private searchSub?: Subscription;

  constructor(
    private readonly meterDataService: MeterDataService,
    private readonly route: ActivatedRoute,
  ) {
    this.dlp = new PagedTab((q) => this.meterDataService.searchDailyLoadProfiles(q), 'Could not load Daily Load Profile data from the API.');
    this.bp = new PagedTab((q) => this.meterDataService.searchRegisterReadings(q), 'Could not load Billing Profile data from the API.');
    this.ls = new PagedTab((q) => this.meterDataService.searchLoadSurveyIntervals(q), 'Could not load Load Survey data from the API.');
    this.events = new PagedTab((q) => this.meterDataService.searchMeterEvents(q), 'Could not load meter event data from the API.');
    this.alarms = new PagedTab((q) => this.meterDataService.searchMeterAlarms(q), 'Could not load meter alarm data from the API.');
  }

  ngOnInit(): void {
    // Prefills from a cross-link (e.g. Billing Holds' "View DLP data") - the same server-side search.
    const initialQuery = this.route.snapshot.queryParamMap.get('q');
    if (initialQuery) this.searchTerm.set(initialQuery);

    this.searchSub = this.search$.pipe(debounceTime(300)).subscribe((term) => {
      if (term === this.searchTerm()) return;
      this.searchTerm.set(term);
      this.reloadActive();
    });

    this.dlp.reset(this.filters());
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
    for (const t of [this.dlp, this.bp, this.ls, this.events, this.alarms]) t.destroy();
  }

  protected get active(): PagedTab<unknown> | null {
    return this.activeTab() === 'ip' ? null : (this.pagedTab(this.activeTab() as PagedTabId) as PagedTab<unknown>);
  }

  private pagedTab(tab: PagedTabId): PagedTab<unknown> {
    return { dlp: this.dlp, bp: this.bp, ls: this.ls, events: this.events, alarms: this.alarms }[tab] as PagedTab<unknown>;
  }

  private filters(): Omit<MeterDataQuery, 'after' | 'pageSize'> {
    return { q: this.searchTerm(), from: this.fromDate(), to: this.toDate(), status: this.statusFilter() };
  }

  selectTab(tab: MeterDataTab): void {
    this.activeTab.set(tab);
    this.clearFilterState();

    if (tab === 'ip') {
      if (!this.ipLoaded) this.loadIp();
      return;
    }
    // Filters were reset, so always start this tab from its first page.
    this.pagedTab(tab).reset(this.filters());
  }

  private loadIp(): void {
    this.ipLoaded = true;
    this.ipLoading.set(true);
    this.meterDataService.listLatestInstantaneousReadings().subscribe({
      next: (rows) => { this.instantaneousReadings.set(rows); this.ipLoading.set(false); },
      error: () => { this.ipError.set('Could not load Instantaneous Profile data from the API.'); this.ipLoading.set(false); },
    });
  }

  onSearchInput(term: string): void {
    this.search$.next(term);
  }

  onDateChange(which: 'from' | 'to', value: string): void {
    (which === 'from' ? this.fromDate : this.toDate).set(value);
    this.reloadActive();
  }

  onStatusChange(value: string): void {
    this.statusFilter.set(value);
    this.reloadActive();
  }

  clearFilters(): void {
    this.clearFilterState();
    this.reloadActive();
  }

  private clearFilterState(): void {
    for (const box of [this.searchBox(), this.fromBox(), this.toBox()]) {
      if (box) box.nativeElement.value = '';
    }
    this.searchTerm.set('');
    this.fromDate.set('');
    this.toDate.set('');
    this.statusFilter.set('');
  }

  protected get hasActiveFilters(): boolean {
    return !!(this.searchTerm().trim() || this.fromDate() || this.toDate() || this.statusFilter());
  }

  private reloadActive(): void {
    if (this.activeTab() === 'ip') return;
    this.pagedTab(this.activeTab() as PagedTabId).reset(this.filters());
  }

  nextPage(): void {
    this.active?.next(this.filters());
  }

  previousPage(): void {
    this.active?.previous(this.filters());
  }

  retry(): void {
    this.active?.load(this.filters());
  }

  /** IP has one row per meter and is filtered in the browser over that small snapshot. */
  protected get filteredInstantaneousReadings(): InstantaneousReadingSummary[] {
    const term = this.searchTerm().trim().toLowerCase();
    const rows = this.instantaneousReadings();
    if (!term) return rows;
    return rows.filter(
      (r) => r.accountNumber.toLowerCase().includes(term) || r.name.toLowerCase().includes(term) || r.meterNumber.toLowerCase().includes(term),
    );
  }

  /** Which of the two daily billing stages this profile's receipt time would have qualified
   * for — 8:30-9:30 AM bills anything received by 8:00 AM, 12:30-1:30 PM bills anything received
   * between 8:00 AM and 12:00 PM (plus provisional billing for what's still missing by then). A
   * display-only classification of the real `receivedAt` timestamp, not a separate field. */
  protected receivedStageLabel(receivedAt: string): string {
    const hour = new Date(receivedAt).getHours() + new Date(receivedAt).getMinutes() / 60;
    if (hour < 8) return 'Stage 1 (8:30-9:30 AM)';
    if (hour < 12) return 'Stage 2 (12:30-1:30 PM)';
    return 'After 12 PM cutoff';
  }

  startAcknowledge(id: string): void {
    this.actingAlarmId.set(id);
    this.actingAlarmMode.set('acknowledge');
    this.alarmNote.set('');
    this.alarmActionError.set(null);
  }

  startResolve(id: string): void {
    this.actingAlarmId.set(id);
    this.actingAlarmMode.set('resolve');
    this.alarmNote.set('');
    this.alarmActionError.set(null);
  }

  cancelAlarmAction(): void {
    this.actingAlarmId.set(null);
    this.actingAlarmMode.set(null);
    this.alarmNote.set('');
    this.alarmActionError.set(null);
  }

  protected eventCodeLabel(code: MeterEventCode): string {
    switch (code) {
      case MeterEventCode.PowerFailure: return 'Power Failure';
      case MeterEventCode.PowerRestoration: return 'Power Restoration';
      case MeterEventCode.CommunicationFailure: return 'Communication Failure';
      case MeterEventCode.CommunicationRestoration: return 'Communication Restoration';
      case MeterEventCode.RelayClosed: return 'Relay Closed';
      case MeterEventCode.RelayOpened: return 'Relay Opened';
      case MeterEventCode.MeterClockChanged: return 'Meter Clock Changed';
      default: return 'Other';
    }
  }

  protected alarmCodeLabel(code: MeterAlarmCode): string {
    switch (code) {
      case MeterAlarmCode.Tamper: return 'Tamper';
      case MeterAlarmCode.MagneticInfluence: return 'Magnetic Influence';
      case MeterAlarmCode.CoverOpen: return 'Cover Open';
      case MeterAlarmCode.ReverseEnergy: return 'Reverse Energy';
      case MeterAlarmCode.VoltageAbnormality: return 'Voltage Abnormality';
      case MeterAlarmCode.CurrentAbnormality: return 'Current Abnormality';
      default: return 'Other';
    }
  }

  submitAlarmAction(id: string): void {
    const value = this.alarmNote().trim();
    if (!value) {
      this.alarmActionError.set(this.actingAlarmMode() === 'resolve' ? 'A resolution note is required.' : 'Your operator identity is required.');
      return;
    }

    this.alarmActionSubmitting.set(true);
    const request$ = this.actingAlarmMode() === 'resolve'
      ? this.meterDataService.resolveAlarm(id, value)
      : this.meterDataService.acknowledgeAlarm(id, value);

    request$.subscribe({
      next: () => {
        this.alarmActionSubmitting.set(false);
        this.cancelAlarmAction();
        this.alarms.load(this.filters());
      },
      error: () => {
        this.alarmActionSubmitting.set(false);
        this.alarmActionError.set('Could not reach the API — the alarm was not updated.');
      },
    });
  }
}
