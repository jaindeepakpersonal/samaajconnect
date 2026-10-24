import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AreaScope } from '../../core/area-scope';
import {
  AttendanceStatus,
  ClassExam,
  Enrolment,
  PathshalaClass,
  PathshalaTeacher,
  RegisterEntry,
} from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/** What the register form holds for one child: a mark, or no mark at all. */
type Mark = AttendanceStatus | '';

/**
 * One class: who teaches it, when it meets, who is on the roll, the register
 * for a date, and its exams.
 *
 * **This is the teaching half of a Pathshala, and none of it had a screen.**
 * The Pathshala detail screen sets a school up and answers parents; six
 * endpoints past that point — teachers, timetable, roll, register, exams,
 * results — existed, were tested, and were reachable only by curl. A Samaaj
 * could enrol a child and then had no way to teach them.
 *
 * **The register is read before it is written, and that is the whole reason two
 * new endpoints exist.** Submitting a register amends what is already recorded
 * and leaves every mark not re-sent exactly as it was. A form that opened blank
 * would therefore turn "correct one child" into "silently keep whatever the
 * form defaulted the other twenty-four to". Nothing could read a register back
 * until now, so this screen could not have been built honestly without
 * `GET /classes/{id}/register` first.
 *
 * **A child with no mark is not marked Present.** The form's third state is
 * "not marked", it is the state an unmarked child starts in, and those rows are
 * left out of the submission entirely. Defaulting to Present would invent
 * attendance for a child nobody saw — and every number this platform reports
 * about a child's attendance is a count over exactly these rows.
 *
 * **Exam marks come with the exams for the same reason.** Re-recording a result
 * amends it, so a teacher needs to see who already has one before typing.
 */
