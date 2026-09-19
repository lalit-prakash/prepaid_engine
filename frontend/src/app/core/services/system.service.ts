import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SystemHealth, SystemIntegrations } from '../models/system.model';

/** Talks to GET /api/v1/system/health and GET /api/v1/system/integrations. */
@Injectable({ providedIn: 'root' })
export class SystemService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1/system`;

  constructor(private readonly http: HttpClient) {}

  health(): Observable<SystemHealth> {
    return this.http.get<SystemHealth>(`${this.baseUrl}/health`);
  }

  integrations(): Observable<SystemIntegrations> {
    return this.http.get<SystemIntegrations>(`${this.baseUrl}/integrations`);
  }
}
