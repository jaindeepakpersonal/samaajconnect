import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '@samaajconnect/shared';
import { catchError, map, of } from 'rxjs';

/**
 * Holds the panel until the signed-in user's roles are known.
 *
 * Roles and permissions come from `/v1/identity/me`, not from the token, so a
 * screen that branches on them - and most of this panel does - reads an empty
 * list if it initialises first. That is not a cosmetic race: a Super Admin's
 * tenant tile stayed blank because the screen decided it was not a Super Admin
 * before the answer arrived, and the Samaaj-scoped tiles showed a confident
 * zero for a Samaaj nobody had selected. Both look like permission bugs.
 *
 * Sits alongside `authGuard`, which checks only that a token exists. This one
 * is about what that token turns out to mean.
 *
 * Like every guard here, a UX concern only: the endpoints behind these screens
 * re-check roles and permissions server-side (root `CLAUDE.md` §7).
 */
export const currentUserGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.ensureCurrentUser().pipe(
    map(() => true),

    // A rejected token is already being sent to /login by the interceptor.
    // Anything else - the service down, say - lands on the login screen too,
    // because a panel that cannot say who you are cannot decide what to show.
    catchError(() => of(router.createUrlTree(['/login']))),
  );
};
