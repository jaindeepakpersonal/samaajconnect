import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { AdminListComponent } from './admin-list.component';

const PENDING = {
  userId: 'u2',
  fullName: 'Rajesh Jain',
  mobileOrEmail: 'rajesh@example.com',
  status: 'PendingActivation',
  roles: ['SamaajAdmin'],
  lastLoginAt: null as string | null,
};

const ACTIVE = { ...PENDING, userId: 'u1', fullName: 'Meera Shah', status: 'Active' };

/**
 * Re-issuing a one-time activation code.
 *
 * The Invite screen has told administrators from the day it shipped that "a
 * lost code is re-issued from the Admin Users screen, which cancels this one".
 * Nothing did: the endpoint and the client method both existed and no screen
 * called either, so an account stuck at Pending Activation stayed stuck while
 * the dashboard counted it every day. `scripts/uncalled-api-methods.sh` is what
 * found it — the endpoint sweep could not, because the path literal sits in the
 * API client.
 */
describe('AdminListComponent re-issuing a code', () => {
  let fixture: ComponentFixture<AdminListComponent>;
  let component: AdminListComponent;
  let http: HttpTestingController;

  function start(admins: object[] = [PENDING]): void {
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

  it('offers a code only for an account still waiting to be activated', () => {
    start([PENDING]);

    expect(buttonSaying('Re-issue code')).toBeDefined();
  });

  it('and not for one that has already been activated', () => {
    // Minting a code for an active account would be offering to do something
    // the service refuses, which is a button that always answers an error.
    start([ACTIVE]);

    expect(buttonSaying('Re-issue code')).toBeUndefined();
  });

  it('shows the code once, with what that means', () => {
    start([PENDING]);

    component.reissue(component.admins()[0]);

    const request = http.expectOne('/v1/identity/activations/u2/code');

    expect(request.request.method).toBe('POST');

    request.flush({
      userId: 'u2',
      mobileOrEmail: 'rajesh@example.com',
      fullName: 'Rajesh Jain',
      code: 'ABC-123',
      expiresAt: '2026-09-05T10:00:00Z',
    });

    fixture.detectChanges();

    expect(text()).toContain('ABC-123');
    expect(text()).toContain('Shown once');

    // An administrator who hands out a second code without knowing the first
    // one has stopped working leaves somebody holding a dead code.
    expect(text()).toContain('cancelled any earlier code');
  });

  it('shows a refusal instead of a code', () => {
    start([PENDING]);

    component.reissue(component.admins()[0]);

    http.expectOne('/v1/identity/activations/u2/code').flush(
      { title: 'Forbidden', detail: 'You may not issue codes here.' },
      { status: 403, statusText: 'Forbidden' },
    );

    fixture.detectChanges();

    expect(text()).toContain('You may not issue codes here');
    expect(component.issued()).toBeNull();
  });

  it('clears the previous code before asking for the next', () => {
    // Otherwise a failed request leaves the last person's code on screen beside
    // a different name, which is how one gets handed to the wrong person.
    start([PENDING, { ...PENDING, userId: 'u3', fullName: 'Anita Shah' }]);

    component.reissue(component.admins()[0]);
    http.expectOne('/v1/identity/activations/u2/code').flush({
      userId: 'u2',
      mobileOrEmail: 'rajesh@example.com',
      fullName: 'Rajesh Jain',
      code: 'ABC-123',
      expiresAt: '2026-09-05T10:00:00Z',
    });
    fixture.detectChanges();

    component.reissue(component.admins()[1]);
    fixture.detectChanges();

    expect(component.issued()).toBeNull();
    expect(text()).not.toContain('ABC-123');

    http.expectOne('/v1/identity/activations/u3/code').flush({
      userId: 'u3',
      mobileOrEmail: 'anita@example.com',
      fullName: 'Anita Shah',
      code: 'XYZ-789',
      expiresAt: '2026-09-05T10:00:00Z',
    });
    fixture.detectChanges();

    expect(text()).toContain('XYZ-789');
  });
});
