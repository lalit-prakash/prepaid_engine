import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { Icon } from '../../../../shared/components/icon/icon';

/**
 * Sign-in page. Sends the login id and password once to POST /auth/login and keeps only the returned
 * short-lived token (see AuthService and docs/assumptions-and-security.md).
 */
@Component({
  selector: 'pe-login',
  imports: [FormsModule, Icon],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  protected username = '';
  protected password = '';
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly checking = signal(false);
  protected readonly passwordVisible = signal(false);

  constructor(
    private readonly auth: AuthService,
    private readonly router: Router,
  ) {}

  togglePasswordVisibility(): void {
    this.passwordVisible.update((v) => !v);
  }

  submit(): void {
    if (!this.username || !this.password) {
      this.errorMessage.set('Enter both your login id and password.');
      return;
    }
    this.checking.set(true);
    this.errorMessage.set(null);

    this.auth.login(this.username, this.password).subscribe({
        next: () => this.router.navigate(['/overview']),
        error: (err) => {
          this.checking.set(false);
          this.errorMessage.set(
            err?.status === 401
              ? 'Invalid login id or password.'
              : err?.status === 429
              ? 'Too many failed attempts. Try again in a few minutes.'
              : 'Could not reach the Prepaid Engine API — is it running?',
          );
        },
      });
  }
}
