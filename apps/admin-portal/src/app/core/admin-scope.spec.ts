import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TokenStore } from '@samaajconnect/shared';
import { AdminScope, adminScopeInterceptor } from './admin-scope';
import { Tenant } from './admin.models';

type HttpErrorResponseLike = { status: number };

const TENANT: Tenant = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Mahavir Samaaj',
  slug: 'mahavir-samaj',
  domain: null,
  logoUrl: null,
  bannerUrl: null,
  contactPerson: null,
  contactEmail: null,
  status: 'Active',
  enabledModules: ['pathshala'],
  createdAt: '2026-01-01T00:00:00Z',
  grievanceContact: null,
};

describe('AdminScope', () => {
  let scope: AdminScope;
  let http: HttpTestingController;
  let client: HttpClient;
  let tokens: TokenStore;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([adminScopeInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    scope = TestBed.inject(AdminScope);
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    tokens = TestBed.inject(TokenStore);

    // Signed in, as every request that legitimately carries an override is.
    tokens.set("access-token", "refresh-token");
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  it('sends no override until a Samaaj is chosen', () => {
    client.get('/v1/identity/admins').subscribe();

    const request = http.expectOne('/v1/identity/admins');

    // A Super Admin who has not chosen a Samaaj is on the platform view, and
    // an override header would silently put them inside one.
    expect(request.request.headers.has('X-Tenant-Override-Id')).toBe(false);
    request.flush([]);
  });

  it('sends the override once a Samaaj is chosen', () => {
    scope.select(TENANT);

    client.get('/v1/identity/admins').subscribe();

    const request = http.expectOne('/v1/identity/admins');

    expect(request.request.headers.get('X-Tenant-Override-Id')).toBe(TENANT.id);
    request.flush([]);
  });

  it('stops sending the override when the scope is cleared', () => {
    scope.select(TENANT);
    scope.clear();

    client.get('/v1/identity/admins').subscribe();

    const request = http.expectOne('/v1/identity/admins');

    expect(request.request.headers.has('X-Tenant-Override-Id')).toBe(false);
    request.flush([]);
  });

  it('sends no override on an anonymous request, whatever the stored scope says', () => {
    // The scope outlives a sign-out, so without this the *login* request goes
    // out with a Samaaj attached and the gateway refuses an override from a
    // caller with no token - telling an admin they may not act on another
    // Samaaj while they are trying to sign in.
    scope.select(TENANT);
    tokens.clear();

    client.post('/v1/identity/login', {}).subscribe();

    const request = http.expectOne('/v1/identity/login');

    expect(request.request.headers.has('X-Tenant-Override-Id')).toBe(false);
    request.flush({});
  });

  describe('when the remembered Samaaj is gone', () => {
    const unavailable = {
      status: 403,
      statusText: 'Forbidden',
    };
    const problem = { title: 'Tenant.Unavailable', status: 403 };

    it('forgets it and retries a read without the override', () => {
      // A database reset leaves this in an open tab. Before, /me failed with
      // it attached and the Super Admin was sent back to sign in, forever.
      scope.select(TENANT);
      let body: unknown;

      client.get('/v1/identity/me').subscribe((response) => (body = response));

      const first = http.expectOne('/v1/identity/me');
      expect(first.request.headers.get('X-Tenant-Override-Id')).toBe(TENANT.id);
      first.flush(problem, unavailable);

      const retry = http.expectOne('/v1/identity/me');
      expect(retry.request.headers.has('X-Tenant-Override-Id')).toBe(false);
      retry.flush({ userId: 'u' });

      expect(body).toEqual({ userId: 'u' });
      expect(scope.tenantId()).toBeNull();
      expect(new AdminScope().tenantId()).toBeNull();
    });

    it('forgets it but does not resend a write without the override', () => {
      // Resending would act on the platform instead of the Samaaj the admin
      // meant, so the write fails and is the admin's to make again.
      scope.select(TENANT);
      let failed: HttpErrorResponseLike | undefined;

      client.post('/v1/events', {}).subscribe({ error: (error) => (failed = error) });

      http.expectOne('/v1/events').flush(problem, unavailable);
      http.expectNone('/v1/events');

      expect(failed?.status).toBe(403);
      expect(scope.tenantId()).toBeNull();
    });

    it('keeps the scope on any other refusal', () => {
      // An ordinary permission refusal says nothing about whether the Samaaj
      // exists, and dropping the admin's chosen scope over one would be a
      // surprise on the very next screen.
      scope.select(TENANT);

      client.get('/v1/audit/logs').subscribe({ error: () => undefined });

      http
        .expectOne('/v1/audit/logs')
        .flush({ title: 'Auth.Forbidden', status: 403 }, unavailable);
      http.expectNone('/v1/audit/logs');

      expect(scope.tenantId()).toBe(TENANT.id);
    });
  });

  it('remembers the chosen Samaaj across a reload', () => {
    // Changing scope reloads the page, so a selection that did not survive
    // that would undo itself the instant it was made.
    scope.select(TENANT);

    const restored = new AdminScope();

    expect(restored.tenantId()).toBe(TENANT.id);
    expect(restored.label()).toBe('Mahavir Samaaj');
  });

  it('reports the platform view when nothing is chosen', () => {
    expect(scope.tenantId()).toBeNull();
    expect(scope.label()).toBe('All Samaaj');
  });

  it('survives storage being unavailable', () => {
    // A browser with site data blocked must still be usable; the scope simply
    // resets on reload rather than the panel failing to work at all.
    const setItem = sessionStorage.setItem;
    sessionStorage.setItem = () => {
      throw new Error('blocked');
    };

    try {
      expect(() => scope.select(TENANT)).not.toThrow();
      expect(scope.tenantId()).toBe(TENANT.id);
    } finally {
      sessionStorage.setItem = setItem;
    }
  });
});
