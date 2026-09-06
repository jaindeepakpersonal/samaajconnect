import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { AdminScope } from '../../core/admin-scope';
import { SecurityPolicy, Tenant } from '../../core/admin.models';
import { SettingsComponent } from './settings.component';

const TENANT: Tenant = {
  id: 't1',
  name: 'Mahavir Samaaj',
  slug: 'mahavir-samaj',
  domain: null,
  logoUrl: null,
  contactPerson: null,
  contactEmail: null,
  status: 'Active',
  enabledModules: ['community'],
  createdAt: '2026-01-01T00:00:00Z',
  grievanceContact: null,
};

const NO_OVERRIDES: SecurityPolicy = {
  minPasswordLength: null,
  otpLifetimeMinutes: null,
  maxFailedLoginAttempts: null,
  lockoutDurationMinutes: null,
  refreshTokenLifetimeDays: null,
};

/** The Settings screen's one built card: a Samaaj's own security policy. */
describe('SettingsComponent', () => {
  let fixture: ComponentFixture<SettingsComponent>;
  let http: HttpTestingController;
  let auth: AuthService;
  let scope: AdminScope;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(SettingsComponent);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    scope = TestBed.inject(AdminScope);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  function signInAs(tenantId: string, ...roles: readonly string[]): void {
    auth.loadCurrentUser().subscribe();

    http.expectOne('/v1/identity/me').flush({
      userId: 'u1',
      tenantId,
      tenantSlug: 'mahavir-samaj',
      mobileOrEmail: 'admin@example.com',
      fullName: 'An Admin',
      status: 'Active',
      isContactVerified: true,
      lastLoginAt: null,
      roles,
      permissions: [],
    });
  }

  function fieldValue(id: string): string {
    return (
      fixture.nativeElement as HTMLElement
    ).querySelector<HTMLInputElement>(`#${id}`)!.value;
  }

  it("loads a Samaaj Admin's own policy without a Samaaj selector", () => {
    signInAs('t1', 'SamaajAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/identity/tenants/t1/security-policy').flush({
      ...NO_OVERRIDES,
      maxFailedLoginAttempts: 3,
    });

    fixture.detectChanges();

    expect(fixture.componentInstance.policy.maxFailedLoginAttempts).toBe(3);
    expect(fixture.componentInstance.policy.minPasswordLength).toBeNull();
    expect(fieldValue('minPasswordLength')).toBe('');
  });

  it('saves the edited policy back to the same tenant', () => {
    signInAs('t1', 'SamaajAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/identity/tenants/t1/security-policy').flush(NO_OVERRIDES);
    fixture.detectChanges();

    fixture.componentInstance.policy.lockoutDurationMinutes = 30;
    fixture.componentInstance.save();

    const request = http.expectOne('/v1/identity/tenants/t1/security-policy');

    expect(request.request.method).toBe('PUT');
    expect(request.request.body.lockoutDurationMinutes).toBe(30);

    request.flush({ ...NO_OVERRIDES, lockoutDurationMinutes: 30 });
    fixture.detectChanges();

    expect(fixture.componentInstance.saved()).toBe(true);
  });

  it('sends null to clear a field back to the platform default', () => {
    signInAs('t1', 'SamaajAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/identity/tenants/t1/security-policy')
      .flush({ ...NO_OVERRIDES, maxFailedLoginAttempts: 3 });
    fixture.detectChanges();

    fixture.componentInstance.policy.maxFailedLoginAttempts = null;
    fixture.componentInstance.save();

    const request = http.expectOne('/v1/identity/tenants/t1/security-policy');

    expect(request.request.body.maxFailedLoginAttempts).toBeNull();
    request.flush(NO_OVERRIDES);
  });

  it('shows a message rather than pretending it saved when the request fails', () => {
    signInAs('t1', 'SamaajAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/identity/tenants/t1/security-policy').flush(NO_OVERRIDES);
    fixture.detectChanges();

    fixture.componentInstance.save();

    http.expectOne('/v1/identity/tenants/t1/security-policy')
      .flush({}, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(fixture.componentInstance.error()).toBeTruthy();
    expect(fixture.componentInstance.saved()).toBe(false);
  });

  it('fetches nothing while a Super Admin is on the platform view', () => {
    signInAs('00000000-0000-0000-0000-000000000000', 'SuperAdmin');
    fixture.detectChanges();

    http.expectNone((req) => req.url.includes('security-policy'));

    expect(
      (fixture.nativeElement as HTMLElement).textContent,
    ).toContain('Choose a Samaaj in the top bar');
  });

  it("fetches the chosen Samaaj's policy once a Super Admin picks one", () => {
    scope.select(TENANT);
    signInAs('00000000-0000-0000-0000-000000000000', 'SuperAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/identity/tenants/t1/security-policy').flush(NO_OVERRIDES);
    fixture.detectChanges();

    expect(fieldValue('minPasswordLength')).toBe('');
  });
});
