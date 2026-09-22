import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BookCheck, FppasRate, TariffDetail, TariffLineage, TariffParameters, TariffSummary } from '../models/tariff.model';

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

  /** Every FPPAS rate notified so far (tariff book §A.4), newest billing month first — the daily run applies the most recent one. */
  fppasRates(): Observable<FppasRate[]> {
    return this.http.get<FppasRate[]>(`${environment.apiBaseUrl}/api/v1/tariff-parameters/fppas`);
  }

  /** Notifies the FPPAS rate for the billing month one month after `notifiedAt` (or now); renotifying within the same
   * billing month corrects it rather than adding a second rate for that month. Restricted to tariff governance roles. */
  notifyFppasRate(rateFraction: number, notifiedAt?: string): Observable<FppasRate> {
    return this.http.post<FppasRate>(`${environment.apiBaseUrl}/api/v1/tariff-parameters/fppas`, { rateFraction, notifiedAt });
  }

  getById(id: string): Observable<TariffDetail> {
    return this.http.get<TariffDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}
