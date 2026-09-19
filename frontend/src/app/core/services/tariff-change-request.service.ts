import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CreateTariffChangeRequestBody,
  TariffChangeRequestDetail,
  TariffChangeRequestStatus,
  TariffChangeRequestSummary,
  UpdateTariffChangeRequestBody,
} from '../models/tariff-change-request.model';

/** Talks to the tariff-governance workflow endpoints (create/draft/submit/approve/reject) —
 * see backend/PrepaidEngine.Domain/Entities/TariffChangeRequest.cs for the state machine this
 * mirrors. Every mutating call is backend-role-gated (ITRole/UtilityRole); this service does not
 * itself enforce anything — it just surfaces whatever the API allows or rejects. */
@Injectable({ providedIn: 'root' })
export class TariffChangeRequestService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/tariff-change-requests`;

  constructor(private readonly http: HttpClient) {}

  list(status?: TariffChangeRequestStatus): Observable<TariffChangeRequestSummary[]> {
    const params = status !== undefined ? { params: { status: TariffChangeRequestStatus[status] } } : {};
    return this.http.get<TariffChangeRequestSummary[]>(this.baseUrl, params);
  }

  getById(id: string): Observable<TariffChangeRequestDetail> {
    return this.http.get<TariffChangeRequestDetail>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  create(body: CreateTariffChangeRequestBody): Observable<{ id: string; status: TariffChangeRequestStatus }> {
    return this.http.post<{ id: string; status: TariffChangeRequestStatus }>(this.baseUrl, body);
  }

  saveDraft(id: string, body: UpdateTariffChangeRequestBody): Observable<{ id: string; status: TariffChangeRequestStatus }> {
    return this.http.put<{ id: string; status: TariffChangeRequestStatus }>(`${this.baseUrl}/${encodeURIComponent(id)}/draft`, body);
  }

  submit(id: string, changeReason: string): Observable<{ id: string; status: TariffChangeRequestStatus }> {
    return this.http.post<{ id: string; status: TariffChangeRequestStatus }>(`${this.baseUrl}/${encodeURIComponent(id)}/submit`, {
      changeReason,
    });
  }

  approve(id: string, commencementDate: string): Observable<{ id: string; status: TariffChangeRequestStatus; commencementDate: string }> {
    return this.http.post<{ id: string; status: TariffChangeRequestStatus; commencementDate: string }>(
      `${this.baseUrl}/${encodeURIComponent(id)}/approve`,
      { commencementDate },
    );
  }

  cancel(id: string, reason: string): Observable<{ id: string; status: TariffChangeRequestStatus }> {
    return this.http.post<{ id: string; status: TariffChangeRequestStatus }>(`${this.baseUrl}/${encodeURIComponent(id)}/cancel`, { reason });
  }

  reject(id: string, rejectionReason: string): Observable<{ id: string; status: TariffChangeRequestStatus }> {
    return this.http.post<{ id: string; status: TariffChangeRequestStatus }>(`${this.baseUrl}/${encodeURIComponent(id)}/reject`, {
      rejectionReason,
    });
  }
}
