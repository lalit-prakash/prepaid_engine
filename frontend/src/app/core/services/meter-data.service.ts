import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  DailyLoadProfileSummary,
  RegisterReadingSummary,
  LoadSurveyIntervalSummary,
  InstantaneousReadingSummary,
  MeterEventSummary,
  MeterAlarmSummary,
} from '../models/meter-data.model';

/** Talks to the real GET /api/v1/meter-data/dlp endpoint — the raw Daily Load Profile stream,
 * capped at 500 most-recent rows (this project has no pagination anywhere; see the endpoint's
 * own doc comment). Read-only by design — this is ingested meter data, never edited from a UI.
 * DLP is the sole driver of ongoing prepaid billing now that the hourly Load Survey (LS) pipeline
 * has been removed. */
@Injectable({ providedIn: 'root' })
export class MeterDataService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/meter-data`;

  constructor(private readonly http: HttpClient) {}

  listDailyLoadProfiles(consumerId?: string): Observable<DailyLoadProfileSummary[]> {
    return this.http.get<DailyLoadProfileSummary[]>(`${this.baseUrl}/dlp`, { params: consumerId ? { consumerId } : {} });
  }

  /** GET /api/v1/meter-data/bp — Billing Profile register readings (register validation only,
   * never a billing input). */
  listRegisterReadings(): Observable<RegisterReadingSummary[]> {
    return this.http.get<RegisterReadingSummary[]>(`${this.baseUrl}/bp`);
  }

  /** GET /api/v1/meter-data/ls — Load Survey intervals (consumption intelligence only, never a
   * billing input). */
  listLoadSurveyIntervals(): Observable<LoadSurveyIntervalSummary[]> {
    return this.http.get<LoadSurveyIntervalSummary[]>(`${this.baseUrl}/ls`);
  }

  /** GET /api/v1/meter-data/ip/latest — the latest Instantaneous Profile reading per meter
   * (meter-health intelligence only). */
  listLatestInstantaneousReadings(): Observable<InstantaneousReadingSummary[]> {
    return this.http.get<InstantaneousReadingSummary[]>(`${this.baseUrl}/ip/latest`);
  }

  /** GET /api/v1/meter-data/events — informational meter history. */
  listMeterEvents(consumerId?: string): Observable<MeterEventSummary[]> {
    return this.http.get<MeterEventSummary[]>(`${this.baseUrl}/events`, { params: consumerId ? { consumerId } : {} });
  }

  /** GET /api/v1/meter-data/alarms — severity-bearing meter conditions. */
  listMeterAlarms(consumerId?: string): Observable<MeterAlarmSummary[]> {
    return this.http.get<MeterAlarmSummary[]>(`${this.baseUrl}/alarms`, { params: consumerId ? { consumerId } : {} });
  }

  acknowledgeAlarm(id: string, acknowledgedBy: string): Observable<MeterAlarmSummary> {
    return this.http.post<MeterAlarmSummary>(`${this.baseUrl}/alarms/${id}/acknowledge`, { acknowledgedBy });
  }

  resolveAlarm(id: string, resolutionNote: string): Observable<MeterAlarmSummary> {
    return this.http.post<MeterAlarmSummary>(`${this.baseUrl}/alarms/${id}/resolve`, { resolutionNote });
  }
}
