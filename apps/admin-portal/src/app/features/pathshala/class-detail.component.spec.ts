import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { ClassDetailComponent } from './class-detail.component';
import { ClassExam, Enrolment, PathshalaDetail, RegisterEntry } from '../../core/admin.models';

const PATHSHALA_ID = 'p1';
const CLASS_ID = 'c1';

function detail(overrides: Partial<PathshalaDetail> = {}): PathshalaDetail {
  return {
    id: PATHSHALA_ID,
    name: 'Shri Mahavir Jain Pathshala',
    address: 'Hiran Magri',
    contactPerson: null,
    status: 'Active',
    acceptsEnrolments: true,
    sessions: [
      { id: 's1', label: '2026-27', startDate: '2026-03-01', endDate: '2027-02-28', isCurrent: true },
    ],
    classes: [
      {
        id: CLASS_ID,
        sessionId: 's1',
        sessionLabel: '2026-27',
        name: 'Class 8 — Jain Studies',
        roomLabel: 'Room 2',
        schedule: [],
        teacherMemberIds: [],
        studentCount: 2,
      },
    ],
    ...overrides,
  };
}

function student(id: string, childProfileId: string): Enrolment {
  return {
    id,
    pathshalaId: PATHSHALA_ID,
    childProfileId,
    classId: CLASS_ID,
    className: 'Class 8 — Jain Studies',
    sessionId: 's1',
    sessionLabel: '2026-27',
    status: 'Active',
    requestedAt: '2026-09-01T10:00:00Z',
    enrolledAt: '2026-09-01T11:00:00Z',
  };
}

