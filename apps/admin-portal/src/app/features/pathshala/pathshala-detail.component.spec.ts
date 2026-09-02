import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { PathshalaDetailComponent } from './pathshala-detail.component';
import { Enrolment, PathshalaDetail } from '../../core/admin.models';

const PATHSHALA_ID = 'p1';

function detail(overrides: Partial<PathshalaDetail> = {}): PathshalaDetail {
  return {
    id: PATHSHALA_ID,
    name: 'Shri Mahavir Jain Pathshala',
    address: 'Hiran Magri',
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
        teacherMemberIds: [],
        studentCount: 4,
      },
    ],
    ...overrides,
  };
}

function request(overrides: Partial<Enrolment> = {}): Enrolment {
  return {
    id: 'e1',
    pathshalaId: PATHSHALA_ID,
    childProfileId: 'kid1',
    classId: null,
    className: null,
    sessionId: null,
    sessionLabel: null,
    status: 'Requested',
    requestedAt: '2026-09-01T10:00:00Z',
    enrolledAt: null,
    ...overrides,
  };
}

describe('PathshalaDetailComponent', () => {
  let fixture: ComponentFixture<PathshalaDetailComponent>;
  let component: PathshalaDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PathshalaDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => PATHSHALA_ID } } },
        },
      ],
    });

    fixture = TestBed.createComponent(PathshalaDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(
    school: PathshalaDetail = detail(),
    requests: Enrolment[] = [],
    names: { id: string; fullName: string }[] = [],
  ) {
    fixture.detectChanges();
    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}`).flush(school);
    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}/enrollments/requests`).flush(requests);

    if (requests.length > 0) {
      const ids = [...new Set(requests.map((r) => r.childProfileId))];
      http.expectOne(`/v1/children/names?ids=${ids.join(',')}`).flush(names);
    }

    fixture.detectChanges();
  }

  it('says nobody is waiting rather than showing an empty table', () => {
    load();

    expect(text()).toContain('Nobody is waiting');
  });

  it('puts the child’s name against a request', () => {
    load(detail(), [request()], [{ id: 'kid1', fullName: 'Diya Jain' }]);

    expect(text()).toContain('Diya Jain');
  });

  it('says "A child" rather than printing an id it could not resolve', () => {
    // Names come from a second service. A GUID on screen is no use to somebody
    // deciding which class to put a child in.
    load(detail(), [request()], []);

    expect(text()).toContain('A child');
    expect(text()).not.toContain('kid1');
  });

  it('asks for each child once, however many requests they have', () => {
    load(
      detail(),
      [request({ id: 'e1' }), request({ id: 'e2' })],
      [{ id: 'kid1', fullName: 'Diya Jain' }],
    );

    expect(text()).toContain('Diya Jain');
  });

  it('will not place a child until a class is chosen', () => {
    load(detail(), [request()], [{ id: 'kid1', fullName: 'Diya Jain' }]);

    component.place(request(), true);

    http.expectNone('/v1/pathshala/enrollments/e1/placement');
  });

  it('places a child into the chosen class', () => {
    load(detail(), [request()], [{ id: 'kid1', fullName: 'Diya Jain' }]);

    component.chosenClass['e1'] = 'c1';
    component.place(request(), true);

    const call = http.expectOne('/v1/pathshala/enrollments/e1/placement');

    expect(call.request.body).toEqual({ classId: 'c1', place: true });

    call.flush(request({ status: 'Active', classId: 'c1' }));
    reload();

    expect(text()).toContain('has a place');
  });

  it('turns a request down without needing a class', () => {
    // The service takes `place: false` with no class, and the screen must not
    // insist on one for a decision that does not use it.
    load(detail(), [request()], [{ id: 'kid1', fullName: 'Diya Jain' }]);

    component.place(request(), false);

    const call = http.expectOne('/v1/pathshala/enrollments/e1/placement');

    expect(call.request.body).toEqual({ classId: null, place: false });

    call.flush(request({ status: 'Withdrawn' }));
    reload();

    expect(text()).toContain('turned down');
  });

  it('offers no class form until a session is open', () => {
    // A class belongs to a session and the service refuses one without it.
    // Offering the form anyway would mean a 404 that reads as a bug.
    load(detail({ sessions: [], classes: [] }));

    expect(text()).toContain('Open a session first');
    expect(text()).not.toContain('Add a class to');
  });

  it('adds a class to the current session', () => {
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
    load();

    component.sessionLabel = '2027-28';
    component.sessionStart = '';
    component.sessionEnd = '2028-02-29';
    component.openSession();

    http.expectNone(`/v1/pathshala/pathshalas/${PATHSHALA_ID}/sessions`);
  });

  it('re-reads the Pathshala after every action rather than patching the screen', () => {
    // Placing a child changes a class's student count and empties a queue row;
    // opening a session changes which one is current. The server is the only
    // thing that knows all of it at once.
    load();

    component.sessionLabel = '2027-28';
    component.sessionStart = '2027-03-01';
    component.sessionEnd = '2028-02-29';
    component.openSession();

    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}/sessions`).flush(detail());
    reload();

    expect(text()).toContain('is now the current session');
  });

  it('explains a 404 as no such Pathshala rather than as an error', () => {
    fixture.detectChanges();
    http
      .expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such Pathshala');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  /** Answers the re-read every action fires. */
  function reload(school: PathshalaDetail = detail(), requests: Enrolment[] = []) {
    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}`).flush(school);
    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}/enrollments/requests`).flush(requests);
    fixture.detectChanges();
  }

  it('offers to stand a Pathshala down, behind a confirmation', () => {
    // Records are kept and enrolments stop, and it cannot be undone from this
    // panel — so the button opens a warning rather than firing.
    load();

    expect(text()).toContain('Stand down');
    http.expectNone(`/v1/pathshala/pathshalas/${PATHSHALA_ID}`);

    component.deactivating.set(true);
    fixture.detectChanges();

    expect(text()).toContain('cannot be undone');
    expect(text()).toContain('register and exam result is kept');
  });

  it('keeps the button that opened the confirmation, and announces the panel', () => {
    // Replacing the trigger destroys the focused element and drops a keyboard
    // user to the body (WCAG 2.4.3); disabling it does the same.
    load();

    const trigger = () =>
      [...fixture.nativeElement.querySelectorAll('button')].find(
        (b) => (b as HTMLElement).textContent?.trim() === 'Stand down…',
      ) as HTMLButtonElement | undefined;

    expect(trigger()!.getAttribute('aria-expanded')).toBe('false');

    component.deactivating.set(true);
    fixture.detectChanges();

    expect(trigger()).toBeTruthy();
    expect(trigger()!.disabled).toBe(false);
    expect(trigger()!.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelector('.notice[role="status"]')).toBeTruthy();
  });

  it('stands it down once confirmed', () => {
    load();

    component.deactivate();

    const call = http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}`);

    expect(call.request.method).toBe('DELETE');

    call.flush({});
    reload();

    expect(text()).toContain('Its records are kept');
  });

  it('offers nothing to stand down on one already stood down', () => {
    load(detail({ status: 'Inactive' }));

    expect(text()).toContain('has been stood down');
    expect(text()).not.toContain('Stand down…');
  });
}); 
