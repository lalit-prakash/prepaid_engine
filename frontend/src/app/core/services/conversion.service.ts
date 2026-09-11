import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ConversionSummary } from '../models/conversion.model';

/** Talks to the real GET /api/v1/conversions endpoint (AMISP integration requirement doc §1) —
 * batch submission happens on the RMS side, not from this UI, so there is no create action here,
 * only the read-only decision trail. */
@Injectable({ providedIn: 'root' })
export class ConversionService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/conversions`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<ConversionSummary[]> {
    return this.http.get<ConversionSummary[]>(this.baseUrl);
  }
}