describe('ClassDetailComponent', () => {
  let fixture: ComponentFixture<ClassDetailComponent>;
  let component: ClassDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ClassDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => (key === 'classId' ? CLASS_ID : PATHSHALA_ID),
              },
            },
          },
        },
      ],
    });

    fixture = TestBed.createComponent(ClassDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  /** Today, which is what the register date defaults to. */
  const date = () => new Date().toISOString().slice(0, 10);

  function load(
    school: PathshalaDetail = detail(),
    roll: Enrolment[] = [],
    register: RegisterEntry[] = [],
    exams: ClassExam[] = [],
    names: { id: string; fullName: string }[] = [],
    members: { id: string; fullName: string }[] = [],
  ) {
    fixture.detectChanges();
    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}`).flush(school);

    if (school.classes.some((c) => c.id === CLASS_ID)) {
      http.expectOne(`/v1/pathshala/classes/${CLASS_ID}/exams`).flush(exams);
      http.expectOne('/v1/members?limit=100').flush(members);
      http.expectOne(`/v1/pathshala/classes/${CLASS_ID}/roll`).flush(roll);

      if (roll.length > 0) {
        const ids = [...new Set(roll.map((s) => s.childProfileId))];
        http.expectOne(`/v1/children/names?ids=${ids.join(',')}`).flush(names);
      }

      http
        .expectOne(`/v1/pathshala/classes/${CLASS_ID}/register?date=${date()}`)
        .flush(register);
    }

    fixture.detectChanges();
  }

  it('explains a class that is not in this Pathshala rather than showing an error', () => {
    load(detail({ classes: [] }));

    expect(text()).toContain('No such class');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('says a class nobody teaches cannot be marked at all', () => {
    // Holding the attendance permission is not enough — the service checks that
    // the caller teaches this class. An empty teacher list is therefore a real
    // blocker, not a cosmetic gap.
    load();

    expect(text()).toContain('Nobody teaches this class yet');
  });

  it('fills the register from the marks already recorded', () => {
    // The submission amends what it names and leaves the rest alone, so the
    // form must start from what is actually recorded. This is the endpoint the
    // platform did not have until this screen needed it.
    load(
      detail(),
      [student('e1', 'kid1'), student('e2', 'kid2')],
      [{ enrolmentId: 'e1', status: 'Absent', markedAt: '2026-09-01T09:00:00Z' }],
      [],
      [
        { id: 'kid1', fullName: 'Diya Jain' },
        { id: 'kid2', fullName: 'Aarav Jain' },
      ],
    );

    expect(component.marks['e1']).toBe('Absent');
    expect(component.marks['e2']).toBe('');
    expect(text()).toContain('1 of 2 already marked');
  });

  it('does not submit a child nobody marked', () => {
    // The one rule on this screen that would silently corrupt a child's record
    // if it were wrong: defaulting an unmarked child to Present invents
    // attendance, and every attendance number this platform reports is a count
    // over exactly these rows.
    load(
      detail(),
      [student('e1', 'kid1'), student('e2', 'kid2')],
      [],
      [],
      [{ id: 'kid1', fullName: 'Diya Jain' }],
    );

    component.marks['e1'] = 'Present';

    expect(component.toSubmit()).toEqual([{ enrolmentId: 'e1', status: 'Present' }]);

    component.submitRegister();

    const call = http.expectOne(`/v1/pathshala/classes/${CLASS_ID}/attendance`);

    expect(call.request.body).toEqual({
      classDate: date(),
      marks: [{ enrolmentId: 'e1', status: 'Present' }],
    });

    call.flush({});
    reload();
  });

  it('will not submit a register in which nothing was marked', () => {
    load(detail(), [student('e1', 'kid1')]);

    component.submitRegister();

    http.expectNone(`/v1/pathshala/classes/${CLASS_ID}/attendance`);
  });

  it('re-reads the register when the date changes', () => {
    load(detail(), [student('e1', 'kid1')]);

    component.chooseDate('2026-03-08');

    http
      .expectOne(`/v1/pathshala/classes/${CLASS_ID}/register?date=2026-03-08`)
      .flush([{ enrolmentId: 'e1', status: 'Excused', markedAt: '2026-03-08T09:00:00Z' }]);
    fixture.detectChanges();

    expect(component.marks['e1']).toBe('Excused');
  });

  it('offers Amend rather than Record where a mark already exists', () => {
    const exam: ClassExam = {
      id: 'x1',
      classId: CLASS_ID,
      title: 'Half-yearly',
      examDate: '2026-09-06',
      maxScore: 50,
      results: [
        { enrolmentId: 'e1', score: 41, grade: 'A', recordedAt: '2026-09-07T09:00:00Z' },
      ],
    };

    load(
      detail(),
      [student('e1', 'kid1'), student('e2', 'kid2')],
      [],
      [exam],
      [
        { id: 'kid1', fullName: 'Diya Jain' },
        { id: 'kid2', fullName: 'Aarav Jain' },
      ],
    );

    // Re-recording amends silently, so which of the two a click will do is the
    // thing a teacher needs to know before they type.
    expect(component.recorded(exam, student('e1', 'kid1'))).toBe(true);
    expect(component.recorded(exam, student('e2', 'kid2'))).toBe(false);

    // The score already recorded is in the box, not a blank waiting to be
    // guessed at.
    expect(component.scores['x1:e1']).toBe(41);
    expect(text()).toContain('Amend');
  });

  it('records a mark against the exam and the child', () => {
    const exam: ClassExam = {
      id: 'x1',
      classId: CLASS_ID,
      title: 'Half-yearly',
      examDate: '2026-09-06',
      maxScore: 50,
      results: [],
    };

    load(detail(), [student('e1', 'kid1')], [], [exam], [{ id: 'kid1', fullName: 'Diya Jain' }]);

    component.scores['x1:e1'] = 44;
    component.grades['x1:e1'] = ' A ';
    component.recordResult(exam, student('e1', 'kid1'));

    const call = http.expectOne('/v1/pathshala/exams/x1/results');

    expect(call.request.body).toEqual({ enrolmentId: 'e1', score: 44, grade: 'A' });

    call.flush({});
    reload();
  });

  it('sends no grade rather than an empty one', () => {
    // The service stores null for "no grade". An empty string would make "no
    // grade" and "a grade called nothing" two different states.
    const exam: ClassExam = {
      id: 'x1',
      classId: CLASS_ID,
      title: 'Half-yearly',
      examDate: '2026-09-06',
      maxScore: 50,
      results: [],
    };

    load(detail(), [student('e1', 'kid1')], [], [exam], [{ id: 'kid1', fullName: 'Diya Jain' }]);

    component.scores['x1:e1'] = 44;
    component.recordResult(exam, student('e1', 'kid1'));

    const call = http.expectOne('/v1/pathshala/exams/x1/results');

    expect(call.request.body).toEqual({ enrolmentId: 'e1', score: 44, grade: null });

    call.flush({});
    reload();
  });

  it('will not set an exam without a title, a date and a mark it is out of', () => {
    load();

    component.examTitle = 'Half-yearly';
    component.examDate = '2026-09-06';
    component.examMax = null;
    component.scheduleExam();

    http.expectNone(`/v1/pathshala/classes/${CLASS_ID}/exams`);
  });

  it('says withdrawing keeps the record rather than deleting it', () => {
    load(detail(), [student('e1', 'kid1')], [], [], [{ id: 'kid1', fullName: 'Diya Jain' }]);

    component.withdraw(student('e1', 'kid1'));

    const call = http.expectOne('/v1/pathshala/enrollments/e1');

    expect(call.request.method).toBe('DELETE');

    call.flush({});
    reload();

    expect(text()).toContain('Their record is kept');
  });

  it('assigns a teacher and offers only members who are not already teaching it', () => {
    load(
      detail({
        classes: [{ ...detail().classes[0]!, teacherMemberIds: ['m1'] }],
      }),
      [],
      [],
      [],
      [],
      [
        { id: 'm1', fullName: 'Smt. Kavita Jain' },
        { id: 'm2', fullName: 'Shri Rajesh Jain' },
      ],
    );

    expect(component.assignable().map((m) => m.id)).toEqual(['m2']);

    component.setTeacher('m2', true);

    const call = http.expectOne(`/v1/pathshala/classes/${CLASS_ID}/teachers`);

    expect(call.request.body).toEqual({ teacherMemberId: 'm2', assign: true });

    call.flush({});
    reload();
  });

  it('says "A child" rather than printing an id it could not resolve', () => {
    load(detail(), [student('e1', 'kid1')], [], [], []);

    expect(text()).toContain('A child');
    expect(text()).not.toContain('kid1');
  });

  /** Answers the re-read every action fires. */
  function reload() {
    http.expectOne(`/v1/pathshala/pathshalas/${PATHSHALA_ID}`).flush(detail());
    http.expectOne(`/v1/pathshala/classes/${CLASS_ID}/exams`).flush([]);
    http.expectOne('/v1/members?limit=100').flush([]);
    http.expectOne(`/v1/pathshala/classes/${CLASS_ID}/roll`).flush([]);
    http.expectOne(`/v1/pathshala/classes/${CLASS_ID}/register?date=${date()}`).flush([]);
    fixture.detectChanges();
  }
});
