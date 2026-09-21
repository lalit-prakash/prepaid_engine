import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CreateUserPayload, ManagedUser, RolesAndPermissions, UpdateUserPayload, UserList } from '../models/user.model';

/** Talks to the User Management endpoints (Admin and IT only). */
@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/v1`;

  constructor(private readonly http: HttpClient) {}

  list(filters: { q?: string; role?: string; status?: string }): Observable<UserList> {
    const params: Record<string, string> = {};
    if (filters.q?.trim()) params['q'] = filters.q.trim();
    if (filters.role) params['role'] = filters.role;
    if (filters.status) params['status'] = filters.status;
    return this.http.get<UserList>(`${this.baseUrl}/users`, { params });
  }

  create(payload: CreateUserPayload): Observable<ManagedUser> {
    return this.http.post<ManagedUser>(`${this.baseUrl}/users`, payload);
  }

  update(id: string, payload: UpdateUserPayload): Observable<ManagedUser> {
    return this.http.put<ManagedUser>(`${this.baseUrl}/users/${encodeURIComponent(id)}`, payload);
  }

  setPassword(id: string, password: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.baseUrl}/users/${encodeURIComponent(id)}/password`, { password });
  }

  unlock(loginId: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.baseUrl}/users/${encodeURIComponent(loginId)}/unlock`, {});
  }

  roles(): Observable<RolesAndPermissions> {
    return this.http.get<RolesAndPermissions>(`${this.baseUrl}/roles`);
  }
}
