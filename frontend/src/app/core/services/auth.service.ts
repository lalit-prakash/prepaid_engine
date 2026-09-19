import { HttpClient } from '@angular/common/http';
import { Injectable, OnDestroy, computed, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

const TOKEN_KEY = 'pe_token';
const EXPIRY_KEY = 'pe_token_exp';
const ROLE_KEY = 'pe_role';
const USER_KEY = 'pe_user';

export type UserRole = 'IT' | 'Utility' | 'Admin' | 'Operator' | 'ReadOnly';

interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  username: string;
  displayName: string;
  role: string;
}

/**
 * Holds the signed-in session for the API's JWT bearer auth (see backend/PrepaidEngine.Api/Auth and
 * docs/assumptions-and-security.md). The password is sent once to POST /auth/login and never kept: only the
 * short-lived access token, its expiry and the display name live in sessionStorage for this tab. The token is
 * renewed shortly before it expires (until the server's absolute session limit), and a 401 signs the user out.
 *
 * The role kept here is a UI convenience so screens can show the right actions; the security boundary is the
 * backend's authorization policies, which are enforced regardless of what the browser holds.
 */
@Injectable({ providedIn: 'root' })
export class AuthService implements OnDestroy {
  private readonly _isAuthenticated = signal(this.hasLiveToken());
  readonly isAuthenticated = this._isAuthenticated.asReadonly();

  private readonly _role = signal<UserRole | null>(sessionStorage.getItem(ROLE_KEY) as UserRole | null);
  readonly role = this._role.asReadonly();

  /** The name to show for the signed-in user (the account's display name). */
  private readonly _username = signal<string | null>(sessionStorage.getItem(USER_KEY));
  readonly username = this._username.asReadonly();

  /** Roles that may perform operational actions (recharge, disconnect/reconnect, retries, exceptions). Mirrors the API's "Operations" policy. */
  readonly canOperate = computed(() => ['Admin', 'IT', 'Operator'].includes(this._role() ?? ''));

  /** Roles that may load bulk data such as the network hierarchy. Mirrors the API's "DataAdmin" policy. */
  readonly canManageData = computed(() => ['Admin', 'IT'].includes(this._role() ?? ''));

  private renewTimer?: ReturnType<typeof setTimeout>;

  constructor(private readonly http: HttpClient) {
    // The previous Basic-auth build kept the encoded password here; make sure none survives.
    sessionStorage.removeItem('pe_auth');
    if (this._isAuthenticated()) this.scheduleRenewal();
  }

  get authHeaderValue(): string | null {
    return this.hasLiveToken() ? `Bearer ${sessionStorage.getItem(TOKEN_KEY)}` : null;
  }

  login(username: string, password: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${environment.apiBaseUrl}/api/v1/auth/login`, { username, password })
      .pipe(tap((res) => this.store(res)));
  }

  signOut(): void {
    clearTimeout(this.renewTimer);
    [TOKEN_KEY, EXPIRY_KEY, ROLE_KEY, USER_KEY].forEach((k) => sessionStorage.removeItem(k));
    this._username.set(null);
    this._role.set(null);
    this._isAuthenticated.set(false);
  }

  ngOnDestroy(): void {
    clearTimeout(this.renewTimer);
  }

  private hasLiveToken(): boolean {
    const token = sessionStorage.getItem(TOKEN_KEY);
    const exp = Number(sessionStorage.getItem(EXPIRY_KEY));
    return !!token && exp > Date.now();
  }

  private store(res: LoginResponse): void {
    sessionStorage.setItem(TOKEN_KEY, res.accessToken);
    sessionStorage.setItem(EXPIRY_KEY, String(new Date(res.expiresAtUtc).getTime()));
    sessionStorage.setItem(USER_KEY, res.displayName || res.username);
    const roles: string[] = ['IT', 'Utility', 'Admin', 'Operator', 'ReadOnly'];
    const role = roles.includes(res.role) ? (res.role as UserRole) : null;
    if (role) sessionStorage.setItem(ROLE_KEY, role);
    else sessionStorage.removeItem(ROLE_KEY);
    this._username.set(res.displayName || res.username);
    this._role.set(role);
    this._isAuthenticated.set(true);
    this.scheduleRenewal();
  }

  /** Swap the token for a fresh one shortly before it expires; if the server refuses, the session has ended. */
  private scheduleRenewal(): void {
    clearTimeout(this.renewTimer);
    const exp = Number(sessionStorage.getItem(EXPIRY_KEY));
    const delay = Math.max(exp - Date.now() - 60_000, 5_000);
    this.renewTimer = setTimeout(() => {
      this.http.post<LoginResponse>(`${environment.apiBaseUrl}/api/v1/auth/refresh`, {}).subscribe({
        next: (res) => this.store(res),
        error: () => this.signOut(),
      });
    }, delay);
  }
}
