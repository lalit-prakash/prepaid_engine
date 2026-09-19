import { Injectable, signal } from '@angular/core';

const SESSION_KEY = 'pe_auth';
const ROLE_KEY = 'pe_role';
const USER_KEY = 'pe_user';

export type UserRole = 'IT' | 'Utility';

/**
 * Holds the HTTP Basic credential for the demo API's stop-gap auth scheme
 * (see backend/PrepaidEngine.Api/Auth/BasicAuthenticationHandler.cs and
 * docs/assumptions-and-security.md — this is not a real auth system, and this
 * service must not become one). The encoded credential lives only in
 * sessionStorage for this tab; it is never persisted, logged, or sent
 * anywhere except as the Authorization header on calls to this app's own API.
 *
 * The role is a UI convenience only (fetched from GET /api/v1/auth/whoami after
 * login) so the tariff-governance screens can show the right actions — it is
 * never the actual security boundary, which lives entirely in the backend's
 * RequireAuthorization("ITRole"/"UtilityRole") policies.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _isAuthenticated = signal(!!sessionStorage.getItem(SESSION_KEY));
  readonly isAuthenticated = this._isAuthenticated.asReadonly();

  private readonly _role = signal<UserRole | null>(sessionStorage.getItem(ROLE_KEY) as UserRole | null);
  readonly role = this._role.asReadonly();

  // Sessions that signed in before the username was stored separately still carry it inside the encoded credential.
  private readonly _username = signal<string | null>(sessionStorage.getItem(USER_KEY) ?? AuthService.usernameFromCredential());
  readonly username = this._username.asReadonly();

  private static usernameFromCredential(): string | null {
    const encoded = sessionStorage.getItem(SESSION_KEY);
    if (!encoded) return null;
    try {
      const name = atob(encoded).split(':')[0];
      return name || null;
    } catch {
      return null;
    }
  }

  get authHeaderValue(): string | null {
    const encoded = sessionStorage.getItem(SESSION_KEY);
    return encoded ? `Basic ${encoded}` : null;
  }

  setCredentials(username: string, password: string): void {
    const encoded = btoa(`${username}:${password}`);
    sessionStorage.setItem(SESSION_KEY, encoded);
    sessionStorage.setItem(USER_KEY, username);
    this._username.set(username);
    this._isAuthenticated.set(true);
  }

  setRole(role: UserRole | null): void {
    if (role) {
      sessionStorage.setItem(ROLE_KEY, role);
    } else {
      sessionStorage.removeItem(ROLE_KEY);
    }
    this._role.set(role);
  }

  signOut(): void {
    sessionStorage.removeItem(SESSION_KEY);
    sessionStorage.removeItem(ROLE_KEY);
    sessionStorage.removeItem(USER_KEY);
    this._username.set(null);
    this._isAuthenticated.set(false);
    this._role.set(null);
  }
}
