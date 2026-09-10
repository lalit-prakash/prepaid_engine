import { Injectable, signal } from '@angular/core';

const SESSION_KEY = 'pe_auth';

/**
 * Holds the HTTP Basic credential for the demo API's stop-gap auth scheme
 * (see backend/PrepaidEngine.Api/Auth/BasicAuthenticationHandler.cs and
 * docs/assumptions-and-security.md — this is not a real auth system, and this
 * service must not become one). The encoded credential lives only in
 * sessionStorage for this tab; it is never persisted, logged, or sent
 * anywhere except as the Authorization header on calls to this app's own API.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _isAuthenticated = signal(!!sessionStorage.getItem(SESSION_KEY));
  readonly isAuthenticated = this._isAuthenticated.asReadonly();

  get authHeaderValue(): string | null {
    const encoded = sessionStorage.getItem(SESSION_KEY);
    return encoded ? `Basic ${encoded}` : null;
  }

  setCredentials(username: string, password: string): void {
    const encoded = btoa(`${username}:${password}`);
    sessionStorage.setItem(SESSION_KEY, encoded);
    this._isAuthenticated.set(true);
  }

  signOut(): void {
    sessionStorage.removeItem(SESSION_KEY);
    this._isAuthenticated.set(false);
  }
}
