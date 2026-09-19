import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BillDetail, BillSearchPage, BillSummary, BillSummaryStats } from '../models/bill.model';
import { BillStatus } from '../models/consumer.model';

/** Talks to the real GET /api/v1/bills and GET /api/v1/bills/{id} endpoints. */
@Injectable({ providedIn: 'root' })
export class BillService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/bills`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<BillSummary[]> {
    return this.http.get<BillSummary[]>(this.baseUrl);
  }

  search(params: { q?: string; status?: BillStatus | null; from?: string; to?: string; after?: string | null; pageSize?: number }): Observable<BillSearchPage> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.status !== null && params.status !== undefined) query['status'] = BillStatus[params.status];
    if (params.from) query['from'] = params.from;
    if (params.to) query['to'] = params.to;
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<BillSearchPage>(`${this.baseUrl}/search`, { params: query });
  }

  summary(): Observable<BillSummaryStats> {
    return this.http.get<BillSummaryStats>(`${this.baseUrl}/summary`);
  }

  getById(id: string): Observable<BillDetail> {
    return this.http.get<BillDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}
