import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ConnectionStatus,
  ConnectivityRequest,
  ConnectivityResult,
  ConsumerDetail,
  ConsumerSearchPage,
  ConsumerSummary,
  RechargeRequest,
  RechargeResult,
} from '../models/consumer.model';

/**
 * Talks to the real Prepaid Engine API (GET /api/v1/consumers,
 * GET /api/v1/consumers/{accountNumber}, POST .../recharge). No mock data,
 * no client-side calculation — this service is a thin pass-through, and the
 * backend remains the sole source of billing/wallet truth (see
 * core/models/consumer.model.ts's Wallet doc comment).
 */
@Injectable({ providedIn: 'root' })
export class ConsumerService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/consumers`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<ConsumerSummary[]> {
    return this.http.get<ConsumerSummary[]>(this.baseUrl);
  }

  search(params: { q?: string; status?: ConnectionStatus | null; lowBalance?: boolean; after?: string | null; pageSize?: number }): Observable<ConsumerSearchPage> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.status !== null && params.status !== undefined) query['status'] = ConnectionStatus[params.status];
    if (params.lowBalance) query['lowBalance'] = 'true';
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<ConsumerSearchPage>(`${this.baseUrl}/search`, { params: query });
  }

  getByAccountNumber(accountNumber: string): Observable<ConsumerDetail> {
    return this.http.get<ConsumerDetail>(`${this.baseUrl}/${encodeURIComponent(accountNumber)}`);
  }

  /**
   * Observes the full HTTP response (not just the body) because the API uses
   * the status code itself to distinguish outcomes — 200 success, 202
   * pending, 402 declined, 503 unavailable, 400 invalid — and 2xx statuses
   * like 202 are delivered via the success channel, not HttpErrorResponse.
   */
  recharge(accountNumber: string, request: RechargeRequest): Observable<HttpResponse<RechargeResult>> {
    return this.http.post<RechargeResult>(
      `${this.baseUrl}/${encodeURIComponent(accountNumber)}/recharge`,
      request,
      { observe: 'response' },
    );
  }

  /** Real disconnect — requires a reason, dispatches a ConnectivityCommand through the meter-
   * command layer, and only advances the consumer's status on an actual acknowledgement. */
  disconnect(accountNumber: string, request: ConnectivityRequest): Observable<ConnectivityResult> {
    return this.http.post<ConnectivityResult>(
      `${this.baseUrl}/${encodeURIComponent(accountNumber)}/disconnect`,
      request,
    );
  }

  /** Real reconnect — same real dispatch/acknowledgement discipline as disconnect(). */
  reconnect(accountNumber: string, request: ConnectivityRequest): Observable<ConnectivityResult> {
    return this.http.post<ConnectivityResult>(
      `${this.baseUrl}/${encodeURIComponent(accountNumber)}/reconnect`,
      request,
    );
  }
}
