import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DashboardSummary } from '../models/dashboard.model';

/** Talks to GET /api/v1/dashboard/summary. */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/dashboard`;

  constructor(private readonly http: HttpClient) {}

  summary(): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${this.baseUrl}/summary`);
  }
}
