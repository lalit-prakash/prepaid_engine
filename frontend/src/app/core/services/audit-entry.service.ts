import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuditEntrySummary } from '../models/audit-entry.model';

/** Talks to the real GET /api/v1/audit-entries endpoint — an immutable, append-only log, so this
 * service is read-only by design. */
@Injectable({ providedIn: 'root' })
export class AuditEntryService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/audit-entries`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<AuditEntrySummary[]> {
    return this.http.get<AuditEntrySummary[]>(this.baseUrl);
  }
}
