import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditEntrySummary, AuditSearchPage, AuditSummaryStats } from '../models/audit-entry.model';

/** Talks to the real GET /api/v1/audit-entries endpoint — an immutable, append-only log, so this
 * service is read-only by design. */
@Injectable({ providedIn: 'root' })
export class AuditEntryService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/audit-entries`;

  constructor(private readonly http: HttpClient) {}

  list(entityId?: string): Observable<AuditEntrySummary[]> {
    return this.http.get<AuditEntrySummary[]>(this.baseUrl, { params: entityId ? { entityId } : {} });
  }

  search(params: { q?: string; entityType?: string; actor?: string; from?: string; to?: string; after?: string | null; pageSize?: number }): Observable<AuditSearchPage> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.entityType) query['entityType'] = params.entityType;
    if (params.actor) query['actor'] = params.actor;
    if (params.from) query['from'] = params.from;
    if (params.to) query['to'] = params.to;
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<AuditSearchPage>(`${this.baseUrl}/search`, { params: query });
  }

  summary(): Observable<AuditSummaryStats> {
    return this.http.get<AuditSummaryStats>(`${this.baseUrl}/summary`);
  }
}
