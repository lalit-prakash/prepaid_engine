import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BookCheck, TariffDetail, TariffLineage, TariffParameters, TariffSummary } from '../models/tariff.model';

/** Talks to the real GET /api/v1/tariffs and GET /api/v1/tariffs/{id} endpoints (read-only —
 * there is no create/update endpoint yet, see Program.cs's comment on why). */
@Injectable({ providedIn: 'root' })
export class TariffService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/tariffs`;

  constructor(private readonly http: HttpClient) {}

  list(status?: 'Active' | 'Retired'): Observable<TariffSummary[]> {
    return this.http.get<TariffSummary[]>(this.baseUrl, { params: status ? { status } : {} });
  }

  /** Each tariff book schedule against the tariff in force for it (read-only). */
  bookCheck(): Observable<BookCheck> {
    return this.http.get<BookCheck>(`${this.baseUrl}/book-check`);
  }

  /** The tariff book's fixed figures (prepaid facilities, duty, maintenance, reconnection, surcharge). */
  parameters(): Observable<TariffParameters> {
    return this.http.get<TariffParameters>(`${environment.apiBaseUrl}/api/v1/tariff-parameters`);
  }

  lineage(id: string): Observable<TariffLineage> {
    return this.http.get<TariffLineage>(`${this.baseUrl}/${encodeURIComponent(id)}/lineage`);
  }

  getById(id: string): Observable<TariffDetail> {
    return this.http.get<TariffDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}
