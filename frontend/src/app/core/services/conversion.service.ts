import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ConversionSummary, ConversionSummaryStats } from '../models/conversion.model';
import { Page } from '../../shared/utils/paged-list';

/** Talks to the real GET /api/v1/conversions endpoint (AMISP integration requirement doc §1) —
 * batch submission happens on the RMS side, not from this UI, so there is no create action here,
 * only the read-only decision trail. */
@Injectable({ providedIn: 'root' })
export class ConversionService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/conversions`;

  constructor(private readonly http: HttpClient) {}

  /** GET /conversions/search: newest first, keyset-paged. `status`: Completed, Rejected or Pending. */
  search(params: { q?: string; status?: string | null; after?: string | null; pageSize?: number }): Observable<Page<ConversionSummary>> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.status) query['status'] = params.status;
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<Page<ConversionSummary>>(`${this.baseUrl}/search`, { params: query });
  }

  summary(): Observable<ConversionSummaryStats> {
    return this.http.get<ConversionSummaryStats>(`${this.baseUrl}/summary`);
  }

  list(): Observable<ConversionSummary[]> {
    return this.http.get<ConversionSummary[]>(this.baseUrl);
  }
}
