import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { Observable } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AreaScope } from '../../core/area-scope';
import { AcademicSession, PathshalaDetail } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';
import { blankToNull } from '../../core/modal-keys';
import { PathshalaAnnouncementsComponent } from './pathshala-announcements.component';
import { PathshalaDonationsComponent } from './pathshala-donations.component';
import { PathshalaRecordsComponent } from './pathshala-records.component';
import { PathshalaStudentsComponent } from './pathshala-students.component';
import { PathshalaTeachersComponent } from './pathshala-teachers.component';

export type PathshalaTab =
  | 'sessions'
  | 'classes'
  | 'teachers'
  | 'students'
  | 'exams'
  | 'progress'
  | 'attendance'
  | 'donations'
  | 'announcements';

const TABS: readonly { value: PathshalaTab; label: string; managersOnly?: true }[] = [
  { value: 'sessions', label: 'Academic sessions' },
  { value: 'classes', label: 'Classes' },
  { value: 'teachers', label: 'Teachers' },
  { value: 'students', label: 'Students' },
  { value: 'exams', label: 'Exams' },
  { value: 'progress', label: 'Progress report' },
  { value: 'attendance', label: 'Attendance' },
  { value: 'donations', label: 'Donations', managersOnly: true },
  { value: 'announcements', label: 'Announcements' },
];

/**
 * One Pathshala, as tabs: its sessions, classes, teachers, students, exams,
 * progress, attendance, donations and announcements.
 *
 * **Each tab is its own component and loads when it is opened**, so opening a
 * Pathshala costs one call rather than nine, and a tab always shows what is
 * true now rather than what was true when the page opened. The sessions and
 * classes come with the Pathshala itself and live here; the rest are in
 * `pathshala-*.component.ts` beside this one.
 *
 * **What a tab offers is decided by who is looking, and the service decides
 * again.** Somebody who runs this Pathshala (`Pathshala.Manage`, and for an
 * Area Admin only their own locality's) sees the forms; one of its teachers
 * sees the tabs read-only - narrowed by the service to the classes they teach
 * - and can message parents. Donations is the administrators' alone, so it is
 * not drawn for a teacher at all.
 *
 * **Editing and standing a Pathshala down live on its tile in the list**, the
 * same place Events and Groups put them; this page is for running it.
 */
