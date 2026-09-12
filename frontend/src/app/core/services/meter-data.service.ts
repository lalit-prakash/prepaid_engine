import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DailyLoadProfileSummary, LoadSurveyIntervalSummary } from '../models/meter-data.model';

/** Talks to the real GET /api/v1/meter-data/ls and GET /api/v1/meter-data/dlp endpoints — the
 * raw Load Survey / Daily Load Profile streams, capped at 500 most-recent rows each (this
 * project has no pagination anywhere; see the endpoint's own doc comment). Read-only by design —
 * these are ingested meter data, never edited from a UI. */
@Injectable({ providedIn: 'root' })
export class MeterDataService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/meter-data`;

  constructor(private readonly http: HttpClient) {}

  listLoadSurvey(): Observable<LoadSurveyIntervalSummary[]> {
    return this.http.get<LoadSurveyIntervalSummary[]>(`${this.baseUrl}/ls`);
  }

  listDailyLoadProfiles(): Observable<DailyLoadProfileSummary[]> {
    return this.http.get<DailyLoadProfileSummary[]>(`${this.baseUrl}/dlp`);
  }
}
