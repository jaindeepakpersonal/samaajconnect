import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { PathshalaApi } from './pathshala.api';
import {
  AttendanceDay,
  Enrolment,
  EnrolmentStatusLabels,
  ExamStatusLabels,
  MyAttendance,
  MyClass,
  MyExam,
  MyProgress,
  ScheduleSlot,
} from './pathshala.models';

/**
 * One child's Pathshala record: the wireframe's `#myclass`, `#attendance`,
 * `#exams` and `#progress` on one screen.
 *
 * The wireframe draws four screens that navigate between each other, and each
 * one is a card or a short table about the same enrolment. Splitting them into
 * four routes would mean four page loads to answer "how is my child getting
 * on", and the wireframe's own `#progress` already reprints the attendance
 * percentage from `#attendance`, which is what four screens about one thing
 * tends to produce. They are sections here, in the order a parent asks them.
 *
 * Three things this screen has to keep straight.
 *
 * **Waiting for a place is not an error.** An unplaced enrolment has no class,
 * so `my-class` answers 409 by design and `exams` answers with an empty list.
 * The screen asks for neither until the enrolment carries a `classId`, and says
 * plainly what is being waited for.
 *
 * **A null percentage is not zero.** A child with no marked register yet has no
 * attendance percentage; printing 0% would tell their parent they had missed
 * every class.
 *
 * **An exam has three states, not the wireframe's two.** One sat last week and
 * not yet marked is neither Upcoming nor Completed, and showing it as Completed
 * with an empty result column reads as a zero.
 *
 * The wireframe's progress screen also carries an "Events: 7 participated"
 * tile. Nothing on this platform records Pathshala event participation, so it
 * is not printed - a tile with no source behind it would be a number the app
 * made up.
 */
