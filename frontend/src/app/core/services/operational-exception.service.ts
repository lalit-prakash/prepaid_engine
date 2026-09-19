import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { OperationalExceptionStatus, OperationalExceptionSummary, OperationalExceptionSummaryStats } from '../models/operational-exception.model';
import { Page } from '../../shared/utils/paged-list';

/** Talks to the real GET /api/v1/exceptions and POST /api/v1/exceptions/{id}/resolve endpoints —
 * every exception here was auto-raised alongside a real Failed/TimedOut MeterCommand or
 * ConnectivityCommand, never hand-entered. Resolving is a genuine action requiring a note. */
@Injectable({ providedIn: 'root' })
export class OperationalExceptionService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/exceptions`;

  constructor(private readonly http: HttpClient) {}

  /** GET /exceptions/search: newest first, keyset-paged. */
  search(params: { q?: string; status?: OperationalExceptionStatus | null; after?: string | null; pageSize?: number }): Observable<Page<OperationalExceptionSummary>> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.status !== null && params.status !== undefined) query['status'] = String(params.status);
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<Page<OperationalExceptionSummary>>(`${this.baseUrl}/search`, { params: query });
  }

  summary(): Observable<OperationalExceptionSummaryStats> {
    return this.http.get<OperationalExceptionSummaryStats>(`${this.baseUrl}/summary`);
  }

  list(): Observable<OperationalExceptionSummary[]> {
    return this.http.get<OperationalExceptionSummary[]>(this.baseUrl);
  }

  resolve(id: string, note: string): Observable<OperationalExceptionSummary> {
    return this.http.post<OperationalExceptionSummary>(`${this.baseUrl}/${encodeURIComponent(id)}/resolve`, { note });
  }
}
