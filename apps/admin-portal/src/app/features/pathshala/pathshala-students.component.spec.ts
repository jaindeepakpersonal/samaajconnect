import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_CONFIG } from '@samaajconnect/shared';
import { PathshalaStudentsComponent } from './pathshala-students.component';
import { PathshalaClass, PathshalaStudent } from '../../core/admin.models';

const PATHSHALA_ID = 'p1';
const STUDENTS = `/v1/pathshala/pathshalas/${PATHSHALA_ID}/students`;

function student(overrides: Partial<PathshalaStudent> = {}): PathshalaStudent {
  return {
    enrolmentId: 'e1',
    childProfileId: 'kid1',
    status: 'Active',
    classId: 'c1',
    className: 'Class 8',
    sessionLabel: '2026-27',
    parentMemberId: 'm1',
    contactName: 'Sunita Jain',
    contactPhone: '9829012345',
    requestedAt: '2026-09-01T10:00:00Z',
    enrolledAt: '2026-09-02T10:00:00Z',
    ...overrides,
  };
}

function klass(id: string, name: string): PathshalaClass {
  return {
    id,
    sessionId: 's1',
    sessionLabel: '2026-27',
    name,
    roomLabel: null,
    schedule: [],
    teacherIds: [],
    studentCount: 0,
  };
}

describe('PathshalaStudentsComponent', () => {
  let fixture: ComponentFixture<PathshalaStudentsComponent>;
  let component: PathshalaStudentsComponent;
  let http: HttpTestingController;

  const text = () => fixture.nativeElement.textContent as string;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PathshalaStudentsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PathshalaStudentsComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => http.verify());

  function load(
    found: PathshalaStudent[],
    names: { id: string; fullName: string }[] = [],
    canManage = true,
  ) {
    fixture.componentRef.setInput('pathshalaId', PATHSHALA_ID);
    fixture.componentRef.setInput('classes', [klass('c1', 'Class 8'), klass('c2', 'Class 9')]);
    fixture.componentRef.setInput('canManage', canManage);
    fixture.detectChanges();
    http.expectOne(STUDENTS).flush(found);

    if (found.length > 0) {
      const ids = [...new Set(found.map((s) => s.childProfileId))];
      http.expectOne(`/v1/children/names?ids=${ids.join(',')}`).flush(names);
    }

    fixture.detectChanges();
  }

  it('shows each child with the parent to call', () => {
    load([student()], [{ id: 'kid1', fullName: 'Diya Jain' }]);

    expect(text()).toContain('Diya Jain');
    expect(text()).toContain('Sunita Jain');

    const phone = fixture.nativeElement.querySelector('a[href^="tel:"]') as HTMLAnchorElement;

    expect(phone.getAttribute('href')).toBe('tel:9829012345');
  });

  it('says a request made before the form asked has no contact, rather than a blank', () => {
    load([student({ contactName: null, contactPhone: null })]);

    expect(text()).toContain('asked before the form did');
  });

  it('says "A child" rather than printing an id it could not resolve', () => {
    load([student()], []);

    expect(text()).toContain('A child');
    expect(text()).not.toContain('kid1');
  });

  it('puts waiting requests in the queue and placed children in the list', () => {
    load(
      [
        student({ enrolmentId: 'e1', childProfileId: 'kid1' }),
        student({ enrolmentId: 'e2', childProfileId: 'kid2', status: 'Requested', classId: null, className: null }),
        student({ enrolmentId: 'e3', childProfileId: 'kid3', status: 'Withdrawn' }),
      ],
      [],
    );

    expect(component.waiting().map((s) => s.enrolmentId)).toEqual(['e2']);
    expect(component.enrolled().map((s) => s.enrolmentId)).toEqual(['e1']);
    expect(text()).toContain('1 more child has left or been turned down');
  });

  it('asks for each child’s name once, however many enrolments they have', () => {
    load([student({ enrolmentId: 'e1' }), student({ enrolmentId: 'e2', status: 'Withdrawn' })], [
      { id: 'kid1', fullName: 'Diya Jain' },
    ]);

    expect(text()).toContain('Diya Jain');
  });

  it('shows a teacher no placement queue', () => {
    load([student({ status: 'Requested', classId: null })], [], false);

    expect(text()).not.toContain('Waiting for a place');
  });

  it('will not place a child until a class is chosen', () => {
    const request = student({ status: 'Requested', classId: null });
    load([request]);

    component.place(request, true);

    http.expectNone('/v1/pathshala/enrollments/e1/placement');
  });

  it('places a child into the chosen class, and tells the page the counts changed', () => {
    const request = student({ status: 'Requested', classId: null });
    load([request], [{ id: 'kid1', fullName: 'Diya Jain' }]);

    let changed = 0;
    component.changed.subscribe(() => changed++);

    component.chosenClass['e1'] = 'c2';
    component.place(request, true);

    const call = http.expectOne('/v1/pathshala/enrollments/e1/placement');

    expect(call.request.body).toEqual({ classId: 'c2', place: true });

    call.flush({});
    http.expectOne(STUDENTS).flush([student({ classId: 'c2' })]);
    http.expectOne('/v1/children/names?ids=kid1').flush([{ id: 'kid1', fullName: 'Diya Jain' }]);
    fixture.detectChanges();

    expect(text()).toContain('Diya Jain has a place');
    expect(changed).toBe(1);
  });

  it('turns a request down without needing a class', () => {
    const request = student({ status: 'Requested', classId: null });
    load([request]);

    component.place(request, false);

    const call = http.expectOne('/v1/pathshala/enrollments/e1/placement');

    expect(call.request.body).toEqual({ classId: null, place: false });

    call.flush({});
    http.expectOne(STUDENTS).flush([]);
  });

  it('narrows the list to one class', () => {
    load([
      student({ enrolmentId: 'e1', childProfileId: 'kid1', classId: 'c1' }),
      student({ enrolmentId: 'e2', childProfileId: 'kid2', classId: 'c2' }),
    ]);

    component.classFilter.set('c2');

    expect(component.shown().map((s) => s.enrolmentId)).toEqual(['e2']);
  });
});
