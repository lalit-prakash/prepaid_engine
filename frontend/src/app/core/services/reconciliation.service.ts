import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApplyReconciliationAdjustmentRequest, ReconciliationAdjustmentSummary } from '../models/reconciliation.model';

/** Talks to the real GET /api/v1/reconciliation-adjustments and
 * POST /api/v1/consumers/{accountNumber}/reconciliation-adjustments endpoints (AMISP integration
 * requirement doc §7-8) — applying one really credits/debits the consumer's wallet, exactly like
 * a recharge, tagged distinctly in the ledger. */
@Injectable({ providedIn: 'root' })
export class ReconciliationService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<ReconciliationAdjustmentSummary[]> {
    return this.http.get<ReconciliationAdjustmentSummary[]>(`${this.baseUrl}/reconciliation-adjustments`);
  }

  apply(accountNumber: string, request: ApplyReconciliationAdjustmentRequest): Observable<ReconciliationAdjustmentSummary> {
    return this.http.post<ReconciliationAdjustmentSummary>(
      `${this.baseUrl}/consumers/${encodeURIComponent(accountNumber)}/reconciliation-adjustments`, request);
  }
}
