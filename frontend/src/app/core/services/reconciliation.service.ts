import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApplyReconciliationAdjustmentRequest, ReconciliationAdjustmentSummary, ReconciliationSummaryStats } from '../models/reconciliation.model';
import { Page } from '../../shared/utils/paged-list';

/** Talks to the real GET /api/v1/reconciliation-adjustments and
 * POST /api/v1/consumers/{accountNumber}/reconciliation-adjustments endpoints (AMISP integration
 * requirement doc §7-8) — applying one really credits/debits the consumer's wallet, exactly like
 * a recharge, tagged distinctly in the ledger. */
@Injectable({ providedIn: 'root' })
export class ReconciliationService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1`;

  constructor(private readonly http: HttpClient) {}

  /** GET /reconciliation-adjustments/search: newest first, keyset-paged. */
  search(params: { q?: string; after?: string | null; pageSize?: number }): Observable<Page<ReconciliationAdjustmentSummary>> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<Page<ReconciliationAdjustmentSummary>>(`${this.baseUrl}/reconciliation-adjustments/search`, { params: query });
  }

  summary(): Observable<ReconciliationSummaryStats> {
    return this.http.get<ReconciliationSummaryStats>(`${this.baseUrl}/reconciliation-adjustments/summary`);
  }

  list(): Observable<ReconciliationAdjustmentSummary[]> {
    return this.http.get<ReconciliationAdjustmentSummary[]>(`${this.baseUrl}/reconciliation-adjustments`);
  }

  apply(accountNumber: string, request: ApplyReconciliationAdjustmentRequest): Observable<ReconciliationAdjustmentSummary> {
    return this.http.post<ReconciliationAdjustmentSummary>(
      `${this.baseUrl}/consumers/${encodeURIComponent(accountNumber)}/reconciliation-adjustments`, request);
  }
}
