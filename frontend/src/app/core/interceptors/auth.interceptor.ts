import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';
import { environment } from '../../../environments/environment';

/** Attaches the bearer token to same-origin API calls, and signs
 * the user back out (returning them to /login) if the API ever rejects it. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  // A relative req.url always "starts with" an empty apiBaseUrl (the production default —
  // see environments/environment.production.ts), which would otherwise make every request
  // — including third-party ones — look like an API call and get the credential attached.
  // Require a non-empty apiBaseUrl configured, matching it explicitly against the request.
  const isApiCall = !!environment.apiBaseUrl && req.url.startsWith(environment.apiBaseUrl);
  const header = auth.authHeaderValue;
  const authedReq = isApiCall && header
    ? req.clone({ setHeaders: { Authorization: header, 'X-Correlation-Id': crypto.randomUUID() } })
    : req;

  return next(authedReq).pipe(
    catchError((err) => {
      if (isApiCall && err?.status === 401) {
        auth.signOut();
        router.navigate(['/login']);
      }
      return throwError(() => err);
    }),
  );
};
