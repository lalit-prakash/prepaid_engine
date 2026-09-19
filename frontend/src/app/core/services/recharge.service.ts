import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RechargeDetail, RechargeSearchPage, RechargeStatus, RechargeSummary, RechargeSummaryStats } from '../models/recharge.model';

/** Talks to the real GET /api/v1/recharges and GET /api/v1/recharges/{id} endpoints. */
@Injectable({ providedIn: 'root' })
export class RechargeService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/recharges`;

  constructor(private readonly http: HttpClient) {}

  list(accountNumber?: string): Observable<RechargeSummary[]> {
    return this.http.get<RechargeSummary[]>(this.baseUrl, { params: accountNumber ? { accountNumber } : {} });
  }

  search(params: { q?: string; paymentStatus?: RechargeStatus | null; meterCredit?: string | null; after?: string | null; pageSize?: number }): Observable<RechargeSearchPage> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.paymentStatus !== null && params.paymentStatus !== undefined) query['paymentStatus'] = RechargeStatus[params.paymentStatus];
    if (params.meterCredit) query['meterCredit'] = params.meterCredit;
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<RechargeSearchPage>(`${this.baseUrl}/search`, { params: query });
  }

  summary(): Observable<RechargeSummaryStats> {
    return this.http.get<RechargeSummaryStats>(`${this.baseUrl}/summary`);
  }

  getById(id: string): Observable<RechargeDetail> {
    return this.http.get<RechargeDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}
