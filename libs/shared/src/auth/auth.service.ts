import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import {
  ConsentNotice,
  CurrentUser,
  LoginResult,
  RegisterRequest,
  RegisterResult,
  TenantSummary,
} from './auth.models';
import { TokenStore } from './token.store';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly tokens = inject(TokenStore);

  private readonly currentUser = signal<CurrentUser | null>(null);

  readonly user = this.currentUser.asReadonly();
  readonly isSignedIn = computed(() => this.tokens.token() !== null);
  readonly roles = computed(() => this.currentUser()?.roles ?? []);

  login(mobileOrEmail: string, password: string): Observable<LoginResult> {
    return this.http
      .post<LoginResult>('/v1/identity/login', { mobileOrEmail, password })
      .pipe(tap((result) => this.tokens.set(result.accessToken)));
  }

  register(request: RegisterRequest): Observable<RegisterResult> {
    return this.http.post<RegisterResult>('/v1/identity/register', request);
  }

  loadCurrentUser(): Observable<CurrentUser> {
    return this.http
      .get<CurrentUser>('/v1/identity/me')
      .pipe(tap((user) => this.currentUser.set(user)));
  }

  /** Anonymous lookup of one Samaaj by slug. */
  findTenant(slug: string): Observable<TenantSummary> {
    return this.http.get<TenantSummary>(`/v1/identity/tenants/${encodeURIComponent(slug)}`);
  }

  /** The consent notice, which must be shown before registering (DPDP s.5). */
  consentNotice(): Observable<ConsentNotice> {
    return this.http.get<ConsentNotice>('/v1/identity/consent-notice');
  }

  signOut(): void {
    this.tokens.clear();
    this.currentUser.set(null);
  }

  /** True when the signed-in member holds any of the given roles. */
  hasAnyRole(...roles: readonly string[]): boolean {
    const held = this.currentUser()?.roles ?? [];

    return roles.some((role) => held.includes(role));
  }
}
