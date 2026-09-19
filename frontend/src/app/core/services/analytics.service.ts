import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AnalyticsOverview } from '../models/analytics.model';

/** Talks to GET /api/v1/analytics/overview. Dates are inclusive yyyy-MM-dd; omit both for the last 30 days. */
@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/analytics`;

  constructor(private readonly http: HttpClient) {}

  overview(from?: string, to?: string): Observable<AnalyticsOverview> {
    const params: Record<string, string> = {};
    if (from) params['from'] = from;
    if (to) params['to'] = to;
    return this.http.get<AnalyticsOverview>(`${this.baseUrl}/overview`, { params });
  }
}
