import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ConnectivityCommandDetail,
  ConnectivityCommandSummary,
  ConnectivityCommandSummaryStats,
  ConnectivityCommandType,
  LiveRcDcRow,
  RetryConnectivityCommandResult,
} from '../models/connectivity-command.model';
import { Page } from '../../shared/utils/paged-list';

/** The list filters, as the search and export endpoints take them. `q` is a consumer (RR) number, not a name. */
export interface RcDcFilters {
  q?: string;
  meterNumber?: string;
  type?: ConnectivityCommandType | null;
  status?: string | null;
  from?: string;
  to?: string;
  zoneId?: string;
  circleId?: string;
  divisionId?: string;
  subDivisionId?: string;
}

function filterParams(p: RcDcFilters): Record<string, string> {
  const query: Record<string, string> = {};
  if (p.type !== null && p.type !== undefined) query['type'] = String(p.type);
  if (p.status) query['status'] = p.status;
  for (const k of ['q', 'meterNumber', 'from', 'to', 'zoneId', 'circleId', 'divisionId', 'subDivisionId'] as const) {
    const v = p[k]?.trim();
    if (v) query[k] = v;
  }
  return query;
}

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

  /** GET /connectivity-commands/search: newest first, keyset-paged. `status`: Queued, Sent, Pending (queued or sent), Acknowledged or FailedOrTimedOut. */
  search(params: RcDcFilters & { after?: string | null; pageSize?: number }): Observable<Page<ConnectivityCommandSummary>> {
    const query = filterParams(params);
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<Page<ConnectivityCommandSummary>>(`${this.baseUrl}/search`, { params: query });
  }

  /** GET /connectivity-commands/export: the filtered list as an Excel file. The API needs a From and To date within 30 days. */
  exportExcel(params: RcDcFilters): Observable<Blob> {
    const query = filterParams(params);
    query['tzOffsetMinutes'] = String(-new Date().getTimezoneOffset());
    return this.http.get(`${this.baseUrl}/export`, { params: query, responseType: 'blob' });
  }

  /** GET /reports/live-rc-dc: per day, DCs sent and the RCs and recharges against them. */
  liveStatus(params: { from: string; to: string; zoneId?: string; circleId?: string }): Observable<{ rows: LiveRcDcRow[]; generatedAt: string }> {
    const query: Record<string, string> = { from: params.from, to: params.to };
    if (params.zoneId) query['zoneId'] = params.zoneId;
    if (params.circleId) query['circleId'] = params.circleId;
    return this.http.get<{ rows: LiveRcDcRow[]; generatedAt: string }>(`${environment.apiBaseUrl}/api/v1/reports/live-rc-dc`, { params: query });
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
