import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
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
} from '../../../../core/models/meter-data.model';
import { StatusBadge } from '../../../../shared/components/badge/status-badge';
import { KpiCard } from '../../../../shared/components/kpi-card/kpi-card';

type MeterDataTab = 'dlp' | 'bp' | 'ls' | 'ip' | 'events' | 'alarms';

/**
 * Real, API-backed Meter Data views — one tab per MDMS profile type, each with its own role (see
 * each entity's own doc comment on the backend): DLP is the sole driver of ongoing prepaid
 * billing; BP/LS/IP are register-validation/consumption-intelligence/meter-health only, never a
 * billing input; Events/Alarms are kept as separate tabs matching their separate backend tables.
 * Read-only except acknowledging/resolving an alarm. Every tab loads its data lazily, on first
 * activation, to avoid firing five API calls for tabs the operator never opens.
 */
@Component({
  selector: 'pe-meter-data-dashboard',
  imports: [StatusBadge, KpiCard, DecimalPipe, DatePipe, FormsModule, RouterLink],
  templateUrl: './meter-data-dashboard.html',
  styleUrl: './meter-data-dashboard.scss',
})
export class MeterDataDashboard implements OnInit {
  protected readonly activeTab = signal<MeterDataTab>('dlp');
  protected readonly searchTerm = signal('');

  protected readonly profiles = signal<DailyLoadProfileSummary[]>([]);
  protected readonly dlpLoading = signal(true);
  protected readonly dlpError = signal<string | null>(null);

  protected readonly registerReadings = signal<RegisterReadingSummary[]>([]);
  protected readonly bpLoading = signal(false);
  protected readonly bpError = signal<string | null>(null);
  protected bpLoaded = false;

  protected readonly loadSurveyIntervals = signal<LoadSurveyIntervalSummary[]>([]);
  protected readonly lsLoading = signal(false);
  protected readonly lsError = signal<string | null>(null);
  protected lsLoaded = false;

  protected readonly instantaneousReadings = signal<InstantaneousReadingSummary[]>([]);
  protected readonly ipLoading = signal(false);
  protected readonly ipError = signal<string | null>(null);
  protected ipLoaded = false;

  protected readonly meterEvents = signal<MeterEventSummary[]>([]);
  protected readonly eventsLoading = signal(false);
  protected readonly eventsError = signal<string | null>(null);
  protected eventsLoaded = false;

  protected readonly meterAlarms = signal<MeterAlarmSummary[]>([]);
  protected readonly alarmsLoading = signal(false);
  protected readonly alarmsError = signal<string | null>(null);
  protected alarmsLoaded = false;

  protected readonly actingAlarmId = signal<string | null>(null);
  protected readonly actingAlarmMode = signal<'acknowledge' | 'resolve' | null>(null);
  protected readonly alarmNote = signal('');
  protected readonly alarmActionError = signal<string | null>(null);
  protected readonly alarmActionSubmitting = signal(false);

  protected readonly DailyProfileStatus = DailyProfileStatus;
  protected readonly MeterAlarmStatus = MeterAlarmStatus;

  constructor(
    private readonly meterDataService: MeterDataService,
    private readonly route: ActivatedRoute,
  ) {}

  ngOnInit(): void {
    // Prefills from a cross-link (e.g. Billing Holds' "View DLP data →") — real reuse of this
    // page's own search state, not a separate filtered endpoint.
    const initialQuery = this.route.snapshot.queryParamMap.get('q');
    if (initialQuery) this.searchTerm.set(initialQuery);

    this.meterDataService.listDailyLoadProfiles().subscribe({
      next: (profiles) => {
        this.profiles.set(profiles);
        this.dlpLoading.set(false);
      },
      error: () => {
        this.dlpError.set('Could not load Daily Load Profile data from the API.');
        this.dlpLoading.set(false);
      },
    });
  }

