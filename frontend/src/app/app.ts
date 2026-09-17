import { Component, HostListener, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { AuthService } from './core/services/auth.service';

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Guards against the browser restoring a signed-out-of page straight from
   * bfcache (back/forward cache) on history navigation, which would show
   * stale authenticated content without re-running route guards. If the
   * restored page is no longer backed by a valid session, replace it with
   * the login screen instead of leaving the shell visible. */
  @HostListener('window:pageshow', ['$event'])
  onPageShow(event: PageTransitionEvent): void {
    if (event.persisted && !this.auth.isAuthenticated()) {
      this.router.navigateByUrl('/login', { replaceUrl: true });
    }
  }
}
