import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { PathshalaDetailComponent } from './pathshala-detail.component';
import { PathshalaDetail } from '../../core/admin.models';

const PATHSHALA_ID = 'p1';

function detail(overrides: Partial<PathshalaDetail> = {}): PathshalaDetail {
  return {
    id: PATHSHALA_ID,
    name: 'Shri Mahavir Jain Pathshala',
    address: { line: 'Hiran Magri', countryId: null, stateId: null, cityId: null, localityId: null },
    contactPerson: null,
    status: 'Active',
    acceptsEnrolments: true,
    sessions: [
      {
        id: 's1',
        label: '2026-27',
        startDate: '2026-03-01',
        endDate: '2027-02-28',
        isCurrent: true,
      },
    ],
    classes: [
      {
        id: 'c1',
        sessionId: 's1',
        sessionLabel: '2026-27',
        name: 'Class 8 — Jain Studies',
        roomLabel: 'Room 2',
        schedule: [],
        teacherIds: ['t1', 't2'],
        studentCount: 4,
      },
    ],
    ...overrides,
  };
}

describe('PathshalaDetailComponent', () => {
  let fixture: ComponentFixture<PathshalaDetailComponent>;
  let component: PathshalaDetailComponent;
  let http: HttpTestingController;
  let query: Record<string, string>;

  beforeEach(() => {
    query = {};

    TestBed.configureTestingModule({
      imports: [PathshalaDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: () => PATHSHALA_ID },
              queryParamMap: { get: (key: string) => query[key] ?? null },
            },
          },
        },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  const tabLabels = () =>
    [...(fixture.nativeElement as HTMLElement).querySelectorAll('[role="tab"]')].map((t) =>
      t.textContent?.trim(),
    );

  function signIn(roles: string[], permissions: string[], areaLocalityId: string | null = null) {
    TestBed.inject(AuthService).ensureCurrentUser().subscribe();
    http.expectOne('/v1/identity/me').flush({
      userId: 'u9',
      tenantId: 't1',
      tenantSlug: 'dasha-nagda',
      mobileOrEmail: 'someone@example.com',
      fullName: 'Someone',
      status: 'Active',
      isContactVerified: true,
      lastLoginAt: null,
      roles,
      permissions,
      areaLocalityId,
    });
  }

  const asAdmin = () => signIn(['Member', 'SamaajAdmin'], ['Members.Read', 'Pathshala.Manage']);

  function load(school: PathshalaDetail = detail()) {
    fixture = TestBed.createComponent(PathshalaDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}`).flush(school);
    fixture.detectChanges();
  }

  /** Answers the re-read every action fires. */
  function reload(school: PathshalaDetail = detail()) {
    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}`).flush(school);
    fixture.detectChanges();
  }

  function openTab(label: string) {
    const tab = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="tab"]')]
      .find((t) => t.textContent?.trim() === label)!;

    tab.click();
    fixture.detectChanges();
  }

  it('offers somebody who runs it all nine tabs', () => {
    asAdmin();
    load();

    expect(tabLabels()).toEqual([
      'Academic sessions',
      'Classes',
      'Teachers',
      'Students',
      'Exams',
      'Progress report',
      'Attendance',
      'Donations',
      'Announcements',
    ]);
  });

  it('shows a teacher every tab but Donations, and no forms', () => {
    // Who gave how much is the administrators' to keep, and the service
    // refuses a teacher the ledger - so the tab is not drawn at all.
    signIn(['Member', 'PathshalaTeacher'], ['Members.Read', 'Pathshala.Attendance.Write']);
    load();

    expect(tabLabels()).not.toContain('Donations');
    expect(tabLabels()).toContain('Announcements');
    expect(text()).not.toContain('Open a session');

    openTab('Classes');

    expect(text()).not.toContain('Add a class');
  });

  it('shows an Area Admin a Pathshala elsewhere without any tabs', () => {
    signIn(['Member', 'AreaAdmin'], ['Pathshala.Manage'], 'loc-hm');
    load(
      detail({
        address: { line: null, countryId: null, stateId: null, cityId: null, localityId: 'loc-other' },
      }),
    );

    // http.verify() in afterEach is the other half: no tab asked for anything.
    expect(text()).toContain('another locality');
    expect(tabLabels()).toEqual([]);
  });

  it('loads a tab only when it is opened', () => {
    asAdmin();
    load();

    http.expectNone(`/v1/pathshala/pathshalas/${PATHSHALA_ID}/teachers`);

    openTab('Teachers');

    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}/teachers`).flush([]);
    fixture.detectChanges();

    expect(text()).toContain('No teachers yet');
  });

  it('opens on the tab a class page links back to', () => {
    query['tab'] = 'classes';
    asAdmin();
    load();

    expect(text()).toContain('Class 8 — Jain Studies');
    expect(text()).toContain('Add a class to 2026-27');
  });

  it('falls back to the first tab when linked to one the caller cannot see', () => {
    query['tab'] = 'donations';
    signIn(['Member', 'PathshalaTeacher'], ['Members.Read']);
    load();

    expect(component.tab()).toBe('sessions');
  });

  it('counts a class’s teachers from its teacher records', () => {
    query['tab'] = 'classes';
    asAdmin();
    load();

    const cells = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tbody td')].map((td) =>
      td.textContent?.trim(),
    );

    expect(cells[cells.length - 1]).toBe('2');
  });

  it('offers no class form until a session is open', () => {
    // A class belongs to a session and the service refuses one without it.
    // Offering the form anyway would mean a 404 that reads as a bug.
    query['tab'] = 'classes';
    asAdmin();
    load(detail({ sessions: [], classes: [] }));

    expect(text()).toContain('Open a session first');
    expect(text()).not.toContain('Add a class to');
  });

  it('adds a class to the current session', () => {
    asAdmin();
    load();

    component.className = 'Class 9';
    component.roomLabel = '';
    component.createClass('s1');

    const call = http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}/classes`);

    // A blank room is null, not '': the service stores null, and sending an
    // empty string would make "no room" and "a room called nothing" two states.
    expect(call.request.body).toEqual({ sessionId: 's1', name: 'Class 9', roomLabel: null });

    call.flush({});
    reload();
  });

  it('opens a session only when it has a label and both dates', () => {
    asAdmin();
    load();

    component.sessionLabel = '2027-28';
    component.sessionStart = '';
    component.sessionEnd = '2028-02-29';
    component.openSession();

    http.expectNone(`/v1/pathshala/pathshalas/${PATHSHALA_ID}/sessions`);
  });

  it('re-reads the Pathshala after every action rather than patching the screen', () => {
    asAdmin();
    load();

    component.sessionLabel = '2027-28';
    component.sessionStart = '2027-03-01';
    component.sessionEnd = '2028-02-29';
    component.openSession();

    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}/sessions`).flush(detail());
    reload();

    expect(text()).toContain('is now the current session');
  });

  it('re-reads the Pathshala when a tab changes what it holds', () => {
    // Removing a teacher takes them off classes, and placing a child changes a
    // class's count - both of which the Classes tab shows.
    asAdmin();
    load();

    component.load();
    reload(detail({ classes: [] }));

    expect(component.pathshala()?.classes).toEqual([]);
  });

  it('explains a 404 as no such Pathshala rather than as an error', () => {
    fixture = TestBed.createComponent(PathshalaDetailComponent);
    fixture.detectChanges();
    http
      .expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such Pathshala');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