  selectTab(tab: MeterDataTab): void {
    this.activeTab.set(tab);
    this.searchTerm.set('');

    if (tab === 'bp' && !this.bpLoaded) {
      this.bpLoaded = true;
      this.bpLoading.set(true);
      this.meterDataService.listRegisterReadings().subscribe({
        next: (rows) => { this.registerReadings.set(rows); this.bpLoading.set(false); },
        error: () => { this.bpError.set('Could not load Billing Profile data from the API.'); this.bpLoading.set(false); },
      });
    } else if (tab === 'ls' && !this.lsLoaded) {
      this.lsLoaded = true;
      this.lsLoading.set(true);
      this.meterDataService.listLoadSurveyIntervals().subscribe({
        next: (rows) => { this.loadSurveyIntervals.set(rows); this.lsLoading.set(false); },
        error: () => { this.lsError.set('Could not load Load Survey data from the API.'); this.lsLoading.set(false); },
      });
    } else if (tab === 'ip' && !this.ipLoaded) {
      this.ipLoaded = true;
      this.ipLoading.set(true);
      this.meterDataService.listLatestInstantaneousReadings().subscribe({
        next: (rows) => { this.instantaneousReadings.set(rows); this.ipLoading.set(false); },
        error: () => { this.ipError.set('Could not load Instantaneous Profile data from the API.'); this.ipLoading.set(false); },
      });
    } else if (tab === 'events' && !this.eventsLoaded) {
      this.eventsLoaded = true;
      this.eventsLoading.set(true);
      this.meterDataService.listMeterEvents().subscribe({
        next: (rows) => { this.meterEvents.set(rows); this.eventsLoading.set(false); },
        error: () => { this.eventsError.set('Could not load meter event data from the API.'); this.eventsLoading.set(false); },
      });
    } else if (tab === 'alarms' && !this.alarmsLoaded) {
      this.loadAlarms();
    }
  }

  private loadAlarms(): void {
    this.alarmsLoaded = true;
    this.alarmsLoading.set(true);
    this.meterDataService.listMeterAlarms().subscribe({
      next: (rows) => { this.meterAlarms.set(rows); this.alarmsLoading.set(false); },
      error: () => { this.alarmsError.set('Could not load meter alarm data from the API.'); this.alarmsLoading.set(false); },
    });
  }

  protected get filteredProfiles(): DailyLoadProfileSummary[] {
    return this.filterByAccountNameMeter(this.profiles());
  }
  protected get filteredRegisterReadings(): RegisterReadingSummary[] {
    return this.filterByAccountNameMeter(this.registerReadings());
  }
  protected get filteredLoadSurveyIntervals(): LoadSurveyIntervalSummary[] {
    return this.filterByAccountNameMeter(this.loadSurveyIntervals());
  }
  protected get filteredInstantaneousReadings(): InstantaneousReadingSummary[] {
    return this.filterByAccountNameMeter(this.instantaneousReadings());
  }
  protected get filteredMeterEvents(): MeterEventSummary[] {
    return this.filterByAccountNameMeter(this.meterEvents());
  }
  protected get filteredMeterAlarms(): MeterAlarmSummary[] {
    return this.filterByAccountNameMeter(this.meterAlarms());
  }

  private filterByAccountNameMeter<T extends { accountNumber: string; name: string; meterNumber: string }>(rows: T[]): T[] {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return rows;
    return rows.filter(
      (r) => r.accountNumber.toLowerCase().includes(term) || r.name.toLowerCase().includes(term) || r.meterNumber.toLowerCase().includes(term),
    );
  }

  protected get provisionalCount(): number {
    return this.profiles().filter((p) => p.isProvisional).length;
  }
  protected get billedCount(): number {
    return this.profiles().filter((p) => p.status === DailyProfileStatus.Billed).length;
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
        this.alarmsLoaded = false;
        this.loadAlarms();
      },
      error: () => {
        this.alarmActionSubmitting.set(false);
        this.alarmActionError.set('Could not reach the API — the alarm was not updated.');
      },
    });
  }
}
