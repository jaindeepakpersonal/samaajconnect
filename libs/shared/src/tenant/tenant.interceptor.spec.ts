import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_CONFIG } from '../api/api-config';
import { tenantInterceptor } from './tenant.interceptor';

/**
 * The interceptor every request in both apps passes through, and which had no
 * tests at all.
 *
 * Every screen writes its paths relative — `/v1/members`, not a full URL — so
 * this is the single piece of code that decides where those go. If it stops
 * rewriting, every call in both applications goes to the app's own origin and
 * 404s; if it rewrites something it should not, an absolute URL somebody wrote
 * deliberately gets a gateway prefix glued onto the front of it.
 */
describe('tenantInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  function configure(gatewayUrl: string) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tenantInterceptor])),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl } },
      ],
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  }

  afterEach(() => backend.verify());

  it('sends a relative path to the gateway', () => {
    configure('http://localhost:8080');

    http.get('/v1/members').subscribe();

    backend.expectOne('http://localhost:8080/v1/members').flush([]);
  });

  it('leaves a relative path alone when the gateway is same-origin', () => {
    // Both apps ship with `gatewayUrl: ''` in production: the member portal is
    // served by the gateway itself and the admin panel's nginx proxies /v1 to
    // it, so a prefix would be wrong in both.
    configure('');

    http.get('/v1/members').subscribe();

    backend.expectOne('/v1/members').flush([]);
  });

  it('does not touch an absolute URL, on either scheme', () => {
    // Somebody who wrote a whole URL meant it. Prefixing would produce
    // "http://localhost:8080https://example.com/thing".
    //
    // Both schemes, because testing only `https` leaves the guard passing while
    // it has quietly stopped recognising `http` — which is the one a local
    // gateway or a staging host is on.
    configure('http://localhost:8080');

    http.get('https://example.com/thing').subscribe();
    http.get('http://example.com/thing').subscribe();

    backend.expectOne('https://example.com/thing').flush({});
    backend.expectOne('http://example.com/thing').flush({});
  });

  it('does not touch an absolute URL whatever the scheme case', () => {
    configure('http://localhost:8080');

    http.get('HTTPS://example.com/thing').subscribe();

    backend.expectOne('HTTPS://example.com/thing').flush({});
  });

  it('preserves the method, body and headers it was given', () => {
    // The interceptor clones the request to change the URL. A clone that
    // dropped anything else would be a silent change to every write in both
    // apps.
    configure('http://localhost:8080');

    http.post('/v1/families', { name: 'Jain' }, { headers: { 'X-Thing': 'kept' } }).subscribe();

    const call = backend.expectOne('http://localhost:8080/v1/families');

    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual({ name: 'Jain' });
    expect(call.request.headers.get('X-Thing')).toBe('kept');

    call.flush({});
  });

  it('keeps the query string', () => {
    configure('http://localhost:8080');

    http.get('/v1/children/names', { params: { ids: 'a,b' } }).subscribe();

    backend.expectOne('http://localhost:8080/v1/children/names?ids=a,b').flush([]);
  });

  it('attaches no tenant header of its own', () => {
    // Deliberate, and the reason this file is named for tenancy while doing
    // nothing about it: the platform is one domain, a member's Samaaj travels
    // in their token, and the gateway strips every inbound tenant header
    // precisely so a client cannot choose its own Samaaj (root CLAUDE.md §6).
    configure('http://localhost:8080');

    http.get('/v1/members').subscribe();

    const call = backend.expectOne('http://localhost:8080/v1/members');

    expect(call.request.headers.has('X-Tenant-Id')).toBe(false);
    expect(call.request.headers.has('X-Tenant-Override-Id')).toBe(false);

    call.flush([]);
  });
});
