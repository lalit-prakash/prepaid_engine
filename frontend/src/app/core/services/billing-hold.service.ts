import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BillingHoldSummary } from '../models/billing-hold.model';

/** Talks to the real GET /api/v1/meter-data/billing-holds and
 * POST /api/v1/meter-data/{meterId}/billing-hold/clear endpoints — every hold here was
 * auto-raised the moment a real LoadSurveyInterval reported a negative-consumption sequence for
 * that meter, never hand-entered. Clearing one is a genuine action requiring a mandatory
 * resolution note; actual LS/DLP billing for the meter resumes the moment it clears. */
@Injectable({ providedIn: 'root' })
export class BillingHoldService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/meter-data`;

  constructor(private readonly http: HttpClient) {}

  list(activeOnly = true): Observable<BillingHoldSummary[]> {
    return this.http.get<BillingHoldSummary[]>(`${this.baseUrl}/billing-holds?activeOnly=${activeOnly}`);
  }

  clear(meterId: string, note: string): Observable<{ id: string; meterId: string; actualBillingBlocked: boolean; clearedAt: string }> {
    return this.http.post<{ id: string; meterId: string; actualBillingBlocked: boolean; clearedAt: string }>(
      `${this.baseUrl}/${encodeURIComponent(meterId)}/billing-hold/clear`, { note });
  }
}
