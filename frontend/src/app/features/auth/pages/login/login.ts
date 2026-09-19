import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../../../core/services/auth.service';
import { Icon } from '../../../../shared/components/icon/icon';

type Mode = 'login' | 'forgot' | 'reset';

/**
 * Sign-in page. Sends the login id and password once to POST /auth/login and keeps only the returned
 * short-lived token (see AuthService and docs/assumptions-and-security.md).
 *
 * "Forgot password?" is a two-step flow: the login id is sent to POST /auth/forgot-password, which e-mails a one-time
 * code to the address registered for that user; the code and a new password then go to POST /auth/reset-password.
 * The page never learns whether a login id exists, so it always says the code was sent "if" the id has an address.
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
  protected code = '';
  protected newPassword = '';
  protected confirmPassword = '';
  protected readonly mode = signal<Mode>('login');
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly problems = signal<string[]>([]);
  protected readonly infoMessage = signal<string | null>(null);
  protected readonly checking = signal(false);
  protected readonly passwordVisible = signal(false);

  constructor(
    private readonly auth: AuthService,
    private readonly router: Router,
  ) {}

  togglePasswordVisibility(): void {
    this.passwordVisible.update((v) => !v);
  }

  show(mode: Mode): void {
    this.mode.set(mode);
    this.errorMessage.set(null);
    this.problems.set([]);
    if (mode !== 'reset') this.infoMessage.set(null);
    this.password = this.code = this.newPassword = this.confirmPassword = '';
  }

  submit(): void {
    const username = this.username.trim();
    if (!username || !this.password) {
      this.errorMessage.set('Enter both your login id and password.');
      return;
    }
    this.checking.set(true);
    this.errorMessage.set(null);
    this.infoMessage.set(null);

    this.auth.login(username, this.password).subscribe({
      next: () => this.router.navigate(['/overview']),
      error: (err) => {
        this.checking.set(false);
        this.errorMessage.set(
          err?.status === 401
            ? 'Invalid login id or password.'
            : err?.status === 429
              ? this.lockedMessage(err?.error?.retryAfterSeconds)
              : 'Could not reach the Prepaid Engine API — is it running?',
        );
      },
    });
  }

  /** Step 1: e-mail a code. Also used by "Send a new code". */
  sendCode(): void {
    const username = this.username.trim();
    if (!username) {
      this.errorMessage.set('Enter your login id.');
      return;
    }
    this.checking.set(true);
    this.errorMessage.set(null);
    this.auth.requestPasswordReset(username).subscribe({
      next: (res) => {
        this.checking.set(false);
        this.mode.set('reset');
        this.infoMessage.set(res.message);
      },
      error: (err) => {
        this.checking.set(false);
        this.errorMessage.set(
          err?.error?.error ??
            (err?.status === 429
              ? 'Too many requests. Wait a minute and try again.'
              : 'Could not reach the Prepaid Engine API — is it running?'),
        );
      },
    });
  }

  /** Step 2: the code plus a new password. */
  resetPassword(): void {
    this.problems.set([]);
    if (!this.code.trim()) {
      this.errorMessage.set('Enter the 6-digit code from your e-mail.');
      return;
    }
    if (this.newPassword !== this.confirmPassword) {
      this.errorMessage.set('The two passwords do not match.');
      return;
    }
    this.checking.set(true);
    this.errorMessage.set(null);
    this.auth.resetPassword(this.username.trim(), this.code.trim(), this.newPassword).subscribe({
      next: (res) => {
        this.checking.set(false);
        this.show('login');
        this.infoMessage.set(res.message);
      },
      error: (err) => {
        this.checking.set(false);
        this.errorMessage.set(err?.error?.error ?? 'Could not reset the password. Try again.');
        this.problems.set(err?.error?.problems ?? []);
      },
    });
  }

  private lockedMessage(seconds?: number): string {
    if (!seconds) return 'Too many failed attempts. Try again in a few minutes, or use "Forgot password?".';
    const minutes = Math.ceil(seconds / 60);
    return `Too many failed attempts. Try again in ${minutes} minute${minutes === 1 ? '' : 's'}, or use "Forgot password?" to reset now.`;
  }
}
