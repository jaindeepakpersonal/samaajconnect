import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, of, shareReplay, tap } from 'rxjs';
import {
  ActivationResult,
  ChangePasswordResult,
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

  private inFlight: Observable<CurrentUser> | null = null;

  readonly user = this.currentUser.asReadonly();
  readonly isSignedIn = computed(() => this.tokens.token() !== null);
  readonly roles = computed(() => this.currentUser()?.roles ?? []);

  login(mobileOrEmail: string, password: string): Observable<LoginResult> {
    return this.http
      .post<LoginResult>('/v1/identity/login', { mobileOrEmail, password })
      .pipe(tap((result) => this.tokens.set(result.accessToken, result.refreshToken)));
  }

  /**
   * Asks for a one-time sign-in code. Answers the same way whether or not the
   * identifier belongs to a real, active account - there is nothing in the
   * response to branch on, deliberately.
   */
  requestLoginOtp(mobileOrEmail: string): Observable<void> {
    return this.http.post<void>('/v1/identity/otp/request', { mobileOrEmail });
  }

  /** Signs in with a one-time code instead of a password. Same shape as `login()`. */
  loginWithOtp(mobileOrEmail: string, code: string): Observable<LoginResult> {
    return this.http
      .post<LoginResult>('/v1/identity/otp/login', { mobileOrEmail, code })
      .pipe(tap((result) => this.tokens.set(result.accessToken, result.refreshToken)));
  }

  register(request: RegisterRequest): Observable<RegisterResult> {
    return this.http.post<RegisterResult>('/v1/identity/register', request);
  }

  /**
   * Redeems a one-time activation code and sets the account's first password.
   *
   * Anonymous by necessity: whoever is doing this cannot sign in yet, and the
   * code is what stands in for authentication. It answers with who the account
   * belongs to and no token, so the next step is signing in normally.
   *
   * Here beside login and register because it is the identity service's third
   * anonymous entry point, not because the admin panel uses it - that panel
   * sends people to the member portal to do this, which is the whole reason the
   * screen had to exist.
   */
  activate(mobileOrEmail: string, code: string, password: string): Observable<ActivationResult> {
    return this.http.post<ActivationResult>('/v1/identity/activations/redeem', {
      mobileOrEmail,
      code,
      password,
    });
  }

  loadCurrentUser(): Observable<CurrentUser> {
    return this.http
      .get<CurrentUser>('/v1/identity/me')
      .pipe(tap((user) => this.currentUser.set(user)));
  }

  /**
   * The signed-in user, loaded at most once.
   *
   * Roles and permissions arrive from /me, not from the token, so anything
   * that branches on them has to wait for this. Without it a screen reads an
   * empty role list during its own construction and decides it is talking to
   * nobody - which looks exactly like a permissions bug and is not one.
   *
   * Cached because several screens ask at once; `shareReplay` means one
   * request, and a failure is not cached so a retry really retries.
   */
  ensureCurrentUser(): Observable<CurrentUser> {
    const loaded = this.currentUser();

    if (loaded !== null) {
      return of(loaded);
    }

    this.inFlight ??= this.loadCurrentUser().pipe(
      tap({ error: () => (this.inFlight = null) }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.inFlight;
  }

  /** Anonymous lookup of one Samaaj by slug. */
  findTenant(slug: string): Observable<TenantSummary> {
    return this.http.get<TenantSummary>(`/v1/identity/tenants/${encodeURIComponent(slug)}`);
  }

  /** The consent notice, which must be shown before registering (DPDP s.5). */
  consentNotice(): Observable<ConsentNotice> {
    return this.http.get<ConsentNotice>('/v1/identity/consent-notice');
  }

  /**
   * Asks for a password reset code. Answers the same way whether or not the
   * identifier belongs to a real, active account.
   */
  requestPasswordReset(mobileOrEmail: string): Observable<void> {
    return this.http.post<void>('/v1/identity/password-reset/request', { mobileOrEmail });
  }

  /**
   * Redeems a password reset code and sets a new password. No token: proving
   * contact-address access is weaker than a real password, so the next step
   * is signing in normally.
   */
  redeemPasswordReset(mobileOrEmail: string, code: string, newPassword: string): Observable<void> {
    return this.http.post<void>('/v1/identity/password-reset/redeem', {
      mobileOrEmail,
      code,
      newPassword,
    });
  }

  /**
   * Sets a new password, given the current one. Ends every other session for
   * the account - the whole point of changing a password is worth nothing if
   * a stolen refresh token is left able to renew forever.
   */
  changePassword(currentPassword: string, newPassword: string): Observable<ChangePasswordResult> {
    return this.http.post<ChangePasswordResult>('/v1/identity/me/password', {
      currentPassword,
      newPassword,
    });
  }

  /**
   * Ends the session on the server as well as in this browser.
   *
   * Clearing local storage alone leaves the refresh token live for a fortnight,
   * which is the gap SECURITY-CHECKLIST.md called out: "signing out" that only
   * forgets is not signing out. The local clear happens first and
   * unconditionally, so a member on a failing network is still signed out here
   * even when the call does not land.
   *
   * `everywhere` ends every session for the account - the thing to offer when
   * someone thinks their password is known.
   */
  signOut(everywhere = false): Observable<unknown> {
    const refreshToken = this.tokens.refreshToken();

    this.tokens.clear();
    this.currentUser.set(null);
    this.inFlight = null;

    if (!refreshToken) {
      return of(null);
    }

    return this.http
      .post('/v1/identity/logout', { refreshToken, everywhere })
      .pipe(catchError(() => of(null)));
  }

  /** True when the signed-in member holds any of the given roles. */
  hasAnyRole(...roles: readonly string[]): boolean {
    const held = this.currentUser()?.roles ?? [];

    return roles.some((role) => held.includes(role));
  }
}
