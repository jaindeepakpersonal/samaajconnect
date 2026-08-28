import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TokenStore } from '@samaajconnect/shared';
import { AdminScope, adminScopeInterceptor } from './admin-scope';
import { Tenant } from './admin.models';

const TENANT: Tenant = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Mahavir Samaaj',
  slug: 'mahavir-samaj',
  domain: null,
  logoUrl: null,
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
