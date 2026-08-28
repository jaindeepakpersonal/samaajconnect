import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { AdminScope } from '../../core/admin-scope';
import { InviteAdminComponent } from './invite-admin.component';

const MATRIX = {
  permissions: ['AdminUsers.Manage'],
  editable: false,
  editableNote: 'Fixed in source.',
  roles: [
    {
      id: 'a1',
      name: 'SamaajAdmin',
      assignableToAdmins: true,
      permissions: ['AdminUsers.Manage'],
    },
    { id: 'a2', name: 'SuperAdmin', assignableToAdmins: false, permissions: ['Tenant.Manage'] },
    { id: 'a3', name: 'Member', assignableToAdmins: false, permissions: [] },
  ],
};

describe('InviteAdminComponent', () => {
  let fixture: ComponentFixture<InviteAdminComponent>;
  let component: InviteAdminComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      imports: [InviteAdminComponent],
      providers: [
        provideRouter([{ path: 'admins', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    // A Samaaj Admin's own Samaaj, so the screen is not asking for a selection.
    TestBed.inject(AdminScope);

    fixture = TestBed.createComponent(InviteAdminComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    http.expectOne('/v1/identity/roles').flush(MATRIX);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('offers only the roles an administrator may hand out', () => {
    const names = component.assignableRoles().map((r) => r.name);

    // SuperAdmin can only come from the platform bootstrap; Member is earned
    // by registering. Offering either would be offering something the backend
    // refuses.
    expect(names).toEqual(['SamaajAdmin']);
  });

  it('does not call the API without a role', () => {
    component.form.setValue({ fullName: 'Rajesh Jain', mobileOrEmail: 'rajesh@example.com' });

    component.submit();

    http.expectNone('/v1/identity/admins');
    fixture.detectChanges();
    expect(text()).toContain('Choose at least one role');
  });

  it('does not call the API with an identifier the service would reject', () => {
    // The same rule identity-tenant-service applies. A mismatch here is either
    // a wasted round trip or a login nobody can create from this screen.
    component.form.setValue({ fullName: 'Rajesh Jain', mobileOrEmail: '1234567890' });
    component.toggle('SamaajAdmin');

    component.submit();

    http.expectNone('/v1/identity/admins');
  });

  it('shows the one-time code once, and never again after inviting another', () => {
    component.form.setValue({ fullName: 'Rajesh Jain', mobileOrEmail: 'rajesh@example.com' });
    component.toggle('SamaajAdmin');
    component.submit();

    const request = http.expectOne('/v1/identity/admins');
    expect(request.request.body).toEqual({
      fullName: 'Rajesh Jain',
      mobileOrEmail: 'rajesh@example.com',
      roles: ['SamaajAdmin'],
    });

    request.flush({
      userId: 'u1',
      fullName: 'Rajesh Jain',
      mobileOrEmail: 'rajesh@example.com',
      roles: ['SamaajAdmin'],
      activationCode: 'K7QRP2WX9M',
      codeExpiresAt: '2026-03-08T00:00:00Z',
    });
    fixture.detectChanges();

    expect(text()).toContain('K7QRP2WX9M');
    expect(text()).toContain('only time the code is shown');

    // Leaving the previous code on screen while a second invitation is typed
    // is how one gets handed to the wrong person.
    component.inviteAnother();
    fixture.detectChanges();

    expect(text()).not.toContain('K7QRP2WX9M');
    expect(component.selected()).toEqual([]);
  });

  it('reports a refused invitation and keeps the form', () => {
    component.form.setValue({ fullName: 'Rajesh Jain', mobileOrEmail: 'rajesh@example.com' });
    component.toggle('SamaajAdmin');
    component.submit();

    http.expectOne('/v1/identity/admins').flush(
      // The shape the services actually return: RFC 9457, with the message in
      // `detail`.
      {
        title: 'Conflict',
        detail: 'That mobile number or email address already has an account.',
        status: 409,
      },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(component.invited()).toBeNull();
    expect(text()).toContain('already has an account');
  });
});
