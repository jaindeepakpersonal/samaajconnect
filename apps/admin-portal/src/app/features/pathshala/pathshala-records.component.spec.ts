import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { PathshalaRecordsComponent, RecordsKind } from './pathshala-records.component';

const BASE = '/v1/pathshala/pathshalas/p1';

describe('PathshalaRecordsComponent', () => {
  let fixture: ComponentFixture<PathshalaRecordsComponent>;
  let http: HttpTestingController;

  const text = () => fixture.nativeElement.textContent as string;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PathshalaRecordsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PathshalaRecordsComponent);
  });

  afterEach(() => http.verify());

  function open(kind: RecordsKind) {
    fixture.componentRef.setInput('pathshalaId', 'p1');
    fixture.componentRef.setInput('kind', kind);
    fixture.detectChanges();
  }

  it('lists every exam with how far marking has got, linked to its class', () => {
    open('exams');
    http.expectOne(`${BASE}/exams`).flush([
      {
        examId: 'x1',
        classId: 'c1',
        className: 'Class 8',
        title: 'Mid-term',
        examDate: '2026-10-04',
        maxScore: 50,
        studentCount: 12,
        resultsRecorded: 9,
        averageScorePercentage: 71.25,
      },
    ]);
    fixture.detectChanges();

    expect(text()).toContain('Mid-term');
    expect(text()).toContain('9 of 12');
    expect(text()).toContain('71.3%');

    const link = fixture.nativeElement.querySelector('a[href="/pathshala/p1/classes/c1"]');

    expect(link).not.toBeNull();
  });

  it('shows nothing-to-count-yet as a dash, never as 0%', () => {
    // A class nobody has marked has no attendance, and "0%" would be a claim
    // about children nobody has said anything about.
    open('attendance');
    http.expectOne(`${BASE}/attendance`).flush([
      {
        classId: 'c1',
        className: 'Class 8',
        sessionLabel: '2026-27',
        studentCount: 12,
        daysMarked: 0,
        present: 0,
        absent: 0,
        excused: 0,
        percentage: null,
      },
    ]);
    fixture.detectChanges();

    expect(text()).toContain('—');
    expect(text()).not.toContain('0%');
  });

  it('names each child on the progress report, asking once for all of them', () => {
    open('progress');
    http.expectOne(`${BASE}/progress`).flush([
      {
        enrolmentId: 'e1',
        childProfileId: 'kid1',
        classId: 'c1',
        className: 'Class 8',
        present: 8,
        absent: 2,
        excused: 1,
        attendancePercentage: 80,
        examsSat: 1,
        averageScorePercentage: null,
      },
    ]);
    http.expectOne('/v1/children/names?ids=kid1').flush([{ id: 'kid1', fullName: 'Diya Jain' }]);
    fixture.detectChanges();

    expect(text()).toContain('Diya Jain');
    expect(text()).toContain('80%');
  });

  it('says so when there is nothing yet', () => {
    open('exams');
    http.expectOne(`${BASE}/exams`).flush([]);
    fixture.detectChanges();

    expect(text()).toContain('No exams set yet');
  });

  it('shows a refusal as an error rather than an empty report', () => {
    open('progress');
    http.expectOne(`${BASE}/progress`).flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(text()).not.toContain('Nobody is enrolled');
  });
});
