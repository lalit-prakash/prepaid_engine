import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { OperationalExceptionSummary } from '../models/operational-exception.model';

/** Talks to the real GET /api/v1/exceptions and POST /api/v1/exceptions/{id}/resolve endpoints —
 * every exception here was auto-raised alongside a real Failed/TimedOut MeterCommand or
 * ConnectivityCommand, never hand-entered. Resolving is a genuine action requiring a note. */
@Injectable({ providedIn: 'root' })
export class OperationalExceptionService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/exceptions`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<OperationalExceptionSummary[]> {
    return this.http.get<OperationalExceptionSummary[]>(this.baseUrl);
  }

  resolve(id: string, note: string): Observable<OperationalExceptionSummary> {
    return this.http.post<OperationalExceptionSummary>(`${this.baseUrl}/${encodeURIComponent(id)}/resolve`, { note });
  }
}
