import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { API_CONFIG } from '../api/api-config';
import { authInterceptor } from './auth.interceptor';
import { TokenStore } from './token.store';

/**
 * The real interceptor, through a real HttpClient.
 *
 * This spec used to re-implement the interceptor's decision and assert against
 * the copy, which passes happily while the shipped code does something else.
 * It matters more now: the interceptor renews an expired access token and
 * retries, which is several steps that can each go wrong quietly - a refresh
 * that recurses into itself, a retry that goes out with the old token, a
 * failed refresh that leaves the member on a broken page.
 */
describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let tokens: TokenStore;
  let navigated: { commands: unknown[]; extras?: unknown } | null;

  beforeEach(() => {
    sessionStorage.clear();
    navigated = null;

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: Router,
          useValue: {
            navigate: (commands: unknown[], extras?: unknown) => {
              navigated = { commands, extras };
              return Promise.resolve(true);
            },
          },
        },
      ],
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    tokens = TestBed.inject(TokenStore);
  });

  afterEach(() => {
    backend.verify();
    sessionStorage.clear();
  });

  it('attaches the access token', () => {
    tokens.set('access-1', 'refresh-1');

    http.get('/v1/identity/me').subscribe();

    const request = backend.expectOne('/v1/identity/me');

    expect(request.request.headers.get('Authorization')).toBe('Bearer access-1');
    request.flush({});
  });

  it('sends no Authorization header when signed out', () => {
    http.get('/v1/identity/tenants/directory').subscribe();

    const request = backend.expectOne('/v1/identity/tenants/directory');

    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush([]);
  });

  it('renews an expired access token and retries with the new one', () => {
    // The whole point of refresh tokens: a member who left a tab open over
    // lunch should not be sent to the login screen.
    tokens.set('stale', 'refresh-1');

    let body: unknown = null;
    http.get('/v1/members/me').subscribe((result) => (body = result));

    backend.expectOne('/v1/members/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    const refresh = backend.expectOne('/v1/identity/token/refresh');
    expect(refresh.request.body).toEqual({ refreshToken: 'refresh-1' });

    refresh.flush({
      accessToken: 'access-2',
      expiresAt: '2026-01-01T00:15:00Z',
      refreshToken: 'refresh-2',
      refreshTokenExpiresAt: '2026-01-15T00:00:00Z',
      userId: 'u1',
      tenantId: 't1',
      tenantSlug: 'mumbai-samaaj',
      fullName: 'Ravi Shah',
      roles: ['Member'],
    });

    const retried = backend.expectOne('/v1/members/me');
    expect(retried.request.headers.get('Authorization')).toBe('Bearer access-2');

    retried.flush({ fullName: 'Ravi Shah' });

    expect(body).toEqual({ fullName: 'Ravi Shah' });
    expect(navigated).toBeNull();
  });

  it('stores the rotated refresh token, because the old one is now spent', () => {
    // Keeping the old one would present it again on the next refresh, and a
    // refresh token presented twice ends the whole session.
    tokens.set('stale', 'refresh-1');

    http.get('/v1/members/me').subscribe({ error: () => undefined });

    backend.expectOne('/v1/members/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    backend.expectOne('/v1/identity/token/refresh').flush({
      accessToken: 'access-2',
      expiresAt: '2026-01-01T00:15:00Z',
      refreshToken: 'refresh-2',
      refreshTokenExpiresAt: '2026-01-15T00:00:00Z',
      userId: 'u1',
      tenantId: 't1',
      tenantSlug: '',
      fullName: 'Ravi Shah',
      roles: [],
    });

    backend.expectOne('/v1/members/me').flush({});

    expect(tokens.refreshToken()).toBe('refresh-2');
    expect(tokens.token()).toBe('access-2');
  });

  it('ends the session when the refresh is itself refused', () => {
    tokens.set('stale', 'revoked-token');

    http.get('/v1/members/me').subscribe({ error: () => undefined });

    backend.expectOne('/v1/members/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    backend
      .expectOne('/v1/identity/token/refresh')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(tokens.token()).toBeNull();
    expect(tokens.refreshToken()).toBeNull();
    expect(navigated?.commands).toEqual(['/login']);
  });

  it('does not try to refresh the refresh call itself', () => {
    // Retrying a failed refresh by refreshing recurses until the stack gives
    // out, and the member sees a hung page rather than a login screen.
    tokens.set('stale', 'refresh-1');

    http.post('/v1/identity/token/refresh', { refreshToken: 'refresh-1' })
      .subscribe({ error: () => undefined });

    backend
      .expectOne('/v1/identity/token/refresh')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    backend.verify();
  });

  it('ends the session on 401 when there is no refresh token to use', () => {
    tokens.set('stale');

    http.get('/v1/members/me').subscribe({ error: () => undefined });

    backend.expectOne('/v1/members/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(tokens.token()).toBeNull();
    expect(navigated?.commands).toEqual(['/login']);
  });

  it('leaves an anonymous 401 alone, because there was no session to end', () => {
    http.get('/v1/identity/me').subscribe({ error: () => undefined });

    backend.expectOne('/v1/identity/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(navigated).toBeNull();
  });

  it('ends the session on a tenant mismatch without trying to refresh', () => {
    // The token belongs to another Samaaj, so it is inapplicable here rather
    // than merely expired - a new access token would be just as wrong.
    tokens.set('other-samaaj', 'refresh-1');

    http.get('/v1/members/me').subscribe({ error: () => undefined });

    backend.expectOne('/v1/members/me').flush(
      { title: 'Tenant.Mismatch', detail: 'Wrong Samaaj.' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(tokens.token()).toBeNull();
    expect(navigated?.extras).toEqual({ queryParams: { otherSamaaj: true } });
  });

  it('leaves an ordinary 403 alone', () => {
    // "Signed in but not allowed." Signing the member out for asking about a
    // page they cannot see would be baffling.
    tokens.set('access-1', 'refresh-1');

    http.get('/v1/identity/tenants').subscribe({ error: () => undefined });

    backend.expectOne('/v1/identity/tenants').flush(
      { title: 'Auth.Forbidden', detail: 'Not allowed.' },
      { status: 403, statusText: 'Forbidden' },
    );

    expect(tokens.token()).toBe('access-1');
    expect(navigated).toBeNull();
  });
});
