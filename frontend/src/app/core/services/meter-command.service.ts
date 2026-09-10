import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MeterCommandDetail, MeterCommandSummary, RetryMeterCommandResult } from '../models/meter-command.model';

/** Talks to the real GET /api/v1/meter-commands, GET /api/v1/meter-commands/{id}, and
 * POST /api/v1/meter-commands/{id}/retry endpoints. Retry is a genuine action — it resets the
 * command and dispatches it again through the same IMeterCommandClient the recharge flow uses,
 * never a fabricated status flip. */
@Injectable({ providedIn: 'root' })
export class MeterCommandService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/meter-commands`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<MeterCommandSummary[]> {
    return this.http.get<MeterCommandSummary[]>(this.baseUrl);
  }

  getById(id: string): Observable<MeterCommandDetail> {
    return this.http.get<MeterCommandDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  retry(id: string): Observable<RetryMeterCommandResult> {
    return this.http.post<RetryMeterCommandResult>(`${this.baseUrl}/${encodeURIComponent(id)}/retry`, {});
  }
}
