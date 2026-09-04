import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { TenantListComponent } from './tenant-list.component';

const TENANT = {
  id: 't1',
  name: 'Mumbai Samaaj',
  slug: 'mumbai-samaaj',
  status: 'Active',
  enabledModules: ['community'],
  logoUrl: null as string | null,
};

/**
 * Setting a Samaaj's logo.
 *
 * The wireframe's Create Samaaj screen has drawn an "Upload Logo" control since
 * the start and `LogoUrl` has been on the record since the first migration,
 * with nothing able to write one — no command ever took a logo. These tests
 * cover the screen that closed that.
 */
describe('TenantListComponent logo', () => {
  let fixture: ComponentFixture<TenantListComponent>;
  let component: TenantListComponent;
  let http: HttpTestingController;

  function start(tenant: object = TENANT): void {
    fixture = TestBed.createComponent(TenantListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    http.expectOne('/v1/identity/tenants/modules').flush([]);
    http.expectOne((r) => r.url === '/v1/identity/tenants').flush([tenant]);
    fixture.detectChanges();
  }

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      imports: [TenantListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  function choose(file: File): void {
    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('input[type="file"]');

    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function png(bytes = 10): File {
    return new File([new Uint8Array(bytes)], 'logo.png', { type: 'image/png' });
  }

  /**
   * A plain `<img src>`, and that is the design rather than a shortcut: a logo
   * is served anonymously because the registration form needs it before anybody
   * has a token, so unlike a member photo it needs nothing to attach one.
   */
  it('draws the logo straight from its path, with no directive in the way', () => {
    start({ ...TENANT, logoUrl: '/v1/identity/tenants/t1/logo' });

    const logo: HTMLImageElement = fixture.nativeElement.querySelector('.tenant-logo');

    expect(logo).not.toBeNull();
    expect(logo.getAttribute('src')).toBe('/v1/identity/tenants/t1/logo');
  });

  it('shows no logo for a Samaaj that has none', () => {
    start();

    expect(fixture.nativeElement.querySelector('.tenant-logo')).toBeNull();
  });

  /**
   * The trigger stays in the DOM and stays enabled. A panel rendered in an
   * `@else` branch takes the button the user just pressed away and drops
   * keyboard focus to the body — the finding three screens shared in the
   * 2026-09-02 accessibility pass.
   */
  it('keeps the trigger and says what it did', () => {
    start();

    const button = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Logo')!;

    expect(button.getAttribute('aria-expanded')).toBe('false');

    button.click();
    fixture.detectChanges();

    expect(button.isConnected).toBe(true);
    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-expanded')).toBe('true');
  });

  it('uploads the chosen file as multipart and re-reads', () => {
    start();
    component.openLogo(component.tenants()[0]);
    fixture.detectChanges();

    choose(png());

    const upload = http.expectOne('/v1/identity/tenants/t1/logo');
    expect(upload.request.method).toBe('POST');
    expect(upload.request.body).toBeInstanceOf(FormData);
    // The browser writes the multipart boundary; one set by hand would have
    // none and no server could parse the body.
    expect(upload.request.headers.has('Content-Type')).toBe(false);

    upload.flush(null);

    // Only the tenant list: `load()` does not refetch the module catalogue,
    // which is read once on init.
    http.expectOne((r) => r.url === "/v1/identity/tenants")
      .flush([{ ...TENANT, logoUrl: "/v1/identity/tenants/t1/logo" }]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.tenant-logo')).not.toBeNull();
  });

  /**
   * The service refuses it anyway. Checking here saves sending two megabytes
   * over a connection to be told what was already knowable.
   */
  it('refuses a logo over 2 MB without sending it', () => {
    start();
    component.openLogo(component.tenants()[0]);
    fixture.detectChanges();

    choose(png(2 * 1024 * 1024 + 1));

    http.verify();
    expect(text()).toContain('larger than 2 MB');
  });

  it('reports a refused upload rather than claiming it worked', () => {
    start();
    component.openLogo(component.tenants()[0]);
    fixture.detectChanges();

    choose(png());

    http.expectOne('/v1/identity/tenants/t1/logo').flush(
      { detail: 'That file is not a picture the platform accepts.' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(text()).toContain('not a picture the platform accepts');
  });

  it('offers removal only when there is a logo, and re-reads after', () => {
    start({ ...TENANT, logoUrl: '/v1/identity/tenants/t1/logo' });
    component.openLogo(component.tenants()[0]);
    fixture.detectChanges();

    expect(text()).toContain('Remove logo');

    component.removeLogo(component.tenants()[0]);

    const removal = http.expectOne('/v1/identity/tenants/t1/logo');
    expect(removal.request.method).toBe('DELETE');
    removal.flush(null);

    http.expectOne((r) => r.url === "/v1/identity/tenants").flush([TENANT]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector(".tenant-logo")).toBeNull();
  });

  /**
   * The panel says what a logo is for and, more importantly, that it is public.
   * An administrator putting something private in a Samaaj's mark should be
   * told before rather than after.
   */
  it('says the logo is public', () => {
    start();
    component.openLogo(component.tenants()[0]);
    fixture.detectChanges();

    expect(text()).toContain('served to anyone');
  });
});
