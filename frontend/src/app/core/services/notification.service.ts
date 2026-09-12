import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { NotificationSummary } from '../models/notification.model';

/** Talks to the real GET /api/v1/notifications endpoint — every notification here was queued
 * automatically during hourly/daily LS/DLP billing processing (low balance, emergency credit,
 * disconnection eligibility, provisional billing), never hand-entered. This project has no real
 * SMS gateway, so a notification's Status reflects whether a real dispatcher would pick it up
 * next, never that an SMS actually left this system. Read-only by design. */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/notifications`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<NotificationSummary[]> {
    return this.http.get<NotificationSummary[]>(this.baseUrl);
  }
}
