import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RiskIndicatorsSummary, SlaMetric } from '../models/sla.model';

/** Talks to the real GET /api/v1/sla and GET /api/v1/risk-indicators endpoints — see each
 * response model's own doc comment for why neither ever fabricates a number. */
@Injectable({ providedIn: 'root' })
export class SlaService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1`;

  constructor(private readonly http: HttpClient) {}

  getSlaSummary(): Observable<SlaMetric[]> {
    return this.http.get<SlaMetric[]>(`${this.baseUrl}/sla`);
  }

  getRiskIndicators(): Observable<RiskIndicatorsSummary> {
    return this.http.get<RiskIndicatorsSummary>(`${this.baseUrl}/risk-indicators`);
  }
}