@Component({
  selector: 'app-pathshala-detail',
  imports: [
    FormsModule,
    DatePipe,
    RouterLink,
    PathshalaTeachersComponent,
    PathshalaStudentsComponent,
    PathshalaRecordsComponent,
    PathshalaDonationsComponent,
    PathshalaAnnouncementsComponent,
  ],
  template: `
    <p><a class="btn link" routerLink="/pathshala">← All Pathshalas</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such Pathshala in {{ scope.label() }}.</p>
    } @else if (pathshala(); as school) {
      <h1 class="title">{{ school.name }}</h1>
      <p class="sub">
        @if (currentSession(); as current) {
          Current session {{ current.label }}.
        } @else {
          No session is open, so nothing can be taught yet.
        }
        @if (school.address?.line; as line) { · {{ line }} }
        @if (school.contactPerson; as contact) { · Contact: {{ contact }} }
      </p>

      @if (school.status !== 'Active') {
        <p class="notice">
          This Pathshala has been stood down. Its records are kept and it takes no new
          enrolments.
        </p>
      }

      @if (!area.administers(school.address?.localityId)) {
        <p class="notice" role="status">
          This Pathshala is in another locality, or has none recorded, so it is run by its own
          administrators.
        </p>
      } @else {
        <div class="tabs" role="tablist" aria-label="Running this Pathshala">
          @for (item of tabs(); track item.value) {
            <button
              type="button"
              role="tab"
              [id]="'tab-' + item.value"
              [class.on]="tab() === item.value"
              [attr.aria-selected]="tab() === item.value"
              aria-controls="pathshala-tab-panel"
              (click)="selectTab(item.value)"
            >
              {{ item.label }}
            </button>
          }
        </div>

        <div id="pathshala-tab-panel" role="tabpanel" [attr.aria-labelledby]="'tab-' + tab()">
          @switch (tab()) {
            @case ('sessions') {
              <div class="card">
                <div class="section-head">
                  <h2>Academic sessions</h2>
                  @if (canManage()) {
                    <button class="btn small" type="button" (click)="openNewSession()">Open a session</button>
                  }
                </div>

                @if (school.sessions.length === 0) {
                  <p class="empty">None yet. Open one to start creating classes.</p>
                } @else {
                  <div class="table-wrap">
                    <table>
                      <caption class="sr-only">Academic sessions</caption>
                      <thead>
                        <tr><th>Session</th><th>Runs</th><th></th><th></th></tr>
                      </thead>
                      <tbody>
                        @for (session of school.sessions; track session.id) {
                          <tr>
                            <td><b>{{ session.label }}</b></td>
                            <td>
                              {{ session.startDate | date: 'd MMM y' }} —
                              {{ session.endDate | date: 'd MMM y' }}
                            </td>
                            <td>
                              @if (session.isCurrent) {
                                <span class="pill ok">Current</span>
                              }
                            </td>
                            <td>
                              @if (canManage()) {
                                <button class="btn small alt" type="button" (click)="openSessionEdit(session)">
                                  Edit<span class="sr-only"> {{ session.label }}</span>
                                </button>
                              }
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }

              </div>
            }

            @case ('classes') {
              <div class="card">
                <div class="section-head">
                  <h2>Classes</h2>
                  @if (canManage()) {
                    <button class="btn small" type="button" (click)="openPopup('class')">Add a class</button>
                  }
                </div>

                @if (school.classes.length === 0) {
                  <p class="empty">No classes yet.</p>
                } @else {
                  <div class="table-wrap">
                    <table>
                      <caption class="sr-only">Classes in this Pathshala</caption>
                      <thead>
                        <tr>
                          <th>Class</th><th>Session</th><th>Room</th><th>Students</th><th>Teachers</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (klass of school.classes; track klass.id) {
                          <tr>
                            <td>
                              <a class="btn link" [routerLink]="['/pathshala', school.id, 'classes', klass.id]">
                                <b>{{ klass.name }}</b>
                              </a>
                            </td>
                            <td>{{ klass.sessionLabel }}</td>
                            <td>{{ klass.roomLabel ?? '—' }}</td>
                            <td>{{ klass.studentCount }}</td>
                            <td>{{ klass.teacherIds.length }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                  <p class="small">
                    Open a class to assign its teachers, set its timetable, mark the register and
                    record exams.
                  </p>
                }
              </div>
            }

            @case ('teachers') {

              <app-pathshala-teachers
                [pathshalaId]="school.id"
                [classes]="school.classes"
                [canManage]="canManage()"
                (changed)="load()"
              />
            }

            @case ('students') {
              <app-pathshala-students
                [pathshalaId]="school.id"
                [classes]="currentClasses()"
                [canManage]="canManage()"
                (changed)="load()"
              />
            }

            @case ('exams') {
              <app-pathshala-records [pathshalaId]="school.id" kind="exams" />
            }

            @case ('progress') {
              <app-pathshala-records [pathshalaId]="school.id" kind="progress" />
            }

            @case ('attendance') {
              <app-pathshala-records [pathshalaId]="school.id" kind="attendance" />
            }

            @case ('donations') {
              <app-pathshala-donations [pathshalaId]="school.id" />
            }

            @case ('announcements') {
              <app-pathshala-announcements [pathshalaId]="school.id" />
            }
          }
        </div>

        <!-- Open a session ------------------------------------------------ -->
        @if (popup() === 'session') {
          <div class="modal-backdrop" (click)="closePopup()">
            <div class="card modal-card modal-dialog" role="dialog" aria-modal="true"
              aria-labelledby="session-popup-title" (click)="$event.stopPropagation()"
              (keydown.escape)="closePopup()">
              @if (editingSession(); as editing) {
                <h2 id="session-popup-title">Edit {{ editing.label }}</h2>
                <p class="small">
                  Its classes, students and registers stay with it.
                  @if (editing.isCurrent) {
                    It stays the current session.
                  }
                </p>
              } @else {
                <h2 id="session-popup-title">Open a session</h2>
                <p class="small">
                  Opening a session makes it the current one. The previous session's classes and
                  records are kept.
                </p>
              }

              @if (error(); as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              <form (ngSubmit)="saveSession()">
                <div class="form-grid">
                  <div class="field">
                    <label for="session-label">Label</label>
                    <input id="session-label" class="input" name="label"
                      [(ngModel)]="sessionLabel" maxlength="40" placeholder="2026-27" />
                  </div>

                  <div class="field">
                    <label for="session-start">Starts</label>
                    <input id="session-start" class="input" type="date" name="start"
                      [(ngModel)]="sessionStart" />
                  </div>

                  <div class="field">
                    <label for="session-end">Ends</label>
                    <input id="session-end" class="input" type="date" name="end"
                      [(ngModel)]="sessionEnd" [min]="sessionStart"
                      [attr.aria-invalid]="sessionEnd && sessionStart && sessionEnd <= sessionStart ? 'true' : null" />
                  </div>
                </div>

                <div class="actions">
                  <button class="btn" type="submit" [disabled]="busy() || !canOpenSession()">
                    {{ editingSession() ? 'Save' : 'Open session' }}
                  </button>
                  <button class="btn alt" type="button" (click)="closePopup()">Cancel</button>
                </div>
              </form>
            </div>
          </div>
        }

        <!-- Add a class ---------------------------------------------------- -->
        @if (popup() === 'class') {
          <div class="modal-backdrop" (click)="closePopup()">
            <div class="card modal-card modal-dialog" role="dialog" aria-modal="true"
              aria-labelledby="class-popup-title" (click)="$event.stopPropagation()"
              (keydown.escape)="closePopup()">
              @if (currentSession(); as current) {
                <h2 id="class-popup-title">Add a class to {{ current.label }}</h2>

                @if (error(); as message) {
                  <p class="notice error" role="alert">{{ message }}</p>
                }

                <form (ngSubmit)="createClass(current.id)">
                  <div class="form-grid">
                    <div class="field">
                      <label for="class-name">Name</label>
                      <input id="class-name" class="input" name="className"
                        [(ngModel)]="className" maxlength="120" placeholder="Class 8 — Jain Studies" />
                    </div>

                    <div class="field">
                      <label for="class-room">Room</label>
                      <input id="class-room" class="input" name="roomLabel"
                        [(ngModel)]="roomLabel" maxlength="60" placeholder="Room 2" />
                    </div>
                  </div>

                  <div class="actions">
                    <button class="btn" type="submit" [disabled]="busy() || !className.trim()">Add class</button>
                    <button class="btn alt" type="button" (click)="closePopup()">Cancel</button>
                  </div>
                </form>
              } @else {
                <h2 id="class-popup-title">Add a class</h2>
                <p class="notice">
                  Open a session first. A class belongs to one, so there is nowhere to put it yet.
                </p>
                <div class="actions">
                  <button class="btn alt" type="button" (click)="closePopup()">Close</button>
                </div>
              }
            </div>
          </div>
        }
      }
    }
  `,
  styles: `
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
  `,
})
export class PathshalaDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);
  readonly area = inject(AreaScope);

  readonly pathshala = signal<PathshalaDetail | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);

  /** Which form is open over the page, if any. */
  readonly popup = signal<'session' | 'class' | null>(null);

  /** The session the session form is correcting, or null when it opens a new one. */
  readonly editingSession = signal<AcademicSession | null>(null);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly tab = signal<PathshalaTab>('sessions');

  readonly currentSession = computed(
    () => this.pathshala()?.sessions.find((s) => s.isCurrent) ?? null,
  );

  /** A child is placed into this session's classes, so the queue offers only those. */
  readonly currentClasses = computed(() => {
    const school = this.pathshala();
    const current = this.currentSession();

    return school === null || current === null
      ? []
      : school.classes.filter((c) => c.sessionId === current.id);
  });

  /** Runs this Pathshala: the permission, and for an Area Admin their own locality's. */
  readonly canManage = computed(
    () =>
      (this.auth.user()?.permissions.includes('Pathshala.Manage') ?? false) &&
      this.area.administers(this.pathshala()?.address?.localityId),
  );

  readonly tabs = computed(() => TABS.filter((t) => !t.managersOnly || this.canManage()));

  sessionLabel = '';
  sessionStart = '';
  sessionEnd = '';
  className = '';
  roomLabel = '';

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    // A class page links back to its own tab.
    const asked = this.route.snapshot.queryParamMap?.get('tab');

    if (asked && TABS.some((t) => t.value === asked)) {
      this.tab.set(asked as PathshalaTab);
    }

    this.load();
  }

  selectTab(tab: PathshalaTab): void {
    this.done.set(null);
    this.error.set(null);
    this.tab.set(tab);
  }

  /** The service's own rule: a label, and an end after the start. */
  canOpenSession(): boolean {
    return (
      this.sessionLabel.trim().length > 0 &&
      this.sessionStart.length > 0 &&
      this.sessionEnd.length > 0 &&
      this.sessionEnd > this.sessionStart
    );
  }

  load(): void {
    // A teacher asks as staff, so one they do not teach at is "no such
    // Pathshala" rather than a page of tabs that all answer 404.
    const asStaff = !(this.auth.user()?.permissions.includes('Pathshala.Manage') ?? false);

    this.api.pathshala(this.id, asStaff).subscribe({
      next: (found) => {
        this.pathshala.set(found);
        this.loading.set(false);

        // Donations is not drawn for somebody who cannot run it, so a link
        // straight to it lands on the first tab instead of an empty panel.
        if (!this.tabs().some((t) => t.value === this.tab())) {
          this.tab.set('sessions');
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

  openPopup(which: 'session' | 'class'): void {
    this.error.set(null);
    this.popup.set(which);
  }

  closePopup(): void {
    this.popup.set(null);
  }

  openNewSession(): void {
    this.editingSession.set(null);
    this.sessionLabel = '';
    this.sessionStart = '';
    this.sessionEnd = '';
    this.openPopup('session');
  }

  openSessionEdit(session: AcademicSession): void {
    this.editingSession.set(session);
    this.sessionLabel = session.label;
    this.sessionStart = session.startDate;
    this.sessionEnd = session.endDate;
    this.openPopup('session');
  }

  saveSession(): void {
    const editing = this.editingSession();

    if (editing === null) {
      this.openSession();

      return;
    }

    if (!this.canOpenSession()) {
      return;
    }

    const label = this.sessionLabel.trim();

    this.act(
      this.api.updateSession(this.id, editing.id, label, this.sessionStart, this.sessionEnd),
      `${label} is saved.`,
      () => {
        this.editingSession.set(null);
        this.popup.set(null);
      },
    );
  }

  openSession(): void {
    if (!this.canOpenSession()) {
      return;
    }

    const label = this.sessionLabel.trim();

    this.act(
      this.api.openSession(this.id, label, this.sessionStart, this.sessionEnd),
      `${label} is now the current session.`,
      () => {
        this.sessionLabel = '';
        this.popup.set(null);
        this.sessionStart = '';
        this.sessionEnd = '';
      },
    );
  }

  createClass(sessionId: string): void {
    const name = this.className.trim();

    if (name.length === 0) {
      return;
    }

    this.act(
      this.api.createClass(this.id, sessionId, name, blankToNull(this.roomLabel)),
      `${name} added.`,
      () => {
        this.className = '';
        this.popup.set(null);
        this.roomLabel = '';
      },
    );
  }

  /**
   * Every action re-reads the Pathshala rather than patching what is on
   * screen: opening a session changes which one is current, and the server is
   * the only thing that knows all of that at once.
   */
  private act(work: Observable<unknown>, message: string, reset: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        reset();
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
