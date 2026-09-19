import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { NotificationEventType, NotificationStatus, NotificationSummary, NotificationSummaryStats } from '../models/notification.model';
import { Page } from '../../shared/utils/paged-list';

/** Talks to the real GET /api/v1/notifications endpoint — every notification here was queued
 * automatically during daily DLP billing, conversion, recharge, and reconciliation processing
 * (low balance, emergency credit, disconnection eligibility, provisional billing, prepaid
 * conversion completed, auto-disconnect/auto-reconnect), never hand-entered. This project has no
 * real SMS gateway, so a notification's Status reflects whether a real dispatcher would pick it
 * up next, never that an SMS actually left this system. Read-only by design. */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/notifications`;

  constructor(private readonly http: HttpClient) {}

  /** GET /notifications/search: newest first, keyset-paged. */
  search(params: { q?: string; eventType?: NotificationEventType | null; status?: NotificationStatus | null; after?: string | null; pageSize?: number }): Observable<Page<NotificationSummary>> {
    const query: Record<string, string> = {};
    if (params.q?.trim()) query['q'] = params.q.trim();
    if (params.eventType !== null && params.eventType !== undefined) query['eventType'] = String(params.eventType);
    if (params.status !== null && params.status !== undefined) query['status'] = String(params.status);
    if (params.after) query['after'] = params.after;
    if (params.pageSize) query['pageSize'] = String(params.pageSize);
    return this.http.get<Page<NotificationSummary>>(`${this.baseUrl}/search`, { params: query });
  }

  summary(): Observable<NotificationSummaryStats> {
    return this.http.get<NotificationSummaryStats>(`${this.baseUrl}/summary`);
  }

  list(): Observable<NotificationSummary[]> {
    return this.http.get<NotificationSummary[]>(this.baseUrl);
  }
}
