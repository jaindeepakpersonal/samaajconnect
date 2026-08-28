import { HttpClient, HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, switchMap, throwError } from 'rxjs';
import { ProblemDetails } from '../api/problem-details';
import { RefreshResult } from './auth.models';
import { TokenStore } from './token.store';

/**
 * Error code the services return when a request's token was issued for a
 * different Samaaj than the one the request arrived on.
 */
const TENANT_MISMATCH = 'Tenant.Mismatch';

const REFRESH_URL = '/v1/identity/token/refresh';

/**
 * Attaches the bearer token, renews it when it has expired, and ends the
 * session when it cannot.
 *
 * Access tokens last fifteen minutes, so a member who leaves a tab open over
 * lunch comes back to an expired one. Before refresh tokens existed the only
 * answer was the login screen; now a 401 buys a new access token and the
 * original request is retried, and the member notices nothing.
 *
 * Three things end the session outright:
 *
 * - **401 with no refresh token, or a refresh that is itself refused.** The
 *   refresh endpoint gives one answer for every reason it declines, so there is
 *   nothing to distinguish here either: sign in again.
 * - **403 Tenant.Mismatch** - the token belongs to another Samaaj. A token is
 *   scoped to one Samaaj, so it is not merely insufficient here, it is
 *   inapplicable; keeping it would also break the *anonymous* screens, because
 *   the services refuse a mismatched token before they ever check whether the
 *   request needed authentication at all.
 * - **A 401 on the refresh call itself**, which is handled by not recursing
 *   into it.
 *
 * Any other 403 is left alone: it means "you are signed in but may not do
 * this", and signing someone out for asking about a page they cannot see would
 * be baffling.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const tokens = inject(TokenStore);
  const router = inject(Router);
  const http = inject(HttpClient);

  const token = tokens.token();

  return next(authorize(request, token)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      if (isMismatch(error)) {
        return endSession(tokens, router, { otherSamaaj: true }, error);
      }

      if (error.status !== 401) {
        return throwError(() => error);
      }

      // Refreshing is itself a request through this interceptor. Retrying a
      // failed refresh by refreshing would recurse until the stack gave out.
      if (request.url.endsWith(REFRESH_URL)) {
        return throwError(() => error);
      }

      const refreshToken = tokens.refreshToken();

      if (!refreshToken) {
        // Nothing to renew with. An anonymous request that 401s is not a
        // session ending - there was no session - so this only redirects when
        // there was a token to lose.
        return token
          ? endSession(tokens, router, { expired: true }, error)
          : throwError(() => error);
      }

      return http.post<RefreshResult>(REFRESH_URL, { refreshToken }).pipe(
        switchMap((renewed) => {
          tokens.set(renewed.accessToken, renewed.refreshToken);

          // The original request, with the new token. The member sees a
          // slightly slower response and nothing else.
          return next(authorize(request, renewed.accessToken));
        }),
        catchError(() => endSession(tokens, router, { expired: true }, error)),
      );
    }),
  );
};

function authorize(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}

function isMismatch(error: HttpErrorResponse): boolean {
  return (
    error.status === 403 && (error.error as ProblemDetails | null)?.title === TENANT_MISMATCH
  );
}

function endSession(
  tokens: TokenStore,
  router: Router,
  queryParams: Record<string, boolean>,
  error: HttpErrorResponse,
): Observable<never> {
  tokens.clear();

  void router.navigate(['/login'], { queryParams });

  return throwError(() => error);
}
