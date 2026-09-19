import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MeterCommandDetail, MeterCommandSummary, MeterCommandSummaryStats, RetryMeterCommandResult } from '../models/meter-command.model';
import { Page } from '../../shared/utils/paged-list';

/** Talks to the real GET /api/v1/meter-commands, GET /api/v1/meter-commands/{id}, and
 * POST /api/v1/meter-commands/{id}/retry endpoints. Retry is a genuine action — it resets the
 * command and dispatches it again through the same IMeterCommandClient the recharge flow uses,
 * never a fabricated status flip. */
@Injectable({ providedIn: 'root' })
export class MeterCommandService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/meter-commands`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<MeterCommandSummary[]> {
    return this.http.get<MeterCommandSummary[]>(this.baseUrl);
  }

  /** GET /meter-commands/search: newest first, keyset-paged. `status`: Acknowledged, Failed, TimedOut, Pending or Retried. */
  search(params: { q?: string; status?: string | null; after?: string | null; pageSize?: number }): Observable<Page<MeterCommandSummary>> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.status) query['status'] = params.status;
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<Page<MeterCommandSummary>>(`${this.baseUrl}/search`, { params: query });
  }

  summary(): Observable<MeterCommandSummaryStats> {
    return this.http.get<MeterCommandSummaryStats>(`${this.baseUrl}/summary`);
  }

  getById(id: string): Observable<MeterCommandDetail> {
    return this.http.get<MeterCommandDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  retry(id: string): Observable<RetryMeterCommandResult> {
    return this.http.post<RetryMeterCommandResult>(`${this.baseUrl}/${encodeURIComponent(id)}/retry`, {});
  }
}
