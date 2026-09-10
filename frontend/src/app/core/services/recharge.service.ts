import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RechargeDetail, RechargeSummary } from '../models/recharge.model';

/** Talks to the real GET /api/v1/recharges and GET /api/v1/recharges/{id} endpoints. */
@Injectable({ providedIn: 'root' })
export class RechargeService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/recharges`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<RechargeSummary[]> {
    return this.http.get<RechargeSummary[]>(this.baseUrl);
  }

  getById(id: string): Observable<RechargeDetail> {
    return this.http.get<RechargeDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}
