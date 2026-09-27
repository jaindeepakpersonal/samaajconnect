import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { Child } from '../members/members.models';
import { EnrolmentComponent } from './enrolment.component';
import { PathshalaListComponent } from './pathshala-list.component';
import {
  Enrolment,
  MyAttendance,
  MyClass,
  MyExam,
  MyProgress,
  Pathshala,
  PathshalaAnnouncement,
} from './pathshala.models';

const ENROLMENTS = '/v1/pathshala/enrollments';
const ANNOUNCEMENTS = '/v1/pathshala/announcements';
const PATHSHALAS = '/v1/pathshala/pathshalas';
const CHILDREN = '/v1/children';

function pathshala(overrides: Partial<Pathshala> = {}): Pathshala {
  return {
    id: 'p1',
    name: 'Shri Mahavir Jain Pathshala',
    address: { line: '12 Temple Road', countryId: null, stateId: null, cityId: null, localityId: null },
    contactPerson: null,
    status: 'Active',
    currentSessionLabel: '2026-27',
    currentSessionId: 's1',
    classCount: 8,
    teacherCount: 3,
    acceptsEnrolments: true,
    ...overrides,
  };
}

function enrolment(overrides: Partial<Enrolment> = {}): Enrolment {
  return {
    id: 'e1',
    pathshalaId: 'p1',
    childProfileId: 'child1',
    classId: 'c1',
    className: 'Class 8 - Jain Studies',
    sessionId: 's1',
    sessionLabel: '2026-27',
    status: 'Active',
    requestedAt: '2026-06-01T00:00:00Z',
    enrolledAt: '2026-06-08T00:00:00Z',
    ...overrides,
  };
}

function child(overrides: Partial<Child> = {}): Child {
  return {
    id: 'child1',
    familyId: 'f1',
    fullName: 'Aarav Jain',
    dateOfBirth: '2015-04-02',
    age: 11,
    gender: 'Male',
    photoUrl: null,
    status: 'Active',
    isEligibleForConversion: false,
    hasPendingConversion: false,
    createdAt: '2026-01-01T00:00:00Z',
    parentalConsent: null,
    ...overrides,
  } as Child;
}

function myClass(overrides: Partial<MyClass> = {}): MyClass {
  return {
    enrolmentId: 'e1',
    pathshalaId: 'p1',
    pathshalaName: 'Shri Mahavir Jain Pathshala',
    classId: 'c1',
    className: 'Class 8 - Jain Studies',
    roomLabel: 'Room 2',
    sessionLabel: '2026-27',
    schedule: [{ dayOfWeek: 'Sunday', startTime: '10:00:00', endTime: '11:30:00' }],
    teacherNames: ['Smt. Kavita Jain'],
    classmateCount: 24,
    ...overrides,
  };
}

function attendance(overrides: Partial<MyAttendance> = {}): MyAttendance {
  return {
    enrolmentId: 'e1',
    percentage: 92,
    present: 46,
    absent: 4,
    excused: 0,
    days: [{ classDate: '2026-08-16', status: 'Present' }],
    ...overrides,
  };
}

function progress(overrides: Partial<MyProgress> = {}): MyProgress {
  return {
    enrolmentId: 'e1',
    sessionLabel: '2026-27',
    attendancePercentage: 92,
    present: 46,
    absent: 4,
    excused: 0,
    examsSat: 1,
    averageScorePercentage: 88,
    ...overrides,
  };
}

function exam(overrides: Partial<MyExam> = {}): MyExam {
  return {
    examId: 'x1',
    title: 'Jain History',
    examDate: '2026-08-10',
    maxScore: 50,
    status: 'Completed',
    score: 44,
    grade: null,
    ...overrides,
  };
}

