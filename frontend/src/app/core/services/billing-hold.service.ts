import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BillingHoldSummary, BillingHoldSummaryStats } from '../models/billing-hold.model';
import { Page } from '../../shared/utils/paged-list';

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
 * POST /api/v1/meter-data/billing-holds/clear-bulk endpoints. Clearing one (or several) is a
 * genuine action requiring a mandatory resolution note; actual DLP billing for the meter resumes
 * the moment it clears. */
@Injectable({ providedIn: 'root' })
export class BillingHoldService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/meter-data`;

  constructor(private readonly http: HttpClient) {}

  /** GET /meter-data/billing-holds/search: newest first, keyset-paged. */
  search(params: { q?: string; activeOnly: boolean; after?: string | null; pageSize?: number }): Observable<Page<BillingHoldSummary>> {
    const query: Record<string, string> = { activeOnly: String(params.activeOnly) };
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<Page<BillingHoldSummary>>(`${this.baseUrl}/billing-holds/search`, { params: query });
  }

  summary(): Observable<BillingHoldSummaryStats> {
    return this.http.get<BillingHoldSummaryStats>(`${this.baseUrl}/billing-holds/summary`);
  }

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
