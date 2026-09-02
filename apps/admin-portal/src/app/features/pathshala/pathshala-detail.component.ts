import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Enrolment, PathshalaDetail } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * One Pathshala: its sessions, its classes, and the children waiting for a
 * place.
 *
 * **The placement queue is the half that was a dead end.** A parent asks for a
 * place from the member portal, the enrolment lands `Requested`, and until now
 * nothing on the platform could move it — the same shape as timeline moderation
 * before the moderation queue existed.
 *
 * **Names come from a second call, on purpose.** pathshala-service stores a
 * child by id and nothing else, so the queue is a list of GUIDs. Rather than
 * have that service reach into member-family per row,
 * `GET /v1/children/names?ids=…` answers for exactly the ids on screen — names
 * only, not the child record, which carries a date of birth and the
 * parental-consent record that a queue printing a name has no business
 * receiving.
 *
 * **A class needs a session, and the screen says so rather than failing.** The
 * service refuses a class with no session; offering the form anyway would mean
 * a 404 that reads as a bug.
 */
@Component({
  selector: 'app-pathshala-detail',
  imports: [FormsModule, DatePipe, RouterLink],
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
      </p>

      <!-- Sessions ------------------------------------------------------- -->
      <div class="card">
        <h3>Academic sessions</h3>

        @if (school.sessions.length === 0) {
          <p class="empty">None yet. Open one to start creating classes.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr><th>Session</th><th>Runs</th><th></th></tr>
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
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        <form (ngSubmit)="openSession()">
          <h3 class="section-heading">Open a session</h3>
          <p class="small">
            Opening a session makes it the current one. The previous session's classes and
            records are kept.
          </p>

          <label for="session-label">Label</label>
          <input
            id="session-label"
            class="input"
            name="label"
            [(ngModel)]="sessionLabel"
            maxlength="40"
            placeholder="2026-27"
          />

          <div class="filter-row">
            <div>
              <label for="session-start">Starts</label>
              <input id="session-start" class="input" type="date" name="start"
                [(ngModel)]="sessionStart" />
            </div>
            <div>
              <label for="session-end">Ends</label>
              <input id="session-end" class="input" type="date" name="end"
                [(ngModel)]="sessionEnd" />
            </div>
          </div>

          <button class="btn" type="submit" [disabled]="busy() || !canOpenSession()">
            Open session
          </button>
        </form>
      </div>

      <!-- Classes -------------------------------------------------------- -->
      <div class="card spaced">
        <h3>Classes</h3>

        @if (school.classes.length === 0) {
          <p class="empty">No classes yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr><th>Class</th><th>Session</th><th>Room</th><th>Students</th><th>Teachers</th></tr>
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
                    <td>{{ klass.teacherMemberIds.length }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        @if (currentSession(); as current) {
          <form (ngSubmit)="createClass(current.id)">
            <h3 class="section-heading">Add a class to {{ current.label }}</h3>

            <label for="class-name">Name</label>
            <input id="class-name" class="input" name="className" [(ngModel)]="className"
              maxlength="120" placeholder="Class 8 — Jain Studies" />

            <label for="class-room">Room</label>
            <input id="class-room" class="input" name="roomLabel" [(ngModel)]="roomLabel"
              maxlength="60" placeholder="Room 2" />

            <button class="btn" type="submit" [disabled]="busy() || !className.trim()">
              Add class
            </button>
          </form>
        } @else {
          <p class="notice">
            Open a session first. A class belongs to one, so there is nowhere to put it yet.
          </p>
        }
      </div>

      <!-- Placement queue ------------------------------------------------ -->
      <div class="card spaced">
        <h3>Waiting for a place</h3>

        @if (requests().length === 0) {
          <p class="empty">
            Nobody is waiting. Requests appear here when a parent asks for a place.
          </p>
        } @else {
          <p class="small">
            A parent asked; somebody here decides. Turning a request down does not delete the
            child's record.
          </p>

          <div class="table-wrap">
            <table>
              <thead>
                <tr><th>Child</th><th>Asked</th><th>Class</th><th></th></tr>
              </thead>
              <tbody>
                @for (request of requests(); track request.id) {
                  <tr>
                    <td><b>{{ childName(request.childProfileId) }}</b></td>
                    <td>{{ request.requestedAt | date: 'd MMM y' }}</td>
                    <td>
                      @if (school.classes.length === 0) {
                        <span class="muted">No classes to place into</span>
                      } @else {
                        <label class="sr-only" [attr.for]="'class-' + request.id">
                          Class for {{ childName(request.childProfileId) }}
                        </label>
                        <select
                          class="input inline"
                          [id]="'class-' + request.id"
                          [name]="'class-' + request.id"
                          [(ngModel)]="chosenClass[request.id]"
                        >
                          <option value="">Choose a class…</option>
                          @for (klass of school.classes; track klass.id) {
                            <option [value]="klass.id">{{ klass.name }}</option>
                          }
                        </select>
                      }
                    </td>
                    <td class="row-actions">
                      <button
                        class="btn"
                        type="button"
                        [disabled]="busy() || !chosenClass[request.id]"
                        (click)="place(request, true)"
                      >
                        Place
                      </button>
                      <button
                        class="btn alt"
                        type="button"
                        [disabled]="busy()"
                        (click)="place(request, false)"
                      >
                        Turn down
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .section-heading {
      margin-top: var(--space-5);
    }

    .filter-row {
      display: flex;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .filter-row > div {
      flex: 1 1 180px;
    }

    .input.inline {
      margin: 0;
      max-width: 220px;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }
  `,
})
export class PathshalaDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);

  readonly pathshala = signal<PathshalaDetail | null>(null);
  readonly requests = signal<readonly Enrolment[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  /** Child id → name, resolved from member-family for the ids on screen. */
  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  readonly currentSession = computed(
    () => this.pathshala()?.sessions.find((s) => s.isCurrent) ?? null,
  );

  sessionLabel = '';
  sessionStart = '';
  sessionEnd = '';
  className = '';
  roomLabel = '';
  chosenClass: Record<string, string> = {};

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.load();
  }

  canOpenSession(): boolean {
    return (
      this.sessionLabel.trim().length > 0 &&
      this.sessionStart.length > 0 &&
      this.sessionEnd.length > 0
    );
  }

  childName(childProfileId: string): string {
    // "A child" rather than a GUID. An id on screen is no use to somebody
    // deciding which class to put them in, and printing one says less than
    // admitting the name could not be resolved.
    return this.names().get(childProfileId) ?? 'A child';
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.pathshala(this.id).subscribe({
      next: (found) => {
        this.pathshala.set(found);
        this.loading.set(false);
        this.loadRequests();
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

  private loadRequests(): void {
    this.api.enrolmentRequests(this.id).subscribe({
      next: (found) => {
        this.requests.set(found);

        // Seeded to '' for every row, because an undefined bound to a <select>
        // matches no option — not even the placeholder — and the control renders
        // empty rather than showing it.
        for (const request of found) {
          this.chosenClass[request.id] ??= '';
        }

        this.loadNames(found.map((r) => r.childProfileId));
      },
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  /**
   * Names are a convenience, so a failure here is silent: a queue showing
   * "A child" is still workable, and one that refused to load is not.
   */
  private loadNames(ids: readonly string[]): void {
    const distinct = [...new Set(ids)];

    if (distinct.length === 0) {
      this.names.set(new Map());
      return;
    }

    this.api.childNames(distinct).subscribe({
      next: (found) => this.names.set(new Map(found.map((c) => [c.id, c.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }

  openSession(): void {
    if (!this.canOpenSession()) {
      return;
    }

    this.act(
      this.api.openSession(this.id, this.sessionLabel.trim(), this.sessionStart, this.sessionEnd),
      `${this.sessionLabel.trim()} is now the current session.`,
      () => {
        this.sessionLabel = '';
        this.sessionStart = '';
        this.sessionEnd = '';
      },
    );
  }

  createClass(sessionId: string): void {
    if (this.className.trim().length === 0) {
      return;
    }

    const name = this.className.trim();

    this.act(
      this.api.createClass(this.id, sessionId, name, blankToNull(this.roomLabel)),
      `${name} added.`,
      () => {
        this.className = '';
        this.roomLabel = '';
      },
    );
  }

  place(request: Enrolment, place: boolean): void {
    const classId = place ? (this.chosenClass[request.id] ?? '') : '';

    if (place && classId.length === 0) {
      return;
    }

    const who = this.childName(request.childProfileId);

    this.act(
      this.api.placeStudent(request.id, place ? classId : null, place),
      place ? `${who} has a place.` : `${who}'s request was turned down.`,
    );
  }

  /**
   * Every action re-reads the Pathshala and the queue rather than patching what
   * is on screen. Placing a child changes a class's student count and takes a
   * row out of the queue, and opening a session changes which one is current —
   * the server is the only thing that knows all of that at once.
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

function blankToNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed.length === 0 ? null : trimmed;
}
