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
  ConsumerSummaryStats,
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
/** The Consumers list filters, as the search and export endpoints take them. `consumerNumber` matches account numbers only. */
export interface ConsumerFilters {
  consumerNumber?: string;
  meterNumber?: string;
  q?: string;
  status?: ConnectionStatus | null;
  /** Completed, Pending or Rejected: consumers with a conversion request in that state. */
  conversion?: string;
  from?: string;
  to?: string;
  zoneId?: string;
  circleId?: string;
  divisionId?: string;
  subDivisionId?: string;
}

function filterParams(p: ConsumerFilters): Record<string, string> {
  const query: Record<string, string> = {};
  if (p.status !== null && p.status !== undefined) query['status'] = ConnectionStatus[p.status];
  for (const k of ['consumerNumber', 'meterNumber', 'q', 'conversion', 'from', 'to', 'zoneId', 'circleId', 'divisionId', 'subDivisionId'] as const) {
    const v = p[k]?.trim();
    if (v) query[k] = v;
  }
  return query;
}

@Injectable({ providedIn: 'root' })
export class ConsumerService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/consumers`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<ConsumerSummary[]> {
    return this.http.get<ConsumerSummary[]>(this.baseUrl);
  }

  search(params: ConsumerFilters & { after?: string | null; pageSize?: number }): Observable<ConsumerSearchPage> {
    const query = filterParams(params);
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<ConsumerSearchPage>(`${this.baseUrl}/search`, { params: query });
  }

  /** GET /consumers/summary: consumer counts by conversion state. */
  summary(): Observable<ConsumerSummaryStats> {
    return this.http.get<ConsumerSummaryStats>(`${this.baseUrl}/summary`);
  }

  /** GET /consumers/export: the filtered list as an Excel file (the API refuses more than 100,000 rows). */
  exportExcel(params: ConsumerFilters): Observable<Blob> {
    const query = filterParams(params);
    query['tzOffsetMinutes'] = String(-new Date().getTimezoneOffset());
    return this.http.get(`${this.baseUrl}/export`, { params: query, responseType: 'blob' });
  }

  /** PUT /api/v1/consumers/{account}/mobile: the API normalises the number and answers 400 with a message if it is not valid. */
  updateMobile(accountNumber: string, mobileNumber: string): Observable<{ accountNumber: string; mobileNumber: string }> {
    return this.http.put<{ accountNumber: string; mobileNumber: string }>(`${this.baseUrl}/${encodeURIComponent(accountNumber)}/mobile`, { mobileNumber });
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
