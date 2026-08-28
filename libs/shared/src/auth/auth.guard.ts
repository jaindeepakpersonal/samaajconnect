import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TokenStore } from './token.store';

/**
 * Keeps signed-out visitors off member pages.
 *
 * A UX convenience only. Every endpoint behind these pages re-checks the
 * caller's roles and permissions server-side, and that check - not this one -
 * is the authorization boundary (root CLAUDE.md section 7).
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const tokens = inject(TokenStore);
  const router = inject(Router);

  if (tokens.isSignedIn) {
    return true;
  }

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
