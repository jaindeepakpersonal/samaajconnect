import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { GroupsListComponent } from './groups-list.component';
import { VolunteerGroup } from '../../core/admin.models';

function group(overrides: Partial<VolunteerGroup> = {}): VolunteerGroup {
  return {
    id: 'g1',
    name: 'Seva Group',
    description: 'Food drives and blood donation camps.',
    focusArea: 'Social Service',
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

  function load(groups: VolunteerGroup[] = [], members = MEMBERS) {
    fixture.detectChanges();
    http.expectOne('/v1/volunteer-groups/groups').flush(groups);
    http.expectOne('/v1/members?limit=100').flush(members);
    fixture.detectChanges();
  }

  it('reads a 404 as the community module being off', () => {
    fixture.detectChanges();
    http
      .expectOne('/v1/volunteer-groups/groups')
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('does not run the community module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('will not create a group without a president', () => {
    // A group with nobody able to decide its applications is a group whose join
    // requests go nowhere — the same dead end as a Pathshala enrolment nobody
    // could place. The service requires one; so does the form.
    load();

    component.name = 'Seva Group';
    component.presidentMemberId = '';

    expect(component.canCreate()).toBe(false);

    component.create();
    http.expectNone('/v1/volunteer-groups/groups');
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

  function reload() {
    http.expectOne('/v1/volunteer-groups/groups').flush([group()]);
    http.expectOne('/v1/members?limit=100').flush(MEMBERS);
    fixture.detectChanges();
  }
});
