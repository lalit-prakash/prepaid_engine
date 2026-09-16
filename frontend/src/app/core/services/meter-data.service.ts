import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DailyLoadProfileSummary } from '../models/meter-data.model';

/** Talks to the real GET /api/v1/meter-data/dlp endpoint — the raw Daily Load Profile stream,
 * capped at 500 most-recent rows (this project has no pagination anywhere; see the endpoint's
 * own doc comment). Read-only by design — this is ingested meter data, never edited from a UI.
 * DLP is the sole driver of ongoing prepaid billing now that the hourly Load Survey (LS) pipeline
 * has been removed. */
@Injectable({ providedIn: 'root' })
export class MeterDataService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/meter-data`;

  constructor(private readonly http: HttpClient) {}

  listDailyLoadProfiles(): Observable<DailyLoadProfileSummary[]> {
    return this.http.get<DailyLoadProfileSummary[]>(`${this.baseUrl}/dlp`);
  }
}
