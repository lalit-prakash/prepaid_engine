import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AnalyticsOverview, MeterCommunication, WalletDistribution } from '../models/analytics.model';

/** Talks to GET /api/v1/analytics/overview. Dates are inclusive yyyy-MM-dd; omit both for the last 30 days. */
@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/analytics`;

  constructor(private readonly http: HttpClient) {}

  /** GET /analytics/meter-communication: installed meters by when they were last heard from. */
  meterCommunication(): Observable<MeterCommunication> {
    return this.http.get<MeterCommunication>(`${this.baseUrl}/meter-communication`);
  }

  /** GET /analytics/wallet-distribution: consumers by wallet balance band. */
  walletDistribution(): Observable<WalletDistribution> {
    return this.http.get<WalletDistribution>(`${this.baseUrl}/wallet-distribution`);
  }

  overview(from?: string, to?: string): Observable<AnalyticsOverview> {
    const params: Record<string, string> = {};
    if (from) params['from'] = from;
    if (to) params['to'] = to;
    return this.http.get<AnalyticsOverview>(`${this.baseUrl}/overview`, { params });
  }
}
