import { HttpClient } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { AuthService } from '../../../../core/services/auth.service';
import { Icon } from '../../../../shared/components/icon/icon';

/**
 * Sign-in gate for the demo API's stop-gap HTTP Basic auth (see
 * docs/assumptions-and-security.md). Verifies the credential against a real
 * endpoint before caching it, exactly like the static wwwroot/index.html
 * console does, so a typo doesn't silently propagate into every subsequent
 * API call.
 */
@Component({
  selector: 'pe-login',
  imports: [FormsModule, Icon],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  protected username = 'demo';
  protected password = '';
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly checking = signal(false);
  protected readonly passwordVisible = signal(false);

  constructor(
    private readonly http: HttpClient,
    private readonly auth: AuthService,
    private readonly router: Router,
  ) {}

  togglePasswordVisibility(): void {
    this.passwordVisible.update((v) => !v);
  }

  submit(): void {
    if (!this.username || !this.password) {
      this.errorMessage.set('Enter both username and password.');
      return;
    }
    this.checking.set(true);
    this.errorMessage.set(null);

    const encoded = btoa(`${this.username}:${this.password}`);
    this.http
      .get(`${environment.apiBaseUrl}/api/v1/consumers`, {
        headers: { Authorization: `Basic ${encoded}` },
      })
      .subscribe({
        next: () => {
          this.auth.setCredentials(this.username, this.password);
          this.router.navigate(['/overview']);
        },
        error: (err) => {
          this.checking.set(false);
          this.errorMessage.set(
            err?.status === 401
              ? 'Invalid username or password.'
              : 'Could not reach the Prepaid Engine API — is it running?',
          );
        },
      });
  }
}
