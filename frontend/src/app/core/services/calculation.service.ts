import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SimulateChargeRequest, SimulateChargeResult } from '../models/calculation.model';

/** Talks to the real POST /api/v1/calculation-workbench/simulate endpoint — the frontend
 * never computes charges itself, only renders what the backend's domain methods return. */
@Injectable({ providedIn: 'root' })
export class CalculationService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/calculation-workbench`;

  constructor(private readonly http: HttpClient) {}

  simulate(request: SimulateChargeRequest): Observable<SimulateChargeResult> {
    return this.http.post<SimulateChargeResult>(`${this.baseUrl}/simulate`, request);
  }
}
