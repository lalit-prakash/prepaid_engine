import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ConnectivityCommandDetail,
  ConnectivityCommandSummary,
  ConnectivityCommandSummaryStats,
  ConnectivityCommandType,
  RetryConnectivityCommandResult,
} from '../models/connectivity-command.model';
import { Page } from '../../shared/utils/paged-list';

/** Talks to the real GET /api/v1/connectivity-commands, GET /api/v1/connectivity-commands/{id},
 * and POST /api/v1/connectivity-commands/{id}/retry endpoints. Retry is a genuine action — it
 * resets the command and dispatches it again through the same IConnectivityCommandClient the
 * original disconnect/reconnect used, never a fabricated status flip. */
@Injectable({ providedIn: 'root' })
export class ConnectivityCommandService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/connectivity-commands`;

  constructor(private readonly http: HttpClient) {}

  list(accountNumber?: string): Observable<ConnectivityCommandSummary[]> {
    return this.http.get<ConnectivityCommandSummary[]>(this.baseUrl, { params: accountNumber ? { accountNumber } : {} });
  }

  /** GET /connectivity-commands/search: newest first, keyset-paged. `status`: Acknowledged, FailedOrTimedOut or Pending. */
  search(params: { q?: string; type?: ConnectivityCommandType | null; status?: string | null; after?: string | null; pageSize?: number }): Observable<Page<ConnectivityCommandSummary>> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.type !== null && params.type !== undefined) query['type'] = String(params.type);
    if (params.status) query['status'] = params.status;
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<Page<ConnectivityCommandSummary>>(`${this.baseUrl}/search`, { params: query });
  }

  summary(): Observable<ConnectivityCommandSummaryStats> {
    return this.http.get<ConnectivityCommandSummaryStats>(`${this.baseUrl}/summary`);
  }

  getById(id: string): Observable<ConnectivityCommandDetail> {
    return this.http.get<ConnectivityCommandDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  retry(id: string): Observable<RetryConnectivityCommandResult> {
    return this.http.post<RetryConnectivityCommandResult>(`${this.baseUrl}/${encodeURIComponent(id)}/retry`, {});
  }
}
