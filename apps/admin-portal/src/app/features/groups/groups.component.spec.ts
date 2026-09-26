import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { GroupsListComponent } from './groups-list.component';
import { VolunteerGroup } from '../../core/admin.models';

function group(overrides: Partial<VolunteerGroup> = {}): VolunteerGroup {
  return {
    id: 'g1',
    name: 'Seva Group',
    description: 'Food drives and blood donation camps.',
    focusArea: 'Social Service',
    localityId: null,
    presidentMemberId: 'm1',
    status: 'Active',
    memberCount: 12,
    ...overrides,
  };
}

const MEMBERS = [
  { id: 'm1', fullName: 'Diya Jain' },
  { id: 'm2', fullName: 'Aarav Jain' },
];

describe('GroupsListComponent', () => {
  let fixture: ComponentFixture<GroupsListComponent>;
  let component: GroupsListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [GroupsListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(GroupsListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  const LOCALITIES = [{ id: 'loc-hm', cityId: 'c1', name: 'Hiran Magri', isActive: true }];
  const CITIES = [{ id: 'c1', countryId: 'in', stateId: null, name: 'Udaipur', isActive: true }];

  /** The locality names the Locality column and the popup's picker show. */
  function flushLocalities() {
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush(LOCALITIES);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush(CITIES);
  }

  function load(groups: VolunteerGroup[] = [], members = MEMBERS) {
    fixture.detectChanges();
    flushLocalities();
    http.expectOne('/v1/volunteer-groups/groups').flush(groups);
    http.expectOne('/v1/members?take=100').flush({ items: members, hasMore: false });
    fixture.detectChanges();
  }

  /** Signs this test in as an Area Admin for Hiran Magri, then builds a fresh screen. */
  function asAreaAdmin() {
    TestBed.inject(AuthService).ensureCurrentUser().subscribe();
    http.expectOne('/v1/identity/me').flush({
      userId: 'u9',
      tenantId: 't1',
      tenantSlug: 'dasha-nagda',
      mobileOrEmail: 'area@example.com',
      fullName: 'Area Admin',
      status: 'Active',
      isContactVerified: true,
      lastLoginAt: null,
      roles: ['Member', 'AreaAdmin'],
      permissions: ['VolunteerGroups.Manage'],
      areaLocalityId: 'loc-hm',
    });

    fixture = TestBed.createComponent(GroupsListComponent);
    component = fixture.componentInstance;
  }

  it('offers an Area Admin controls only on their own locality s groups', () => {
    asAreaAdmin();
    load([
      group({ id: 'g1', name: 'Hiran Seva', localityId: 'loc-hm' }),
      group({ id: 'g2', name: 'Sector Seva', localityId: 'loc-other' }),
    ]);

    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'),
    ) as HTMLElement[];

    expect(rows[0]!.textContent).toContain('Hiran Magri, Udaipur');
    expect(rows[0]!.textContent).toContain('Stand down');
    expect(rows[1]!.textContent).not.toContain('Stand down');
    expect(rows[1]!.textContent).toContain('Another locality');
  });

  it('sets up an Area Admin s group in their own locality', () => {
    asAreaAdmin();
    load();

    const clickEvent = { currentTarget: document.createElement('button') } as unknown as MouseEvent;
    component.openCreateModal(clickEvent);
    component.name = 'Hiran Seva';
    component.create();

    expect(
      (http.expectOne('/v1/volunteer-groups/groups').request.body as { localityId: string })
        .localityId,
    ).toBe('loc-hm');
  });

  it('reads a 404 as the community module being off', () => {
    fixture.detectChanges();
    flushLocalities();
    http
      .expectOne('/v1/volunteer-groups/groups')
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('does not run the community module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('creates a group with no president when none is chosen, sending null', () => {
    // A group may exist before anyone is chosen to lead it; until then a Samaaj
    // admin accepts joiners, and a president is set later.
    load();

    component.name = 'Seva Group';
    component.presidentMemberId = '';

    expect(component.canSubmit()).toBe(true);

    component.create();

    const call = http.expectOne('/v1/volunteer-groups/groups');
    expect(call.request.body).toEqual({
      name: 'Seva Group',
      description: null,
      focusArea: null,
      presidentMemberId: null,
      localityId: null,
    });
  });

  it('still needs a name', () => {
    load();

    component.name = '   ';

    expect(component.canSubmit()).toBe(false);
  });

  it('shows Not set and offers Set president for a group with no president', () => {
    load([group({ presidentMemberId: null })]);

    expect(text()).toContain('Not set');
    expect(text()).toContain('Set president');
  });

  it('creates a group with its president, sending no blanks', () => {
    load();

    component.name = '  Seva Group  ';
    component.description = '   ';
    component.focusArea = 'Social Service';
    component.presidentMemberId = 'm2';
    component.create();

    const call = http.expectOne('/v1/volunteer-groups/groups');

    expect(call.request.body).toEqual({
      name: 'Seva Group',
      description: null,
      focusArea: 'Social Service',
      presidentMemberId: 'm2',
      localityId: null,
    });

    call.flush(group());
    reload();

    expect(text()).toContain('Aarav Jain as president');
  });

  it('names the president rather than printing an id', () => {
    load([group({ presidentMemberId: 'm1' })]);

    expect(text()).toContain('Diya Jain');
    expect(text()).not.toContain('m1');
  });

  it('falls back to "A member" when the directory could not be read', () => {
    load([group()], []);

    expect(text()).toContain('A member');
  });

  it('stands a group down rather than deleting it', () => {
    // Inactive keeps its members and its history and simply stops taking new
    // applications. A Samaaj that ran a seva group for one monsoon should still
    // be able to see who was in it.
    load([group({ status: 'Active' })]);

    component.setStatus(group(), 'Inactive');

    const call = http.expectOne('/v1/volunteer-groups/groups/g1/status');

    expect(call.request.method).toBe('PATCH');
    expect(call.request.body).toEqual({ status: 'Inactive' });

    call.flush(group({ status: 'Inactive' }));
    reload();

    expect(text()).toContain('members and history are kept');
  });

  it('offers to bring an inactive group back', () => {
    load([group({ status: 'Inactive' })]);

    expect(text()).toContain('Bring back');
    expect(text()).not.toContain('Stand down');
  });

  // ---- Editing --------------------------------------------------------------
  //
  // Create and Edit share one modal, the same shape Events' own screen uses -
  // UpdateGroupCommand did not exist until this pass, so there was no way to
  // correct a typo in a name or description at all.

  it('opens the create modal with a blank form', () => {
    load();

    component.name = 'Leftover text';
    const clickEvent = { currentTarget: document.createElement('button') } as unknown as MouseEvent;
    component.openCreateModal(clickEvent);
    fixture.detectChanges();

    expect(component.modalOpen()).toBe(true);
    expect(component.name).toBe('');
    expect(text()).toContain('Set up a group');
  });

  it('opens the edit modal pre-filled from the row', () => {
    load([group()]);

    const clickEvent = { currentTarget: document.createElement('button') } as unknown as MouseEvent;
    component.openEditModal(clickEvent, group());
    fixture.detectChanges();

    expect(component.name).toBe('Seva Group');
    expect(component.description).toBe('Food drives and blood donation camps.');
    expect(component.focusArea).toBe('Social Service');
    expect(text()).toContain('Edit Seva Group');
  });

  it('sends a correction and closes the modal', () => {
    load([group()]);

    const clickEvent = { currentTarget: document.createElement('button') } as unknown as MouseEvent;
    component.openEditModal(clickEvent, group());
    component.name = 'Seva Group (Renamed)';
    component.update();

    const call = http.expectOne('/v1/volunteer-groups/groups/g1');

    expect(call.request.method).toBe('PATCH');
    expect(call.request.body).toEqual({
      name: 'Seva Group (Renamed)',
      description: 'Food drives and blood donation camps.',
      focusArea: 'Social Service',
      localityId: null,
    });

    call.flush(group({ name: 'Seva Group (Renamed)' }));
    reload();

    expect(component.modalOpen()).toBe(false);
    expect(text()).toContain('updated');
  });

  // ---- Changing a president -----------------------------------------------
  //
  // VolunteerGroup.ChangePresident existed and was called from nowhere - this
  // service's own CLAUDE.md named GroupPresidentChangedDomainEvent as "Raised
  // by VolunteerGroup.ChangePresident" the whole time nothing ever called it.

  it('opens a panel to choose the new president', () => {
    load([group()]);

    component.openChangePresident(group());
    fixture.detectChanges();

    expect(text()).toContain('New president');
  });

  it('hands the group over and reports who leads it now', () => {
    load([group()]);

    component.openChangePresident(group());
    component.newPresidentMemberId = 'm2';
    component.changePresident(group());

    const call = http.expectOne('/v1/volunteer-groups/groups/g1/president');

    expect(call.request.method).toBe('PATCH');
    expect(call.request.body).toEqual({ newPresidentMemberId: 'm2' });

    call.flush(group({ presidentMemberId: 'm2' }));
    reload();

    expect(text()).toContain('Aarav Jain');
    expect(component.presidentFor()).toBeNull();
  });

  it('does nothing until somebody is actually chosen', () => {
    load([group()]);

    component.openChangePresident(group());
    component.changePresident(group());

    http.expectNone('/v1/volunteer-groups/groups/g1/president');
  });

  it('shows a refusal rather than pretending it worked', () => {
    load([group()]);

    component.openChangePresident(group());
    component.newPresidentMemberId = 'm2';
    component.changePresident(group());

    http.expectOne('/v1/volunteer-groups/groups/g1/president').flush(
      { title: 'Forbidden', detail: 'Only a Samaaj admin may change a group’s president.' },
      { status: 403, statusText: 'Forbidden' },
    );

    fixture.detectChanges();

    expect(text()).toContain('Only a Samaaj admin may change');
  });

  function reload() {
    http.expectOne('/v1/volunteer-groups/groups').flush([group()]);
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    fixture.detectChanges();
  }
});