@Component({
  selector: 'app-enrolment',
  imports: [RouterLink],
  styleUrl: './pathshala.css',
  template: `
    <div class="pathshala-page">
      <a class="back" routerLink="/pathshala">‹ Back to Jain Pathshala</a>

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (enrolment(); as place) {
        <!-- Named by the class once there is one. Before that the screen knows
             the Pathshala only by id, and a bare "Pathshala" reads as a stub. -->
        <h1 class="page-title">{{ myClass()?.className ?? 'Your Pathshala place' }}</h1>
        <p class="subtitle">
          <span class="pill" [class]="pillClass(place)">{{ stage(place) }}</span>
          {{ myClass()?.pathshalaName ?? '' }}
        </p>

        <!-- Waiting to be placed ------------------------------------------ -->
        @if (place.classId === null) {
          <p class="notice info" role="status">
            @switch (place.status) {
              @case ('Requested') {
                You have asked for a place. The Pathshala decides which class, and the timetable,
                attendance and exams appear here once they have.
              }
              @case ('Declined') {
                The Pathshala did not offer a place this session.
              }
              @case ('Withdrawn') {
                This place has been given up.
              }
              @default {
                There is no class on this enrolment.
              }
            }
          </p>
        } @else {
          <!-- My Class --------------------------------------------------- -->
          @if (myClass(); as found) {
            <h2 class="section-heading">The class</h2>

            <div class="card">
              <p><b>Session</b> {{ found.sessionLabel }}</p>

              @if (found.roomLabel; as room) {
                <p><b>Room</b> {{ room }}</p>
              }

              <!-- The wireframe says "Teacher: Smt. Kavita Jain". Teachers are
                   member ids here; names live in member-family-service and a
                   name per teacher would be a call per teacher for somebody
                   the parent has not asked about. -->
              <p>{{ teachers(found) }}</p>
              <p>{{ classmates(found) }}</p>

              @if (found.schedule.length > 0) {
                <h3 class="section-heading">Timetable</h3>
                <ul class="slots">
                  @for (slot of found.schedule; track slot.dayOfWeek + slot.startTime) {
                    <li>
                      <span class="day">{{ slot.dayOfWeek }}</span>
                      <span class="time">{{ time(slot) }}</span>
                    </li>
                  }
                </ul>
              } @else {
                <p class="small">No timetable has been set for this class yet.</p>
              }
            </div>
          }

          <!-- My Attendance ---------------------------------------------- -->
          @if (attendance(); as marks) {
            <h2 class="section-heading">Attendance</h2>

            <div class="grid">
              <div class="card">
                <h2>Overall</h2>
                @if (marks.percentage === null) {
                  <!-- Null, not zero. Nothing has been marked yet. -->
                  <p class="stat unknown">Not yet</p>
                  <p class="small">No register has been marked for this class so far.</p>
                } @else {
                  <p class="stat">{{ marks.percentage }}%</p>
                  @if (progress()?.sessionLabel; as session) {
                    <p class="small">Academic session {{ session }}</p>
                  }
                }
              </div>

              <div class="card">
                <h2>Present</h2>
                <p class="stat">{{ marks.present }}</p>
                <p class="small">Classes attended</p>
              </div>

              <div class="card">
                <h2>Absent</h2>
                <p class="stat">{{ marks.absent }}</p>
                <!-- One expression rather than text with an @if inside it: the
                     block's own indentation becomes a space, which put a gap
                     before the comma. -->
                <p class="small">{{ absentNote(marks) }}</p>
              </div>
            </div>

            @if (marks.days.length > 0) {
              <div class="table-scroll">
                <table>
                  <caption class="sr-only">Every class day marked so far</caption>
                  <tr>
                    <th scope="col">Date</th>
                    <th scope="col">Marked</th>
                  </tr>
                  @for (day of marks.days; track day.classDate) {
                    <tr>
                      <td>{{ date(day.classDate) }}</td>
                      <td>
                        <span class="pill" [class]="attendanceClass(day)">{{ day.status }}</span>
                      </td>
                    </tr>
                  }
                </table>
              </div>
            }
          }

          <!-- My Exams --------------------------------------------------- -->
          <h2 class="section-heading">Exams</h2>

          @if (exams().length === 0) {
            <p class="small">No exams have been set for this class yet.</p>
          } @else {
            <div class="table-scroll">
              <table>
                <caption class="sr-only">Upcoming and completed examinations</caption>
                <tr>
                  <th scope="col">Exam</th>
                  <th scope="col">Date</th>
                  <th scope="col">Status</th>
                  <th scope="col">Result</th>
                </tr>
                @for (exam of exams(); track exam.examId) {
                  <tr>
                    <td>{{ exam.title }}</td>
                    <td>{{ date(exam.examDate) }}</td>
                    <td>
                      <span class="pill" [class]="examClass(exam)">{{ examStage(exam) }}</span>
                    </td>
                    <td>{{ result(exam) }}</td>
                  </tr>
                }
              </table>
            </div>
          }

          <!-- My Progress ------------------------------------------------ -->
          @if (progress(); as summary) {
            <h2 class="section-heading">Progress</h2>

            <div class="grid">
              <div class="card">
                <h2>Average score</h2>
                @if (summary.averageScorePercentage === null) {
                  <p class="stat unknown">Not yet</p>
                  <p class="small">
                    {{
                      summary.examsSat === 0
                        ? 'No exams sat so far.'
                        : 'Sat, but not marked yet.'
                    }}
                  </p>
                } @else {
                  <p class="stat">{{ rounded(summary.averageScorePercentage) }}%</p>
                  <p class="small">
                    Across {{ summary.examsSat }}
                    {{ summary.examsSat === 1 ? 'exam' : 'exams' }}
                  </p>
                }
              </div>

              <div class="card">
                <h2>Attendance</h2>
                @if (summary.attendancePercentage === null) {
                  <p class="stat unknown">Not yet</p>
                } @else {
                  <p class="stat">{{ summary.attendancePercentage }}%</p>
                  <!-- The three counts, not a ratio. An excused day is marked
                       but not counted against them, so "2 of 4" would sit
                       beside a 67% that is 2 of 3 and read as an error. -->
                  <p class="small">{{ attendanceNote(summary) }}</p>
                }
              </div>
            </div>
          }
        }
      }
    </div>
  `,
})
export class EnrolmentComponent implements OnInit {
  private readonly api = inject(PathshalaApi);
  private readonly route = inject(ActivatedRoute);

