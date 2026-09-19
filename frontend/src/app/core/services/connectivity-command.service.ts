import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ConnectivityCommandDetail,
  ConnectivityCommandSummary,
  RetryConnectivityCommandResult,
} from '../models/connectivity-command.model';

/** Talks to the real GET /api/v1/connectivity-commands, GET /api/v1/connectivity-commands/{id},
 * and POST /api/v1/connectivity-commands/{id}/retry endpoints. Retry is a genuine action — it
 * resets the command and dispatches it again through the same IConnectivityCommandClient the
 * original disconnect/reconnect used, never a fabricated status flip. */
@Injectable({ providedIn: 'root' })
export class ConnectivityCommandService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/connectivity-commands`;

  constructor(private readonly http: HttpClient) {}

  list(accountNumber?: string): Observable<ConnectivityCommandSummary[]> {
    return this.http.get<ConnectivityCommandSummary[]>(this.baseUrl, { params: accountNumber ? { accountNumber } : {} });
  }

  getById(id: string): Observable<ConnectivityCommandDetail> {
    return this.http.get<ConnectivityCommandDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  retry(id: string): Observable<RetryConnectivityCommandResult> {
    return this.http.post<RetryConnectivityCommandResult>(`${this.baseUrl}/${encodeURIComponent(id)}/retry`, {});
  }
}
