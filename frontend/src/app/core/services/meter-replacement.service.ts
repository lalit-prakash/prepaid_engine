import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { MeterReplacementSummary } from '../models/meter-replacement.model';

/** Talks to the real GET /api/v1/meter-replacements endpoint — the audit trail that exists
 * specifically so an old meter's cumulative reading is never compared against a new meter's
 * (they're different physical meters). Read-only by design: a replacement is an audit record,
 * never edited from this page. */
@Injectable({ providedIn: 'root' })
export class MeterReplacementService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/meter-replacements`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<MeterReplacementSummary[]> {
    return this.http.get<MeterReplacementSummary[]>(this.baseUrl);
  }
}
