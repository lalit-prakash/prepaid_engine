import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BillingHoldSummary } from '../models/billing-hold.model';

/** One meter's outcome from a bulk clear — a mix of cleared and skipped meters is expected and
 * not itself an error (see BillingHoldService.clearBulk). */
export interface BulkClearResult {
  meterId: string;
  cleared: boolean;
  error: string | null;
  id?: string;
  clearedAt?: string;
}

/** Talks to the real GET /api/v1/meter-data/billing-holds,
 * POST /api/v1/meter-data/{meterId}/billing-hold/clear, and
 * POST /api/v1/meter-data/billing-holds/clear-bulk endpoints — every hold here was auto-raised
 * the moment a real LoadSurveyInterval reported a negative-consumption sequence for that meter,
 * never hand-entered. Clearing one (or several) is a genuine action requiring a mandatory
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

  /** One shared resolution note is applied to every meter in the batch — see the real endpoint's
   * own doc comment for why per-item notes aren't supported here. A meter with no active hold is
   * reported per-item as cleared: false rather than failing the whole batch. */
  clearBulk(meterIds: string[], note: string): Observable<BulkClearResult[]> {
    return this.http.post<BulkClearResult[]>(`${this.baseUrl}/billing-holds/clear-bulk`, { meterIds, note });
  }
}
