import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MeterAssignmentEventType, MeterReplacementSummary, MeterReplacementSummaryStats } from '../models/meter-replacement.model';
import { Page } from '../../shared/utils/paged-list';

/** Talks to the real GET /api/v1/meter-replacements endpoint — the audit trail that exists
 * specifically so an old meter's cumulative reading is never compared against a new meter's
 * (they're different physical meters). Read-only by design: a replacement is an audit record,
 * never edited from this page. */
@Injectable({ providedIn: 'root' })
export class MeterReplacementService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/meter-replacements`;

  constructor(private readonly http: HttpClient) {}

  /** GET /meter-replacements/search: newest first, keyset-paged. */
  search(params: { q?: string; eventType?: MeterAssignmentEventType | null; after?: string | null; pageSize?: number }): Observable<Page<MeterReplacementSummary>> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.eventType !== null && params.eventType !== undefined) query['eventType'] = String(params.eventType);
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<Page<MeterReplacementSummary>>(`${this.baseUrl}/search`, { params: query });
  }

  summary(): Observable<MeterReplacementSummaryStats> {
    return this.http.get<MeterReplacementSummaryStats>(`${this.baseUrl}/summary`);
  }

  list(): Observable<MeterReplacementSummary[]> {
    return this.http.get<MeterReplacementSummary[]>(this.baseUrl);
  }
}