@Component({
  selector: 'app-class-detail',
  imports: [FormsModule, RouterLink],
  template: `
    <p><a class="btn link" [routerLink]="['/pathshala', pathshalaId]" [queryParams]="{ tab: 'classes' }">← Back to the Pathshala</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such class in {{ scope.label() }}.</p>
    } @else if (klass(); as subject) {
      <h1 class="title">{{ subject.name }}</h1>
      <p class="sub">
        {{ subject.sessionLabel }}@if (subject.roomLabel) { · {{ subject.roomLabel }}}
        · {{ roll().length }} on the roll
      </p>

      <div class="tabs" role="tablist" aria-label="This class">
        @for (item of tabs; track item.value) {
          <button
            type="button"
            role="tab"
            [id]="'class-tab-' + item.value"
            [class.on]="tab() === item.value"
            [attr.aria-selected]="tab() === item.value"
            aria-controls="class-tab-panel"
            (click)="tab.set(item.value)"
          >
            {{ item.label }}
          </button>
        }
      </div>

      <div id="class-tab-panel" role="tabpanel" [attr.aria-labelledby]="'class-tab-' + tab()">
      @switch (tab()) {
      @case ('teachers') {
      <!-- Teachers -------------------------------------------------------- -->
      <div class="card">
        <div class="section-head">
          <h2>Teachers</h2>
          <button class="btn small" type="button" (click)="openPopup('teacher')">Assign a teacher</button>
        </div>

        @if (subject.teacherIds.length === 0) {
          <p class="empty">
            Nobody teaches this class yet. Until somebody does, no register can be marked and no
            exam can be set — teaching this class is part of the permission, not just holding it.
          </p>
        } @else {
          <ul class="plain">
            @for (teacherId of subject.teacherIds; track teacherId) {
              <li class="row-actions">
                <span>{{ teacherName(teacherId) }}</span>
                <button
                  class="btn small"
                  type="button"
                  [disabled]="busy()"
                  (click)="setTeacher(teacherId, false)"
                >
                  Remove
                </button>
              </li>
            }
          </ul>
        }

      </div>

      }
      @case ('timetable') {
      <!-- Timetable ------------------------------------------------------- -->
      <div class="card">
        <div class="section-head">
          <h2>Timetable</h2>
          <button class="btn small" type="button" (click)="openPopup('slot')">Add a slot</button>
        </div>

        @if (subject.schedule.length === 0) {
          <p class="empty">No weekly slots yet.</p>
        } @else {
          <ul class="plain">
            @for (slot of subject.schedule; track slot.dayOfWeek + slot.startTime) {
              <li>{{ slot.dayOfWeek }} · {{ slot.startTime }} – {{ slot.endTime }}</li>
            }
          </ul>
        }

      </div>

      }
      @case ('register') {
      <!-- Register -------------------------------------------------------- -->
      <div class="card">
        <h2>Register</h2>

        @if (roll().length === 0) {
          <p class="empty">Nobody is on the roll, so there is no register to mark.</p>
        } @else {
          <label for="register-date">Date</label>
          <input
            id="register-date"
            class="input inline"
            type="date"
            name="registerDate"
            [max]="todayValue"
            [ngModel]="registerDate()"
            (ngModelChange)="chooseDate($event)"
            [attr.aria-invalid]="dateProblem(subject) ? 'true' : null"
          />
          @if (meetingDays(subject); as days) {
            <p class="small">This class meets on {{ days }}.</p>
          }

          @if (dateProblem(subject); as problem) {
            <p class="notice" role="alert">{{ problem }}</p>
          }

          @if (registerLoading()) {
            <p class="empty" role="status">Reading the register…</p>
          } @else {
            <p class="small">
              @if (marked() > 0) {
                {{ marked() }} of {{ roll().length }} already marked for this date. Submitting
                amends those marks; a child left as “Not marked” is not sent at all.
              } @else {
                Nothing marked for this date yet.
              }
            </p>

            <div class="table-wrap">
              <table>
                <caption class="sr-only">The register for the chosen date</caption>
                <thead>
                  <tr><th>Child</th><th>Mark</th></tr>
                </thead>
                <tbody>
                  @for (student of roll(); track student.id) {
                    <tr>
                      <td><b>{{ childName(student.childProfileId) }}</b></td>
                      <td>
                        <label class="sr-only" [attr.for]="'mark-' + student.id">
                          Mark for {{ childName(student.childProfileId) }}
                        </label>
                        <select
                          class="input inline"
                          [id]="'mark-' + student.id"
                          [name]="'mark-' + student.id"
                          [(ngModel)]="marks[student.id]"
                        >
                          <option value="">Not marked</option>
                          <option value="Present">Present</option>
                          <option value="Absent">Absent</option>
                          <option value="Excused">Excused</option>
                        </select>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <p class="small">
              Excused days are not counted against a child's attendance percentage.
            </p>

            <button class="btn" type="button" [disabled]="busy() || toSubmit().length === 0 || dateProblem(subject) !== null"
              (click)="submitRegister()">
              Submit {{ toSubmit().length }} mark{{ toSubmit().length === 1 ? '' : 's' }}
            </button>
          }
        }
      </div>

      }
      @case ('roll') {
      <!-- Roll ------------------------------------------------------------ -->
      <div class="card">
        <h2>Roll</h2>

        @if (roll().length === 0) {
          <p class="empty">
            Nobody has been placed in this class. Parents' requests are answered on the Pathshala
            screen.
          </p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Children on this class's roll</caption>
              <thead>
                <tr><th>Child</th><th>Status</th><th></th></tr>
              </thead>
              <tbody>
                @for (student of roll(); track student.id) {
                  <tr>
                    <td><b>{{ childName(student.childProfileId) }}</b></td>
                    <td>{{ student.status }}</td>
                    <td>
                      <button
                        class="btn small"
                        type="button"
                        [disabled]="busy()"
                        (click)="withdraw(student)"
                      >
                        Withdraw
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <p class="small">
            Withdrawing takes a child off the roll and keeps their attendance and results. A child
            who left in March still attended from June.
          </p>
        }
      </div>

      }
      @case ('exams') {
      <!-- Exams ----------------------------------------------------------- -->
      <div class="card">
        <div class="section-head">
          <h2>Exams</h2>
          <button class="btn small" type="button" (click)="openPopup('exam')">Set an exam</button>
        </div>

        @if (exams().length === 0) {
          <p class="empty">No exams set for this class.</p>
        } @else {
          @for (exam of exams(); track exam.id) {
            <div class="exam">
              <h3 class="section-heading">
                {{ exam.title }} · {{ exam.examDate }} · out of {{ exam.maxScore }}
              </h3>

              <div class="table-wrap">
                <table>
                  <caption class="sr-only">Exam results</caption>
                  <thead>
                    <tr><th>Child</th><th>Score</th><th>Grade</th><th></th></tr>
                  </thead>
                  <tbody>
                    @for (student of roll(); track student.id) {
                      <tr>
                        <td><b>{{ childName(student.childProfileId) }}</b></td>
                        <td>
                          <label class="sr-only" [attr.for]="'score-' + exam.id + '-' + student.id">
                            Score for {{ childName(student.childProfileId) }}
                          </label>
                          <input
                            class="input inline"
                            type="number"
                            min="0"
                            [max]="exam.maxScore"
                            [id]="'score-' + exam.id + '-' + student.id"
                            [name]="'score-' + exam.id + '-' + student.id"
                            [(ngModel)]="scores[exam.id + ':' + student.id]"
                          />
                        </td>
                        <td>
                          <label class="sr-only" [attr.for]="'grade-' + exam.id + '-' + student.id">
                            Grade for {{ childName(student.childProfileId) }}
                          </label>
                          <input
                            class="input inline"
                            maxlength="4"
                            [id]="'grade-' + exam.id + '-' + student.id"
                            [name]="'grade-' + exam.id + '-' + student.id"
                            [(ngModel)]="grades[exam.id + ':' + student.id]"
                          />
                        </td>
                        <td>
                          <button
                            class="btn small"
                            type="button"
                            [disabled]="busy() || !hasScore(exam, student)"
                            (click)="recordResult(exam, student)"
                          >
                            {{ recorded(exam, student) ? 'Amend' : 'Record' }}
                          </button>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            </div>
          }

          <p class="small">
            Averages across exams are computed as percentages, so a paper out of 20 does not count
            for less than one out of 100.
          </p>
        }

      </div>
      }
      }
      </div>

      @if (popup() === 'teacher') {
        <div class="modal-backdrop" (click)="popup.set(null)">
          <div class="card modal-card modal-dialog" role="dialog" aria-modal="true"
            aria-labelledby="teacher-popup-title" (click)="$event.stopPropagation()" (keydown.escape)="popup.set(null)">
            <h2 id="teacher-popup-title">Assign a teacher</h2>

            @if (error(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <form (ngSubmit)="setTeacher(chosenTeacher, true)">

              @if (teachers().length === 0) {
                <p class="small">
                  This Pathshala has no teachers yet. Add them on its Teachers tab first - a teacher
                  need not be a member of the Samaaj.
                </p>
              }

              <label for="teacher">Teacher</label>
              <select id="teacher" class="input" name="teacher" [(ngModel)]="chosenTeacher">
                <option value="">Choose a teacher…</option>
                @for (teacher of assignable(); track teacher.id) {
                  <option [value]="teacher.id">{{ teacher.fullName }}</option>
                }
              </select>

              <div class="actions">
              <button class="btn" type="submit" [disabled]="busy() || !chosenTeacher">
                Assign
              </button>
              <button class="btn alt" type="button" (click)="popup.set(null)">Cancel</button>
            </div>
            </form>
          </div>
        </div>
      }

      @if (popup() === 'slot') {
        <div class="modal-backdrop" (click)="popup.set(null)">
          <div class="card modal-card modal-dialog" role="dialog" aria-modal="true"
            aria-labelledby="slot-popup-title" (click)="$event.stopPropagation()" (keydown.escape)="popup.set(null)">
            <h2 id="slot-popup-title">Add a slot</h2>

            @if (error(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <form (ngSubmit)="addSlot()">

              <div class="form-grid">
                <div class="field">
                  <label for="day">Day</label>
                  <select id="day" class="input" name="day" [(ngModel)]="day">
                    @for (name of days; track name) {
                      <option [value]="name">{{ name }}</option>
                    }
                  </select>
                </div>
                <div class="field">
                  <label for="from">From</label>
                  <input id="from" class="input" type="time" name="from" [(ngModel)]="startTime" />
                </div>
                <div class="field">
                  <label for="to">To</label>
                  <input id="to" class="input" type="time" name="to" [(ngModel)]="endTime" />
                </div>
              </div>

              <p class="small">
                A slot that overlaps another on the same day is refused — one class cannot meet twice
                at once.
              </p>

              <div class="actions">
              <button class="btn" type="submit" [disabled]="busy() || !canAddSlot()">Add slot</button>
              <button class="btn alt" type="button" (click)="popup.set(null)">Cancel</button>
            </div>
            </form>
          </div>
        </div>
      }

      @if (popup() === 'exam') {
        <div class="modal-backdrop" (click)="popup.set(null)">
          <div class="card modal-card modal-dialog" role="dialog" aria-modal="true"
            aria-labelledby="exam-popup-title" (click)="$event.stopPropagation()" (keydown.escape)="popup.set(null)">
            <h2 id="exam-popup-title">Set an exam</h2>

            @if (error(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <form (ngSubmit)="scheduleExam()">

              <div class="form-grid">
                <div class="field">
                  <label for="exam-title">Title</label>
                  <input id="exam-title" class="input" name="examTitle" [(ngModel)]="examTitle"
                    maxlength="200" placeholder="Half-yearly" />
                </div>

                <div class="field">
                  <label for="exam-date">Date</label>
                  <input id="exam-date" class="input" type="date" name="examDate"
                    [(ngModel)]="examDate" />
                </div>

                <div class="field">
                  <label for="exam-max">Out of</label>
                  <input id="exam-max" class="input" type="number" min="1" name="examMax"
                    [(ngModel)]="examMax" />
                </div>
              </div>

              <div class="actions">
              <button class="btn" type="submit" [disabled]="busy() || !canScheduleExam()">
                Set exam
              </button>
              <button class="btn alt" type="button" (click)="popup.set(null)">Cancel</button>
            </div>
            </form>
          </div>
        </div>
      }
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .section-heading {
      margin-top: var(--space-5);
    }

    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .section-head h2 {
      margin: 0;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex-wrap: wrap;
    }

    ul.plain {
      list-style: none;
      padding: 0;
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }

    .exam {
      margin-bottom: var(--space-4);
    }
  `,
})
export class ClassDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);
  readonly area = inject(AreaScope);

  readonly klass = signal<PathshalaClass | null>(null);

  readonly tabs: readonly { value: ClassTab; label: string }[] = [
    { value: 'register', label: 'Register' },
    { value: 'roll', label: 'Roll' },
    { value: 'exams', label: 'Exams' },
    { value: 'timetable', label: 'Timetable' },
    { value: 'teachers', label: 'Teachers' },
  ];

  /** The register first - marking it is what a class page is opened for most. */
  readonly tab = signal<ClassTab>('register');

  /** Which form is open over the page, if any. */
  readonly popup = signal<'teacher' | 'slot' | 'exam' | null>(null);

  openPopup(which: 'teacher' | 'slot' | 'exam'): void {
    this.error.set(null);
    this.popup.set(which);
  }
  readonly roll = signal<readonly Enrolment[]>([]);
  readonly exams = signal<readonly ClassExam[]>([]);
  readonly loading = signal(true);
  readonly registerLoading = signal(false);
  readonly busy = signal(false);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly registerDate = signal(today());

  /** Child id → name, resolved from member-family for the roll on screen. */
  private readonly childNames = signal<ReadonlyMap<string, string>>(new Map());

  /**
   * The Pathshala's own teacher records. A class names its teachers by record
   * id rather than member id, because a teacher need not be a member at all.
   */
  readonly teachers = signal<readonly PathshalaTeacher[]>([]);

  /** Enrolment id → the mark the form holds. `''` means not marked. */
  marks: Record<string, Mark> = {};

  /** `examId:enrolmentId` → what the form holds. */
  scores: Record<string, number | null> = {};
  grades: Record<string, string> = {};

  readonly days = [
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday',
    'Sunday',
  ];

  chosenTeacher = '';
  day = 'Sunday';
  startTime = '';
  endTime = '';
  examTitle = '';
  examDate = '';
  examMax: number | null = null;

  /** The Pathshala's teachers not already teaching this class. */
  readonly assignable = computed(() => {
    const already = new Set(this.klass()?.teacherIds ?? []);

    return this.teachers().filter((t) => !already.has(t.id));
  });

  /** How many of the roll already have a mark for the chosen date. */
  readonly marked = signal(0);

  get pathshalaId(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  private get classId(): string {
    return this.route.snapshot.paramMap.get('classId') ?? '';
  }

  ngOnInit(): void {
    this.load();
  }

  childName(childProfileId: string): string {
    return this.childNames().get(childProfileId) ?? 'A child';
  }

  teacherName(teacherId: string): string {
    return this.teachers().find((t) => t.id === teacherId)?.fullName ?? 'A teacher';
  }

  canAddSlot(): boolean {
    return this.startTime.length > 0 && this.endTime.length > 0;
  }

  canScheduleExam(): boolean {
    return (
      this.examTitle.trim().length > 0 &&
      this.examDate.length > 0 &&
      (this.examMax ?? 0) > 0
    );
  }

  /** Whether this child already has a mark in this exam. */
  recorded(exam: ClassExam, student: Enrolment): boolean {
    return exam.results.some((r) => r.enrolmentId === student.id);
  }

  hasScore(exam: ClassExam, student: Enrolment): boolean {
    const score = this.scores[`${exam.id}:${student.id}`];

    return score !== null && score !== undefined && `${score}`.length > 0;
  }

  /**
   * Only the children the teacher actually marked.
   *
   * A child left as "Not marked" is left out rather than sent as Present. The
   * submission amends what it names and leaves the rest alone, so an omitted
   * child keeps whatever they had — which is the honest answer for a child
   * nobody has said anything about.
   */
  toSubmit(): { enrolmentId: string; status: AttendanceStatus }[] {
    return this.roll()
      .map((student) => ({ enrolmentId: student.id, status: this.marks[student.id] }))
      .filter((mark): mark is { enrolmentId: string; status: AttendanceStatus } =>
        mark.status !== '' && mark.status !== undefined,
      );
  }

  /** Today on this browser's calendar - the teacher's day, not the server's UTC one. */
  readonly todayValue = today();

  /** "Sunday and Wednesday", or null for a class with no timetable yet (which meets any day). */
  meetingDays(subject: PathshalaClass): string | null {
    const days = WEEK.filter((day) => subject.schedule.some((slot) => slot.dayOfWeek === day));

    if (days.length === 0) {
      return null;
    }

    return days.length === 1 ? days[0]! : `${days.slice(0, -1).join(', ')} and ${days[days.length - 1]}`;
  }

  /**
   * Why the chosen date cannot be marked, before asking - the service's own
   * two refusals: a day still to come, and a day the class does not meet.
   */
  dateProblem(subject: PathshalaClass): string | null {
    const date = this.registerDate();

    if (!date) {
      return null;
    }

    if (date > this.todayValue) {
      return 'That day is still to come. A register is marked once the class has met.';
    }

    const weekday = WEEK[(new Date(`${date}T00:00:00`).getDay() + 6) % 7]!;
    const days = this.meetingDays(subject);

    if (days !== null && !subject.schedule.some((slot) => slot.dayOfWeek === weekday)) {
      return `This class does not meet on a ${weekday}. It meets on ${days}.`;
    }

    return null;
  }

  chooseDate(date: string): void {
    this.registerDate.set(date);
    this.loadRegister();
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    const asStaff = !(this.auth.user()?.permissions.includes('Pathshala.Manage') ?? false);

    this.api.pathshala(this.pathshalaId, asStaff).subscribe({
      next: (school) => {
        // An Area Admin runs only their own locality's Pathshalas; anything
        // else answers 404 from every call below, so it reads as not found.
        const found = this.area.administers(school.address?.localityId)
          ? (school.classes.find((c) => c.id === this.classId) ?? null)
          : null;

        this.klass.set(found);
        this.notFound.set(found === null);
        this.loading.set(false);

        if (found) {
          // Today, if the class meets today; otherwise the last day it did -
          // which is the register a teacher is opening this to mark.
          this.registerDate.set(lastClassDay(found.schedule.map((slot) => slot.dayOfWeek)));
          this.loadRoll();
          this.loadExams();
          this.loadTeachers();
        }
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.notFound.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  private loadRoll(): void {
    this.api.classRoll(this.classId).subscribe({
      next: (found) => {
        this.roll.set(found);
        this.loadChildNames(found.map((s) => s.childProfileId));
        this.loadRegister();
      },
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  /**
   * Reads the marks already recorded and puts them in the form.
   *
   * This is the call that makes the register safe to edit. Everything not
   * re-sent stays as it was, so a form that started blank would ask a teacher
   * correcting one child to remember the other twenty-four.
   */
  private loadRegister(): void {
    this.registerLoading.set(true);

    this.api.classRegister(this.classId, this.registerDate()).subscribe({
      next: (entries) => {
        this.applyRegister(entries);
        this.registerLoading.set(false);
      },
      error: (failure: unknown) => {
        // Not silent, unlike the name lookups: a register that failed to load
        // and a date nobody has marked look identical on screen, and the
        // difference decides whether submitting is a correction or a first
        // entry.
        this.error.set(describeError(failure));
        this.registerLoading.set(false);
      },
    });
  }

  private applyRegister(entries: readonly RegisterEntry[]): void {
    const existing = new Map(entries.map((e) => [e.enrolmentId, e.status]));

    this.marks = {};

    for (const student of this.roll()) {
      this.marks[student.id] = existing.get(student.id) ?? '';
    }

    this.marked.set(this.roll().filter((s) => existing.has(s.id)).length);
  }

  private loadExams(): void {
    this.api.classExams(this.classId).subscribe({
      next: (found) => {
        this.exams.set(found);

        // Seed the score boxes with the marks already recorded, for the same
        // reason the register is read back: re-recording amends, so a teacher
        // must see what is there before typing over it.
        for (const exam of found) {
          for (const result of exam.results) {
            this.scores[`${exam.id}:${result.enrolmentId}`] = result.score;
            this.grades[`${exam.id}:${result.enrolmentId}`] = result.grade ?? '';
          }
        }
      },
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  /** Names are a convenience: a failure leaves "A child" rather than a GUID. */
  private loadChildNames(ids: readonly string[]): void {
    const distinct = [...new Set(ids)];

    if (distinct.length === 0) {
      this.childNames.set(new Map());
      return;
    }

    this.api.childNames(distinct).subscribe({
      next: (found) => this.childNames.set(new Map(found.map((c) => [c.id, c.fullName]))),
      error: () => this.childNames.set(new Map()),
    });
  }

  private loadTeachers(): void {
    this.api.pathshalaTeachers(this.pathshalaId).subscribe({
      next: (found) => this.teachers.set(found),
      error: () => this.teachers.set([]),
    });
  }

  setTeacher(teacherId: string, assign: boolean): void {
    if (teacherId.length === 0) {
      return;
    }

    const who = this.teacherName(teacherId);

    this.act(
      this.api.assignTeacher(this.classId, teacherId, assign),
      assign ? `${who} now teaches this class.` : `${who} no longer teaches this class.`,
      () => {
        this.chosenTeacher = '';
        this.popup.set(null);
      },
    );
  }

  addSlot(): void {
    if (!this.canAddSlot()) {
      return;
    }

    this.act(
      this.api.addClassSlot(this.classId, this.day, this.startTime, this.endTime),
      `${this.day} ${this.startTime}–${this.endTime} added.`,
      () => {
        this.startTime = '';
        this.endTime = '';
        this.popup.set(null);
      },
    );
  }

  submitRegister(): void {
    const marks = this.toSubmit();

    if (marks.length === 0) {
      return;
    }

    this.act(
      this.api.markAttendance(this.classId, this.registerDate(), marks),
      `Register submitted for ${this.registerDate()}.`,
    );
  }

  withdraw(student: Enrolment): void {
    this.act(
      this.api.withdrawStudent(student.id),
      `${this.childName(student.childProfileId)} is off the roll. Their record is kept.`,
    );
  }

  scheduleExam(): void {
    if (!this.canScheduleExam()) {
      return;
    }

    const title = this.examTitle.trim();

    this.act(
      this.api.scheduleExam(this.classId, title, this.examDate, this.examMax ?? 0),
      `${title} set.`,
      () => {
        this.examTitle = '';
        this.examDate = '';
        this.popup.set(null);
        this.examMax = null;
      },
    );
  }

  recordResult(exam: ClassExam, student: Enrolment): void {
    const key = `${exam.id}:${student.id}`;
    const score = this.scores[key];

    if (score === null || score === undefined) {
      return;
    }

    const grade = (this.grades[key] ?? '').trim();

    this.act(
      this.api.recordExamResult(exam.id, student.id, Number(score), grade.length ? grade : null),
      `${this.childName(student.childProfileId)}: ${score} out of ${exam.maxScore}.`,
    );
  }

  /**
   * Every action re-reads the class, the roll, the register and the exams
   * rather than patching the screen. Assigning a teacher changes who may mark
   * this register; withdrawing a child changes the roll every other section is
   * drawn from; recording a mark changes whether the next click amends or
   * records. The server is the only thing that knows all of it at once.
   */
  private act(work: { subscribe: (o: object) => void }, message: string, reset?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        reset?.();
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}

/** Today, as the date input wants it. */
/** Monday first, matching the timetable's own day names. */
const WEEK = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

/** A local Date as "yyyy-MM-dd". */
function localDay(date: Date): string {
  const pad = (n: number) => n.toString().padStart(2, '0');

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/**
 * The most recent day, today included, that the class meets on - or today
 * for a class with no timetable, which the service lets meet any day.
 */
function lastClassDay(meetingDays: readonly string[]): string {
  const day = new Date();

  for (let back = 0; back < 7 && meetingDays.length > 0; back++) {
    if (meetingDays.includes(WEEK[(day.getDay() + 6) % 7]!)) {
      return localDay(day);
    }

    day.setDate(day.getDate() - 1);
  }

  return localDay(new Date());
}

/**
 * Today on this browser's calendar. `toISOString()` is UTC, which in India
 * is still yesterday until 5:30 in the morning.
 */
function today(): string {
  return localDay(new Date());
}

type ClassTab = 'register' | 'roll' | 'exams' | 'timetable' | 'teachers';
