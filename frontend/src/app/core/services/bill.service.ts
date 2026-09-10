import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { BillDetail, BillSummary } from '../models/bill.model';

/** Talks to the real GET /api/v1/bills and GET /api/v1/bills/{id} endpoints. */
@Injectable({ providedIn: 'root' })
export class BillService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/bills`;

  constructor(private readonly http: HttpClient) {}

  list(): Observable<BillSummary[]> {
    return this.http.get<BillSummary[]>(this.baseUrl);
  }

  getById(id: string): Observable<BillDetail> {
    return this.http.get<BillDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }
}
