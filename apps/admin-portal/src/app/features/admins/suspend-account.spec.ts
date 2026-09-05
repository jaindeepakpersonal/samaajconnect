import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { AdminListComponent } from './admin-list.component';

const ACTIVE = {
  userId: 'u1',
  fullName: 'Meera Shah',
  mobileOrEmail: 'meera@example.com',
  status: 'Active',
  roles: ['SamaajAdmin'],
  lastLoginAt: null as string | null,
};

const SUSPENDED = { ...ACTIVE, userId: 'u2', fullName: 'Rajesh Jain', status: 'Suspended' };

/**
 * Suspending or reinstating an account.
 *
 * SetUserSuspensionCommand, its step-up, and the self-suspend and erased-
 * account refusals were all built and tested against a running database, and
 * PUT /admins/{userId}/status was in API-CONTRACTS.md from the day it landed
 * - but AdminListComponent never called it, so a Samaaj administrator with a
 * problem account had no way to act on it short of asking the platform
 * operator to archive the whole Samaaj. scripts/unreachable-endpoints.sh is
 * what found it.
 */
describe('AdminListComponent suspending an account', () => {
  let fixture: ComponentFixture<AdminListComponent>;
  let component: AdminListComponent;
  let http: HttpTestingController;

  function start(admins: object[] = [ACTIVE]): void {
    fixture = TestBed.createComponent(AdminListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    http.expectOne('/v1/identity/roles').flush({
      permissions: [],
      roles: [{ id: 'r1', name: 'SamaajAdmin', assignableToAdmins: true, permissions: [], editable: true }],
      editable: false,
      editableNote: '',
    });

    http.expectOne('/v1/identity/admins').flush(admins);
    fixture.detectChanges();
  }

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      imports: [AdminListComponent],
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

  it('offers to suspend an active account', () => {
    start([ACTIVE]);

    expect(buttonSaying('Suspend')).toBeDefined();
    expect(buttonSaying('Reinstate')).toBeUndefined();
  });

  it('offers to reinstate a suspended one instead', () => {
    start([SUSPENDED]);

    expect(buttonSaying('Reinstate')).toBeDefined();
    expect(buttonSaying('Suspend')).toBeUndefined();
  });

  it('asks for a password before it acts', () => {
    start([ACTIVE]);

    component.askToConfirm(component.admins()[0]!);
    fixture.detectChanges();

    expect(text()).toContain('Suspending Meera Shah signs them out immediately');
    http.expectNone('/v1/identity/admins/u1/status');
  });

  it('announces that warning to a screen reader rather than only showing it', () => {
    // The same WCAG 4.1.3 gap as tenant deactivation's own confirm panel,
    // which this screen's panel was copied from: nothing else on the page
    // moves focus when this appears, so a screen reader user hears nothing
    // unless the warning is a live region.
    start([ACTIVE]);

    component.askToConfirm(component.admins()[0]!);
    fixture.detectChanges();

    const warning = (fixture.nativeElement as HTMLElement).querySelector(
      'form.confirm p[role="status"]',
    );

    expect(warning?.textContent).toContain('signs them out immediately');
  });

  it('suspends with the password once confirmed', () => {
    start([ACTIVE]);

    component.askToConfirm(component.admins()[0]!);
    component.password = 'correct horse battery staple';
    component.confirmSuspend(component.admins()[0]!);

    const request = http.expectOne('/v1/identity/admins/u1/status');

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      suspended: true,
      password: 'correct horse battery staple',
    });

    request.flush({ userId: 'u1', status: 'Suspended', changed: true });
    fixture.detectChanges();

    expect(component.admins()[0]!.status).toBe('Suspended');
    expect(component.confirming()).toBeNull();
  });

  it('reinstates in one click, with no password', () => {
    start([SUSPENDED]);

    component.reinstate(component.admins()[0]!);

    const request = http.expectOne('/v1/identity/admins/u2/status');

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ suspended: false, password: undefined });

    request.flush({ userId: 'u2', status: 'Active', changed: true });
    fixture.detectChanges();

    expect(component.admins()[0]!.status).toBe('Active');
  });

  it('shows a refusal rather than pretending it worked', () => {
    // Suspending yourself, or an already-erased account, both come back 409.
    start([ACTIVE]);

    component.askToConfirm(component.admins()[0]!);
    component.password = 'wrong';
    component.confirmSuspend(component.admins()[0]!);

    http.expectOne('/v1/identity/admins/u1/status').flush(
      { title: 'Conflict', detail: 'You cannot suspend your own account. Ask another administrator.' },
      { status: 409, statusText: 'Conflict' },
    );

    fixture.detectChanges();

    expect(text()).toContain('You cannot suspend your own account');
    // Left open, with the message beside the field - closing it on a refusal
    // would make the admin start again from nothing to see why.
    expect(component.confirming()).toBe('u1');
  });

  it('leaves the panel open across attempts for a different account', () => {
    start([ACTIVE, { ...ACTIVE, userId: 'u3', fullName: 'Anita Shah' }]);

    component.askToConfirm(component.admins()[0]!);
    fixture.detectChanges();
    expect(component.confirming()).toBe('u1');

    component.askToConfirm(component.admins()[1]!);
    fixture.detectChanges();

    // Opening a second account's panel replaces the first, rather than
    // stacking - there is one password field, for one account at a time.
    expect(component.confirming()).toBe('u3');
  });
});