  readonly enrolment = signal<Enrolment | null>(null);
  readonly myClass = signal<MyClass | null>(null);
  readonly attendance = signal<MyAttendance | null>(null);
  readonly exams = signal<readonly MyExam[]>([]);
  readonly progress = signal<MyProgress | null>(null);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  /**
   * The enrolment first, then the views that depend on it having a class.
   *
   * There is no "get one enrolment" endpoint - `/enrollments` is the member's
   * whole list - so this reads the list and picks. That is also what tells the
   * screen whether asking for the class would be asking for a 409.
   */
  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name an enrolment.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.myEnrolments().subscribe({
      next: (enrolments) => {
        const found = enrolments.find((enrolment) => enrolment.id === id) ?? null;

        this.enrolment.set(found);

        if (found === null) {
          this.loading.set(false);
          this.error.set('No such enrolment.');
          return;
        }

        if (found.classId === null) {
          // Unplaced: my-class would answer 409 and exams an empty list. The
          // attendance and progress views are still meaningful - they answer
          // with zeroes and nulls rather than failing - but there is nothing
          // for them to say, so the screen shows the waiting notice instead.
          this.loading.set(false);
          return;
        }

        this.loadClassViews(found.id);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  /**
   * The four views, in parallel.
   *
   * Each falls back to null rather than failing the screen: a parent who can
   * see the class should still see it if the exam list happens to error.
   */
  private loadClassViews(enrolmentId: string): void {
    forkJoin({
      myClass: this.api.myClass(enrolmentId).pipe(catchError(() => of(null))),
      attendance: this.api.myAttendance(enrolmentId).pipe(catchError(() => of(null))),
      exams: this.api.myExams(enrolmentId).pipe(catchError(() => of([] as MyExam[]))),
      progress: this.api.myProgress(enrolmentId).pipe(catchError(() => of(null))),
    }).subscribe({
      next: ({ myClass, attendance, exams, progress }) => {
        this.myClass.set(myClass);
        this.attendance.set(attendance);
        this.exams.set(exams);
        this.progress.set(progress);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  // ---- Rendering ---------------------------------------------------------

  stage(enrolment: Enrolment): string {
    return EnrolmentStatusLabels[enrolment.status];
  }

  pillClass(enrolment: Enrolment): string {
    switch (enrolment.status) {
      case 'Active':
        return 'ok';
      case 'Requested':
        return 'warn';
      case 'Declined':
        return 'danger';
      case 'Withdrawn':
        return '';
    }
  }

  /** A count, because the ids behind it are names this app cannot resolve. */
  teachers(found: MyClass): string {
    const count = found.teacherMemberIds.length;

    return count === 0
      ? 'No teacher has been assigned yet'
      : `${count} ${count === 1 ? 'teacher' : 'teachers'}`;
  }

  classmates(found: MyClass): string {
    const count = found.classmateCount;

    return `${count} ${count === 1 ? 'student' : 'students'} in the class`;
  }

  time(slot: ScheduleSlot): string {
    return `${this.clock(slot.startTime)} – ${this.clock(slot.endTime)}`;
  }

  /**
   * The line under the absent count.
   *
   * An excused absence is still an absence the parent should see, but it is
   * deliberately not counted against the percentage - the domain calls it
   * "absent, but not counted against them" - so it is named here rather than
   * folded into the number above.
   */
  absentNote(marks: MyAttendance): string {
    return marks.excused === 0
      ? 'Classes missed'
      : `Classes missed, and ${marks.excused} excused`;
  }

  /**
   * The line under the attendance percentage on the progress tile.
   *
   * The three counts rather than a ratio: excused days are marked but excluded
   * from the percentage, so "2 of 4" printed beside a percentage computed as
   * 2 of 3 reads as an arithmetic error rather than as the policy it is.
   */
  attendanceNote(summary: MyProgress): string {
    const parts = [`${summary.present} present`, `${summary.absent} absent`];

    if (summary.excused > 0) {
      parts.push(`${summary.excused} excused`);
    }

    return parts.join(', ');
  }

  attendanceClass(day: AttendanceDay): string {
    switch (day.status) {
      case 'Present':
        return 'ok';
      case 'Absent':
        return 'danger';
      case 'Excused':
        return 'warn';
    }
  }

  examStage(exam: MyExam): string {
    return ExamStatusLabels[exam.status];
  }

  examClass(exam: MyExam): string {
    switch (exam.status) {
      case 'Completed':
        return 'ok';
      case 'AwaitingResult':
        return 'warn';
      case 'Upcoming':
        return '';
    }
  }

  /**
   * The wireframe's "88%" - out of the exam's own maximum, not out of 100.
   *
   * A dash for an exam with no score, whether that is because it has not been
   * sat or because it has not been marked. The status column beside it is what
   * says which, so the dash does not have to.
   */
  result(exam: MyExam): string {
    if (exam.score === null) {
      return '—';
    }

    const percentage = exam.maxScore > 0 ? Math.round((exam.score / exam.maxScore) * 100) : null;
    const mark = `${exam.score}/${exam.maxScore}`;

    if (exam.grade !== null) {
      return `${mark} • ${exam.grade}`;
    }

    return percentage === null ? mark : `${mark} (${percentage}%)`;
  }

  rounded(value: number): number {
    return Math.round(value);
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }

  /** `TimeOnly` serialises as `HH:mm:ss`; the seconds are noise on a timetable. */
  private clock(value: string): string {
    return value.length >= 5 ? value.slice(0, 5) : value;
  }
}
