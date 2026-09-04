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
  grievanceContact: null as object | null,
};

/**
 * Naming the person a member complains to about their data (DPDP section 13).
 *
 * The endpoint has existed since the obligation was written down, and
 * `DPDP-COMPLIANCE.md` has marked section 13 **built** — while the only way to
 * actually name anybody was curl. `scripts/unreachable-endpoints.sh` never saw
 * it, because the path literal lives in the API client, so the endpoint counted
 * as reached while the client method had no caller at all.
 */
describe('TenantListComponent grievance contact', () => {
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

  function buttonSaying(label: string): HTMLButtonElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim().startsWith(label)) as HTMLButtonElement | undefined;
  }

  it('says when a Samaaj has named nobody', () => {
    start();

    // A statutory obligation nobody has met should be visible without opening
    // anything, because the count of Samaaj that have not is the number that
    // matters to the platform operator.
    expect(text()).toContain('Not named');
  });

  it('does not say that when one is named', () => {
    start({
      ...TENANT,
      grievanceContact: { name: 'Rajesh Jain', email: 'grievance@example.com', phone: null },
    });

    expect(text()).not.toContain('Not named');
  });

  it('seeds the form from what is stored', () => {
    // The command replaces all three fields at once, so a panel that opened
    // empty would silently drop the name whenever somebody corrected a phone
    // number.
    start({
      ...TENANT,
      grievanceContact: { name: 'Rajesh Jain', email: 'grievance@example.com', phone: null },
    });

    component.openGrievance(component.tenants()[0]);
    fixture.detectChanges();

    expect(component.grievance.name).toBe('Rajesh Jain');
    expect(component.grievance.email).toBe('grievance@example.com');
    expect(component.grievance.phone).toBe('');
  });

  it('refuses a name with no way to reach the person', () => {
    // The service's own rule, duplicated here and required to stay in step: a
    // name on its own is not a means of redressal.
    start();

    component.openGrievance(component.tenants()[0]);
    component.grievance = { name: 'Rajesh Jain', email: '', phone: '' };
    fixture.detectChanges();

    expect(component.grievanceIncomplete()).toBe(true);
    expect(buttonSaying('Save contact')!.disabled).toBe(true);

    component.saveGrievance(component.tenants()[0]);

    http.expectNone(`/v1/identity/tenants/t1/grievance-contact`);
  });

  it('allows clearing all three, which removes the contact', () => {
    start({
      ...TENANT,
      grievanceContact: { name: 'Rajesh Jain', email: 'grievance@example.com', phone: null },
    });

    component.openGrievance(component.tenants()[0]);
    component.grievance = { name: '', email: '', phone: '' };
    fixture.detectChanges();

    // Removing a contact is not the same as naming an unreachable one, and the
    // Act asks for one rather than forbidding its removal.
    expect(component.grievanceIncomplete()).toBe(false);
  });

  it('sends the three fields and blanks as null', () => {
    start();

    component.openGrievance(component.tenants()[0]);
    component.grievance = { name: 'Rajesh Jain', email: 'grievance@example.com', phone: '  ' };
    component.saveGrievance(component.tenants()[0]);

    const request = http.expectOne('/v1/identity/tenants/t1/grievance-contact');

    expect(request.request.method).toBe('PUT');
    expect(request.request.body.name).toBe('Rajesh Jain');
    expect(request.request.body.email).toBe('grievance@example.com');
    expect(request.request.body.phone).toBeNull();

    request.flush({
      ...TENANT,
      grievanceContact: { name: 'Rajesh Jain', email: 'grievance@example.com', phone: null },
    });

    fixture.detectChanges();

    expect(text()).not.toContain('Not named');
  });

  it('shows a refusal rather than closing as though it saved', () => {
    start();

    component.openGrievance(component.tenants()[0]);
    component.grievance = { name: 'Rajesh Jain', email: 'grievance@example.com', phone: '' };
    component.saveGrievance(component.tenants()[0]);

    http.expectOne('/v1/identity/tenants/t1/grievance-contact').flush(
      { title: 'Forbidden', detail: 'Only this Samaaj may set its own contact.' },
      { status: 403, statusText: 'Forbidden' },
    );

    fixture.detectChanges();

    expect(text()).toContain('Only this Samaaj may set its own contact');
    expect(component.grievanceFor()).toBe('t1');
  });
});
