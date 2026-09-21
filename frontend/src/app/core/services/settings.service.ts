import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

/** One editable setting from GET /api/v1/settings. `value` is what the API uses now; `defaultValue` is what it uses when nothing has been saved. */
export interface SettingItem {
  key: string;
  label: string;
  description: string;
  unit: string;
  type: 'Integer' | 'Decimal';
  min: number;
  max: number;
  allowBlank: boolean;
  value: string | null;
  defaultValue: string | null;
  isCustom: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
}

export interface SettingsResponse {
  groups: { name: string; settings: SettingItem[] }[];
  readOnly: { label: string; value: string }[];
  note: string;
}

/** Talks to the System Settings endpoints (Admin and IT only). */
@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly url = `${environment.apiBaseUrl}/api/v1/settings`;

  constructor(private readonly http: HttpClient) {}

  get(): Observable<SettingsResponse> {
    return this.http.get<SettingsResponse>(this.url);
  }

  /** Saves the given values; a blank value puts that setting back to its default. */
  save(values: Record<string, string>): Observable<{ changed: number; message: string }> {
    return this.http.put<{ changed: number; message: string }>(this.url, { values });
  }
}
