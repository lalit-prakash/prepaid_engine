import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { TariffDetail, TariffSummary } from '../models/tariff.model';

/** Talks to the real GET /api/v1/tariffs and GET /api/v1/tariffs/{id} endpoints (read-only —
 * there is no create/update endpoint yet, see Program.cs's comment on why). */
@Injectable({ providedIn: 'root' })
export class TariffService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/tariffs`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<TariffSummary[]> {
    return this.http.get<TariffSummary[]>(this.baseUrl);
  }

  getById(id: string): Observable<TariffDetail> {
    return this.http.get<TariffDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}
