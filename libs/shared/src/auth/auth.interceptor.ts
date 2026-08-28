import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails } from '../api/problem-details';
import { TokenStore } from './token.store';

/**
 * Error code the services return when a request's token was issued for a
 * different Samaaj than the one the request arrived on.
 */
const TENANT_MISMATCH = 'Tenant.Mismatch';

/**
 * Attaches the bearer token, and drops it when it is no longer usable here.
 *
 * Two cases end the session:
 *
 * - **401** - the token expired or was rejected.
 * - **403 Tenant.Mismatch** - the token belongs to another Samaaj. A token is
 *   scoped to one Samaaj, so on this host it is not merely insufficient, it is
 *   inapplicable; keeping it would also break the *anonymous* screens, because
 *   the services refuse a mismatched token before they ever check whether the
 *   request needed authentication at all.
 *
 * Any other 403 is left alone: it means "you are signed in but may not do
 * this", and signing someone out for asking about a page they cannot see would
 * be baffling.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const tokens = inject(TokenStore);
  const router = inject(Router);

  const token = tokens.token();

  const authorized = token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (token && error instanceof HttpErrorResponse && endsSession(error)) {
        tokens.clear();

        void router.navigate(['/login'], {
          queryParams: error.status === 401 ? { expired: true } : { otherSamaaj: true },
        });
      }

      return throwError(() => error);
    }),
  );
};

function endsSession(error: HttpErrorResponse): boolean {
  if (error.status === 401) {
    return true;
  }

  return (
    error.status === 403 && (error.error as ProblemDetails | null)?.title === TENANT_MISMATCH
  );
}
