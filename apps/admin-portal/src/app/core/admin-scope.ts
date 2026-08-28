import { HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { TokenStore } from '@samaajconnect/shared';
import { Tenant } from './admin.models';

const STORAGE_KEY = 'samaajconnect.admin.scope';

/**
 * Which Samaaj the admin panel is currently acting on.
 *
 * This is the top bar's Samaaj selector from the admin wireframe. For a Samaaj
 * Admin it is fixed: their token names one Samaaj and nothing here can change
 * that, so the selector is not offered. For a Super Admin, whose token names no
 * Samaaj at all, choosing one sends `X-Tenant-Override-Id` and every service
 * behind the gateway then scopes to it.
 *
 * That header is a real privilege, not a convenience. The gateway refuses it
 * from anyone without the SuperAdmin role and writes an audit entry on every
 * request that carries one - on a single domain there is no admin hostname to
 * gate it by, so the role on the validated token is the whole gate and the
 * audit log is the only record of who acted on whose Samaaj (root `CLAUDE.md`
 * §6). The panel therefore makes the current scope visible at all times rather
 * than letting it be something the admin has forgotten about.
 */
@Injectable({ providedIn: 'root' })
export class AdminScope {
  private readonly selected = signal<Tenant | null>(restore());

  /** The Samaaj being acted on, or null for the platform-wide view. */
  readonly tenant = this.selected.asReadonly();
  readonly tenantId = computed(() => this.selected()?.id ?? null);
  readonly label = computed(() => this.selected()?.name ?? 'All Samaaj');

  select(tenant: Tenant | null): void {
    this.selected.set(tenant);

    try {
      if (tenant === null) {
        sessionStorage.removeItem(STORAGE_KEY);
      } else {
        sessionStorage.setItem(STORAGE_KEY, JSON.stringify(tenant));
      }
    } catch {
      // A browser with storage blocked still works; the scope just resets on
      // reload. Never let a storage failure stop an admin from acting.
    }
  }

  clear(): void {
    this.select(null);
  }
}

function restore(): Tenant | null {
  try {
    const stored = sessionStorage.getItem(STORAGE_KEY);

    return stored === null ? null : (JSON.parse(stored) as Tenant);
  } catch {
    return null;
  }
}

/**
 * Attaches the Super Admin tenant override when a Samaaj is selected.
 *
 * Only ever added to requests this app makes, and only when an admin has
 * actively chosen a Samaaj <i>and</i> is signed in. The gateway refuses the
 * header from a caller without the SuperAdmin role, so a Samaaj Admin sending
 * it by accident would get a 403 rather than someone else's data - but the
 * panel does not offer them the selector in the first place.
 */
export const adminScopeInterceptor: HttpInterceptorFn = (request, next) => {
  const scope = inject(AdminScope);
  const tokens = inject(TokenStore);
  const tenantId = scope.tenantId();

  // Nothing anonymous carries an override. The scope outlives a sign-out - it
  // is in sessionStorage and a forced sign-out does not clear it - so without
  // this the *login* request goes out with a Samaaj attached, the gateway
  // rightly refuses an override from a caller with no token, and the admin is
  // told they may not act on another Samaaj while trying to sign in.
  if (tenantId === null || tokens.token() === null) {
    return next(request);
  }

  return next(
    request.clone({ setHeaders: { 'X-Tenant-Override-Id': tenantId } }),
  );
};