describe('PathshalaListComponent', () => {
  let fixture: ComponentFixture<PathshalaListComponent>;
  let component: PathshalaListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PathshalaListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(PathshalaListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(options: {
    pathshalas?: Pathshala[];
    enrolments?: Enrolment[];
    children?: Child[];
    announcements?: PathshalaAnnouncement[];
  }): void {
    fixture.detectChanges();

    http.expectOne(PATHSHALAS).flush(options.pathshalas ?? [pathshala()]);
    http.expectOne(ENROLMENTS).flush(options.enrolments ?? []);
    http.expectOne(CHILDREN).flush(options.children ?? [child()]);
    http.expectOne(ANNOUNCEMENTS).flush(options.announcements ?? []);

    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  // ---- Where it is ---------------------------------------------------------
  //
  // The service sends ids, never names, so the directory resolves them once from
  // master-data-service rather than one lookup per tile.

  const located = () =>
    pathshala({
      address: { line: '12 Temple Road', countryId: 'IN', stateId: 'RJ', cityId: 'UDR', localityId: 'HM' },
    });

  function settleLocation(): void {
    http.expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'Country')
      .flush([{ id: 'IN', category: 'Country', code: 'IN', label: 'India', sortOrder: 1, isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/states')
      .flush([{ id: 'RJ', countryId: 'IN', name: 'Rajasthan', isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/cities')
      .flush([{ id: 'UDR', countryId: 'IN', stateId: 'RJ', name: 'Udaipur', isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/localities')
      .flush([{ id: 'HM', cityId: 'UDR', name: 'Hiran Magri', isActive: true }]);
    fixture.detectChanges();
  }

  it('shows the address line, locality, city, state and country on the tile', () => {
    load({ pathshalas: [located()] });
    settleLocation();

    expect(text()).toContain('12 Temple Road, Hiran Magri, Udaipur, Rajasthan, India');
    expect(text()).not.toContain('[object Object]');
  });

  it('shows just the free-text line when no ids were picked, and asks master data for nothing', () => {
    load({});

    expect(text()).toContain('12 Temple Road');
    // http.verify() in afterEach is the assertion: no master-data request went out.
  });

  it('leaves out a place that does not resolve rather than printing its id', () => {
    load({ pathshalas: [located()] });

    http.expectOne((r) => r.params.get('category') === 'Country').flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/states').flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/cities')
      .flush([{ id: 'UDR', countryId: 'IN', stateId: 'RJ', name: 'Udaipur', isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('12 Temple Road, Udaipur');
    expect(text()).not.toContain('RJ');
  });

  it('still shows the directory when the location lists cannot be read', () => {
    load({ pathshalas: [located()] });

    for (const req of http.match((r) => r.url.startsWith('/v1/master-data/'))) {
      req.flush(null, { status: 500, statusText: 'Error' });
    }
    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(text()).toContain('12 Temple Road');
  });

  it('names the child rather than printing their id', () => {
    // The one place this app does resolve a name: a parent's own children are
    // one call to a list they already have, and "Waiting for a place" against
    // an opaque id would be useless to the only person the screen is for.
    load({ enrolments: [enrolment({ status: 'Requested', classId: null, className: null })] });

    expect(text()).toContain('Aarav Jain');
    expect(text()).not.toContain('child1');
  });

  it('tells waiting for a place apart from being enrolled', () => {
    load({ enrolments: [enrolment({ status: 'Requested', classId: null, className: null })] });

    expect(text()).toContain('Waiting for a place');
    expect(text()).toContain('has not placed them in a class yet');
  });

  it('shows the class once a child has been placed', () => {
    load({ enrolments: [enrolment()] });

    expect(text()).toContain('Enrolled');
    expect(text()).toContain('Class 8 - Jain Studies');
  });

  it('does not offer a child who already has a live place', () => {
    // The service refuses a second live enrolment, so offering it would be
    // offering a 409.
    load({ enrolments: [enrolment({ childProfileId: 'child1', status: 'Active' })] });

    expect(component.enrollable()).toEqual([]);
    expect(text()).toContain('already been put forward');
  });

  it('offers a child again once a place has been withdrawn', () => {
    load({ enrolments: [enrolment({ childProfileId: 'child1', status: 'Withdrawn' })] });

    expect(component.enrollable().map((c) => c.id)).toEqual(['child1']);
  });

  it('disables enrolling at a Pathshala with no open session', () => {
    load({ pathshalas: [pathshala({ acceptsEnrolments: false, currentSessionLabel: null })] });

    const button = fixture.nativeElement.querySelector('button[disabled]') as HTMLButtonElement;

    expect(button).not.toBeNull();
    expect(button.getAttribute('title')).toContain('no session open');
  });

  /** Picks a child the way the select does, which is what fetches the profile. */
  function pickChild(me: { fullName: string; mobile: string | null } | null) {
    component.chooseChild('p1', 'child1');

    const profile = http.expectOne('/v1/members/me');

    if (me === null) {
      profile.flush(null, { status: 404, statusText: 'Not Found' });
    } else {
      profile.flush(me);
    }

    fixture.detectChanges();
  }

  it('asks for a place with the contact the Pathshala will call, and says it decides the class', () => {
    load({});
    pickChild({ fullName: 'Sunita Jain', mobile: '9829012345' });

    component.contactPhone = ' 98290 11111 ';
    component.enrol(pathshala());

    const request = http.expectOne(`${PATHSHALAS}/p1/enrollments`);

    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      childProfileId: 'child1',
      contactName: 'Sunita Jain',
      contactPhone: '98290 11111',
    });

    request.flush(enrolment({ status: 'Requested', classId: null }));

    // The screen re-reads rather than patching.
    http.expectOne(PATHSHALAS).flush([pathshala()]);
    http.expectOne(ENROLMENTS).flush([enrolment({ status: 'Requested', classId: null })]);
    http.expectOne(CHILDREN).flush([child()]);
    http.expectOne(ANNOUNCEMENTS).flush([]);
    fixture.detectChanges();

    expect(text()).toContain('The Pathshala places them in a class');
  });

  it('starts the contact on the member’s own name and mobile, and says who sees it', () => {
    load({});
    pickChild({ fullName: 'Sunita Jain', mobile: '9829012345' });

    expect(component.contactName).toBe('Sunita Jain');
    expect(component.contactPhone).toBe('9829012345');
    expect(text()).toContain('whatever your directory privacy setting');
  });

  it('asks for the profile once, however many children are picked', () => {
    load({});
    pickChild({ fullName: 'Sunita Jain', mobile: null });

    component.chooseChild('p1', '');
    component.chooseChild('p1', 'child1');

    // http.verify() in afterEach: no second /members/me.
    expect(component.contactPhone).toBe('');
  });

  it('will not ask for a place without a phone the service would accept', () => {
    load({});
    pickChild(null);

    component.contactName = 'Sunita Jain';

    for (const phone of ['', 'ring the office', '12']) {
      component.contactPhone = phone;

      expect(component.canEnrol(pathshala()), phone).toBe(false);
    }

    component.enrol(pathshala());
    http.expectNone(`${PATHSHALAS}/p1/enrollments`);
  });

  it('shows the Pathshala’s messages first, newest as sent', () => {
    load({
      announcements: [
        {
          id: 'a1',
          pathshalaId: 'p1',
          pathshalaName: 'Shri Mahavir Jain Pathshala',
          title: 'No class this Sunday',
          body: 'The hall is booked for Paryushan.',
          sentBy: 'u1',
          sentAt: '2026-09-20T10:00:00Z',
          recipientCount: 14,
        },
      ],
    });

    expect(text()).toContain('Messages from the Pathshala');
    expect(text()).toContain('No class this Sunday');
    expect(text()).toContain('The hall is booked for Paryushan.');
  });

  it('says nothing about messages when there are none, and survives failing to read them', () => {
    fixture.detectChanges();

    http.expectOne(PATHSHALAS).flush([pathshala()]);
    http.expectOne(ENROLMENTS).flush([]);
    http.expectOne(CHILDREN).flush([child()]);
    http.expectOne(ANNOUNCEMENTS).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(text()).not.toContain('Messages from the Pathshala');
    expect(text()).toContain('Shri Mahavir Jain Pathshala');
  });

  it('still shows the directory to a member with no family record', () => {
    // No children is a 404, not an empty list. That is not a reason to fail the
    // whole screen.
    fixture.detectChanges();

    http.expectOne(PATHSHALAS).flush([pathshala()]);
    http.expectOne(ENROLMENTS).flush([]);
    http.expectOne(CHILDREN).flush(null, { status: 404, statusText: 'Not Found' });
    http.expectOne(ANNOUNCEMENTS).flush([]);
    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(text()).toContain('Shri Mahavir Jain Pathshala');
  });

  it('says the Samaaj has no Pathshala rather than showing an empty grid', () => {
    load({ pathshalas: [] });

    expect(text()).toContain('has not set up a Pathshala yet');
  });

  it('starts each child picker on its placeholder rather than on nothing', () => {
    // An undefined model matches no option at all, not even the one whose value
    // is the empty string, so the select renders with selectedIndex -1 and
    // reads as an empty dropdown. Only visible by opening the page.
    load({});

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;

    expect(component.chosenChild['p1']).toBe('');
    expect(select.selectedIndex).toBe(0);
    expect(select.options[0]!.textContent).toContain('Choose a child');
  });
});

describe('EnrolmentComponent', () => {
  let fixture: ComponentFixture<EnrolmentComponent>;
  let component: EnrolmentComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EnrolmentComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', 'e1']]) } },
        },
      ],
    });

    fixture = TestBed.createComponent(EnrolmentComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function loadPlaced(views: {
    myClass?: MyClass;
    attendance?: MyAttendance;
    exams?: MyExam[];
    progress?: MyProgress;
    place?: Enrolment;
  }): void {
    fixture.detectChanges();

    http.expectOne(ENROLMENTS).flush([views.place ?? enrolment()]);

    http.expectOne(`${ENROLMENTS}/e1/my-class`).flush(views.myClass ?? myClass());
    http.expectOne(`${ENROLMENTS}/e1/attendance`).flush(views.attendance ?? attendance());
    http.expectOne(`${ENROLMENTS}/e1/exams`).flush(views.exams ?? [exam()]);
    http.expectOne(`${ENROLMENTS}/e1/progress`).flush(views.progress ?? progress());

    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  it('does not ask for a class the enrolment has not got', () => {
    // my-class answers 409 by design while a child is waiting. Asking anyway
    // would put an expected error on every visit to a waiting enrolment.
    fixture.detectChanges();

    http.expectOne(ENROLMENTS).flush([
      enrolment({ status: 'Requested', classId: null, className: null }),
    ]);
    fixture.detectChanges();

    // http.verify() in afterEach is what proves the four view calls were not
    // made; this is the visible half.
    expect(text()).toContain('You have asked for a place');
  });

  it('shows the class, its room and its timetable', () => {
    loadPlaced({});

    expect(text()).toContain('Class 8 - Jain Studies');
    expect(text()).toContain('Room 2');
    expect(text()).toContain('Sunday');
    expect(text()).toContain('10:00 – 11:30');
  });

  it('names the class’s teachers, from the class rather than the directory', () => {
    loadPlaced({ myClass: myClass({ teacherNames: ['Smt. Kavita Jain', 'Pt. Suresh Shastri'] }) });

    expect(text()).toContain('Teachers: Smt. Kavita Jain, Pt. Suresh Shastri');
  });

  it('says no teacher has been assigned rather than an empty list', () => {
    loadPlaced({ myClass: myClass({ teacherNames: [] }) });

    expect(text()).toContain('No teacher has been assigned yet');
  });

  it('does not print a nil attendance as zero per cent', () => {
    // A child enrolled last week whose register has never been marked has no
    // percentage. 0% would tell their parent they had missed everything.
    loadPlaced({
      attendance: attendance({ percentage: null, present: 0, absent: 0, excused: 0, days: [] }),
      progress: progress({ attendancePercentage: null, present: 0, absent: 0, excused: 0 }),
    });

    expect(text()).toContain('Not yet');
    expect(text()).not.toContain('0%');
  });

  it('separates an exam awaiting its result from a completed one', () => {
    // The wireframe has two states. An exam sat last week and not yet marked is
    // neither, and showing it as Completed with a blank result reads as a zero.
    loadPlaced({
      exams: [exam({ examId: 'x2', status: 'AwaitingResult', score: null, grade: null })],
    });

    expect(text()).toContain('Awaiting result');
    expect(text()).not.toContain('Completed');
  });

  it('shows a mark out of the exam maximum, not out of a hundred', () => {
    loadPlaced({ exams: [exam({ score: 44, maxScore: 50 })] });

    expect(text()).toContain('44/50');
    expect(text()).toContain('88%');
  });

  it('prefers a grade over a computed percentage when one was recorded', () => {
    loadPlaced({ exams: [exam({ score: 44, maxScore: 50, grade: 'A' })] });

    expect(text()).toContain('44/50 • A');
  });

  it('names excused absences without gluing them onto the sentence badly', () => {
    loadPlaced({
      attendance: attendance({ present: 2, absent: 1, excused: 1, percentage: 67 }),
      progress: progress({ present: 2, absent: 1, excused: 1, attendancePercentage: 67 }),
    });

    expect(text()).toContain('Classes missed, and 1 excused');
    expect(text()).not.toContain('missed ,');
  });

  it('does not print a ratio that contradicts the attendance percentage', () => {
    // An excused day is marked but not counted against them, so 67% is 2 of 3
    // while 4 days were marked. "2 of 4" beside 67% reads as an error.
    loadPlaced({
      attendance: attendance({ present: 2, absent: 1, excused: 1, percentage: 67 }),
      progress: progress({ present: 2, absent: 1, excused: 1, attendancePercentage: 67 }),
    });

    expect(text()).toContain('2 present, 1 absent, 1 excused');
    expect(text()).not.toContain('2 of 4');
  });

  it('does not claim an average before anything has been marked', () => {
    loadPlaced({ progress: progress({ averageScorePercentage: null, examsSat: 0 }) });

    expect(text()).toContain('No exams sat so far');
  });

  it('does not print the wireframe events tile, which nothing can supply', () => {
    // The wireframe's progress screen shows "Events: 7 participated". Nothing
    // records Pathshala event participation, so the tile would be a number the
    // app made up.
    loadPlaced({});

    expect(text()).not.toContain('Participated');
    expect(text()).not.toContain('Events');
  });

  it('still shows the class when one of the other views fails', () => {
    fixture.detectChanges();

    http.expectOne(ENROLMENTS).flush([enrolment()]);
    http.expectOne(`${ENROLMENTS}/e1/my-class`).flush(myClass());
    http.expectOne(`${ENROLMENTS}/e1/attendance`).flush(attendance());
    http
      .expectOne(`${ENROLMENTS}/e1/exams`)
      .flush(null, { status: 500, statusText: 'Server Error' });
    http.expectOne(`${ENROLMENTS}/e1/progress`).flush(progress());
    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(text()).toContain('Class 8 - Jain Studies');
  });

  it('says so when the link names an enrolment that is not the reader’s', () => {
    fixture.detectChanges();

    http.expectOne(ENROLMENTS).flush([enrolment({ id: 'other' })]);
    fixture.detectChanges();

    expect(component.error()).toBe('No such enrolment.');
  });
});
