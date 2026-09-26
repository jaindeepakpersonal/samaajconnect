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
    {
      id: 'a4',
      name: 'AreaAdmin',
      assignableToAdmins: true,
      permissions: ['Members.Read', 'Members.Write'],
    },
  ],
};

const LOCALITIES = [
  { id: 'loc-hm', cityId: 'c1', name: 'Hiran Magri', isActive: true },
  { id: 'loc-sc', cityId: 'c1', name: 'Sector 14', isActive: true },
];

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
    http
      .expectOne((r) => r.url === '/v1/master-data/localities')
      .flush(LOCALITIES);
    http
      .expectOne((r) => r.url === '/v1/master-data/cities')
      .flush([{ id: 'c1', countryId: 'in', stateId: null, name: 'Udaipur', isActive: true }]);
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
    expect(names).toEqual(['SamaajAdmin', 'AreaAdmin']);
  });

  // ---- Area Admin ----------------------------------------------------------

  it('offers a locality picker only once AreaAdmin is chosen', () => {
    expect(fixture.nativeElement.querySelector('#invite-locality')).toBeNull();

    component.toggle('AreaAdmin');
    fixture.detectChanges();

    const select = fixture.nativeElement.querySelector('#invite-locality') as HTMLSelectElement;
    expect(select).not.toBeNull();

    // With its city: locality names repeat across cities.
    expect(text()).toContain('Hiran Magri, Udaipur');
  });

  it('never lets AreaAdmin sit alongside another role', () => {
    component.toggle('SamaajAdmin');
    component.toggle('AreaAdmin');
    expect(component.selected()).toEqual(['AreaAdmin']);

    component.toggle('SamaajAdmin');
    expect(component.selected()).toEqual(['SamaajAdmin']);
  });

  it('unticks the other boxes on screen when AreaAdmin is ticked, not just in state', () => {
    const box = (name: string) =>
      fixture.nativeElement.querySelector(`#role-${name}`) as HTMLInputElement;

    box('SamaajAdmin').click();
    fixture.detectChanges();
    box('AreaAdmin').click();
    fixture.detectChanges();

    expect(box('AreaAdmin').checked).toBe(true);
    expect(box('SamaajAdmin').checked).toBe(false);

    box('SamaajAdmin').click();
    fixture.detectChanges();

    expect(box('SamaajAdmin').checked).toBe(true);
    expect(box('AreaAdmin').checked).toBe(false);
  });

  it('refuses to invite an Area Admin with no locality', () => {
    component.form.setValue({ fullName: 'Rajesh Jain', mobileOrEmail: 'rajesh@example.com' });
    component.toggle('AreaAdmin');

    component.submit();

    http.expectNone('/v1/identity/admins');
    fixture.detectChanges();
    expect(text()).toContain('An Area Admin needs a locality');
  });

  it('sends the locality with an Area Admin invitation', () => {
    component.form.setValue({ fullName: 'Rajesh Jain', mobileOrEmail: 'rajesh@example.com' });
    component.toggle('AreaAdmin');
    component.localityId.set('loc-hm');

    component.submit();

    expect(http.expectOne('/v1/identity/admins').request.body).toEqual({
      fullName: 'Rajesh Jain',
      mobileOrEmail: 'rajesh@example.com',
      roles: ['AreaAdmin'],
      localityId: 'loc-hm',
    });
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

  // ---- Granting an existing member a role ----------------------------------

  it('searches the member directory rather than the admin roster', () => {
    component.setMode('existing');
    component.searchTerm = 'Ravi';
    component.search();

    const request = http.expectOne(
      (r) => r.method === 'GET' && r.url === '/v1/members' && r.params.get('term') === 'Ravi',
    );

    request.flush({
      items: [
        { id: 'm5', fullName: 'Ravi Shah' } as never,
        { id: 'm6', fullName: 'Ravi Mehta' } as never,
      ],
      hasMore: false,
    });

    expect(component.searchResults().map((m) => m.fullName)).toEqual(['Ravi Shah', 'Ravi Mehta']);
  });

  it('grants each checked role with its own call, and reports the outcome', () => {
    component.setMode('existing');
    component.selectMember({ id: 'm5', fullName: 'Ravi Shah' } as never);
    component.toggle('SamaajAdmin');
    component.grant();

    const request = http.expectOne('/v1/identity/admins/m5/roles/SamaajAdmin');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ granted: true });

    request.flush({
      userId: 'm5',
      role: 'SamaajAdmin',
      granted: true,
      changed: true,
      roles: ['SamaajAdmin'],
    });
    fixture.detectChanges();

    expect(text()).toContain('Ravi Shah');
    expect(text()).toContain('SamaajAdmin');
    expect(text()).toContain('already have a password');
  });

  it('grants AreaAdmin to an existing member only with a locality, and sends it', () => {
    component.setMode('existing');
    component.selectMember({ id: 'm5', fullName: 'Ravi Shah' } as never);
    component.toggle('AreaAdmin');

    component.grant();
    http.expectNone('/v1/identity/admins/m5/roles/AreaAdmin');

    component.localityId.set('loc-sc');
    component.grant();

    expect(
      http.expectOne('/v1/identity/admins/m5/roles/AreaAdmin').request.body,
    ).toEqual({ granted: true, localityId: 'loc-sc' });
  });

  it('refuses to grant with nobody selected', () => {
    component.setMode('existing');
    component.toggle('SamaajAdmin');
    component.grant();

    http.expectNone((r) => r.url.startsWith('/v1/identity/admins/'));
  });

  it('does not offer roles that stayed from the invite form after switching modes', () => {
    // Selections are per-mode: a role ticked while inviting a new person must
    // not silently carry over to granting an existing member one.
    component.toggle('SamaajAdmin');
    expect(component.selected()).toEqual(['SamaajAdmin']);

    component.setMode('existing');

    expect(component.selected()).toEqual([]);
  });

  it('keeps a refused grant off the page and lets the member stay selected', () => {
    component.setMode('existing');
    component.selectMember({ id: 'm5', fullName: 'Ravi Shah' } as never);
    component.toggle('SamaajAdmin');
    component.grant();

    http.expectOne('/v1/identity/admins/m5/roles/SamaajAdmin').flush(
      { title: 'Forbidden', detail: 'You are not allowed to perform this action.', status: 403 },
      { status: 403, statusText: 'Forbidden' },
    );
    fixture.detectChanges();

    expect(component.granted()).toBeNull();
    expect(text()).toContain('not allowed');
    expect(component.selectedMember()?.fullName).toBe('Ravi Shah');
  });
});
