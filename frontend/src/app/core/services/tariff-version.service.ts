import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { TariffVersionSummary } from '../models/tariff-version.model';

/** Talks to the real GET /api/v1/tariffs/{id}/versions endpoint — a tariff's recorded parameter
 * changes, enabling a future Tariff Change Report even though `Tariff` itself has no update
 * endpoint yet (a version is recorded independently of any actual tariff mutation). */
@Injectable({ providedIn: 'root' })
export class TariffVersionService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/tariffs`;

  constructor(private readonly http: HttpClient) {}

  list(tariffId: string): Observable<TariffVersionSummary[]> {
    return this.http.get<TariffVersionSummary[]>(`${this.baseUrl}/${encodeURIComponent(tariffId)}/versions`);
  }
}
