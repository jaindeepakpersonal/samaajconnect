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
};

/**
 * Deactivating a Samaaj asks for the administrator's own password.
 *
 * The server refuses a deactivation that arrives without one, so a button that
 * fired straight off one click would simply be broken. These tests are about
 * the screen holding up its end of that.
 */
describe('TenantListComponent deactivation', () => {
  let fixture: ComponentFixture<TenantListComponent>;
  let component: TenantListComponent;
  let http: HttpTestingController;

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

    fixture = TestBed.createComponent(TenantListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    http.expectOne('/v1/identity/tenants/modules').flush([]);
    http.expectOne((r) => r.url === '/v1/identity/tenants').flush([TENANT]);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  function tenant() {
    return component.tenants()[0];
  }

  it('asks before it acts rather than deactivating on one click', () => {
    component.askToConfirm(tenant());
    fixture.detectChanges();

    expect(component.confirming()).toBe('t1');

    // http.verify() in afterEach is the assertion that matters: nothing was
    // sent.
  });

  it('explains what deactivating does before asking for the password', () => {
    component.askToConfirm(tenant());
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('signs out every one of its members');
  });

  it('sends the password with the status change', () => {
    component.askToConfirm(tenant());
    component.password = 'correct-horse';
    component.confirmDeactivate(tenant());

    const request = http.expectOne('/v1/identity/tenants/t1/status');

    expect(request.request.body).toEqual({ status: 'Inactive', password: 'correct-horse' });

    request.flush({ ...TENANT, status: 'Inactive' });
    fixture.detectChanges();

    expect(component.tenants()[0].status).toBe('Inactive');
    expect(component.confirming()).toBeNull();
  });

  it('keeps the panel open when the password is wrong', () => {
    // A closed panel would make an administrator start again to fix a typo,
    // and the message belongs beside the field rather than at the top of the
    // page.
    component.askToConfirm(tenant());
    component.password = 'wrong';
    component.confirmDeactivate(tenant());

    http
      .expectOne('/v1/identity/tenants/t1/status')
      .flush(
        { title: 'Auth.StepUpFailed', detail: 'That password is not correct.' },
        { status: 403, statusText: 'Forbidden' },
      );

    fixture.detectChanges();

    expect(component.confirming()).toBe('t1');
    expect(component.confirmError()).toBeTruthy();
    expect(component.tenants()[0].status).toBe('Active');
  });

  it('forgets the password when the panel is cancelled', () => {
    component.askToConfirm(tenant());
    component.password = 'correct-horse';
    component.cancelConfirm();

    expect(component.password).toBe('');
    expect(component.confirming()).toBeNull();
  });

  it('does not ask for a password to bring a Samaaj back into service', () => {
    // Deliberately asymmetric. Activating restores service and is undone by the
    // very call that undid it; a step-up on the harmless direction only teaches
    // people to type their password without reading the screen.
    component.setStatus({ ...tenant(), status: 'Inactive' }, 'Active');

    const request = http.expectOne('/v1/identity/tenants/t1/status');

    expect(request.request.body).toEqual({ status: 'Active', password: undefined });

    request.flush({ ...TENANT, status: 'Active' });
  });
});
