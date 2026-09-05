import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { MembersApi } from '../members/members.api';
import { Child } from '../members/members.models';
import { PathshalaApi } from './pathshala.api';
import { Enrolment, EnrolmentStatusLabels, Pathshala } from './pathshala.models';

/**
 * Jain Pathshala, from the member-portal wireframe's `#pathshala` screen.
 *
 * The wireframe puts two cards side by side: a Pathshala with its counts and an
 * "Enroll Child" button, and an administration card whose buttons jump to My
 * Class and My Exams. Those two halves are two different readers, so the
 * shipped screen separates them by what the member actually has: the places
 * their children already hold come first, because a parent who has enrolled
 * comes back to check on a child rather than to enrol another one. The
 * directory sits below it.
 *
 * **Child names are resolved here, and that is not a contradiction of the
 * app's usual rule.** Timeline, Events and Groups print "A member" because
 * resolving those ids would be a call per row for names the reader has no
 * particular claim on. A parent's own children are one call to `/v1/children`,
 * a list they already have, and "Waiting for a place" against an opaque id
 * would be useless to the one person the screen is for.
 */
@Component({
  selector: 'app-pathshala-list',
  imports: [FormsModule, RouterLink],
  styleUrl: './pathshala.css',
  template: `
    <div class="pathshala-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Jain Pathshala</h1>
          <p class="subtitle">Your children's places, and the Samaaj's Pathshalas.</p>
        </div>
      </header>

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else {
        <!-- Places already held ------------------------------------------ -->
        @if (enrolments().length > 0) {
          <h2 class="section-heading">Your children at Pathshala</h2>

          <div class="grid">
            @for (enrolment of enrolments(); track enrolment.id) {
              <div class="card">
                <h2>{{ childName(enrolment) }}</h2>
                <p>{{ pathshalaName(enrolment.pathshalaId) }}</p>

                <div class="badges">
                  <span class="pill" [class]="pillClass(enrolment)">
                    {{ stage(enrolment) }}
                  </span>
                </div>

                <p class="small">{{ describe(enrolment) }}</p>

                <div class="actions">
                  <a class="btn small" [routerLink]="['/pathshala', enrolment.id]">
                    {{ enrolment.classId === null ? 'View' : 'Open' }}
                  </a>
                </div>
              </div>
            }
          </div>
        }

        <!-- The directory, and enrolling ---------------------------------- -->
        <h2 class="section-heading">
          {{ enrolments().length > 0 ? 'Enrol another child' : 'Pathshalas in your Samaaj' }}
        </h2>

        @if (pathshalas().length === 0) {
          <p class="notice info" role="status">
            Your Samaaj has not set up a Pathshala yet.
          </p>
        } @else {
          <div class="grid">
            @for (pathshala of pathshalas(); track pathshala.id) {
              <div class="card">
                <h2>{{ pathshala.name }}</h2>

                @if (pathshala.address; as address) {
                  <p class="small">{{ address }}</p>
                }

                <!-- The wireframe's "3 teachers • 8 classes • 126 students".
                     The student count is not one the service offers a parent,
                     and a roll size is a fact about other people's children. -->
                <p>{{ counts(pathshala) }}</p>

                @if (pathshala.currentSessionLabel; as session) {
                  <p class="small">Session {{ session }}</p>
                }

                @if (enrolError()[pathshala.id]; as message) {
                  <p class="notice error" role="alert">{{ message }}</p>
                }

                @if (enrolMessage()[pathshala.id]; as message) {
                  <p class="notice info" role="status">{{ message }}</p>
                }

                <div class="actions">
                  @if (!pathshala.acceptsEnrolments) {
                    <!-- No open session, so there is no class to be placed in.
                         A disabled button that says why beats a button that
                         fails. -->
                    <button
                      class="btn"
                      type="button"
                      disabled
                      title="This Pathshala has no session open for enrolment"
                    >
                      Enrol a child
                    </button>
                  } @else if (enrollable().length === 0) {
                    <span class="small">
                      Every child on your family record has already been put forward here.
                      Children are added under <a routerLink="/family">My Family</a>.
                    </span>
                  } @else {
                    <label [attr.for]="'child-' + pathshala.id">Child</label>
                    <select
                      class="input"
                      [id]="'child-' + pathshala.id"
                      [name]="'child-' + pathshala.id"
                      [(ngModel)]="chosenChild[pathshala.id]"
                    >
                      <option value="">Choose a child…</option>
                      @for (child of enrollable(); track child.id) {
                        <option [value]="child.id">{{ child.fullName }}</option>
                      }
                    </select>

                    <button
                      class="btn"
                      type="button"
                      [disabled]="busy() || !chosenChild[pathshala.id]"
                      (click)="enrol(pathshala)"
                    >
                      {{ busy() ? 'Sending…' : 'Enrol a child' }}
                    </button>
                  }
                </div>
              </div>
            }
          </div>
        }
      }
    </div>
  `,
})
export class PathshalaListComponent implements OnInit {
  private readonly api = inject(PathshalaApi);
  private readonly members = inject(MembersApi);

  readonly pathshalas = signal<readonly Pathshala[]>([]);
  readonly enrolments = signal<readonly Enrolment[]>([]);
  readonly children = signal<readonly Child[]>([]);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  /** Per-Pathshala, so one refusal does not blank the directory. */
  readonly enrolError = signal<Record<string, string>>({});
  readonly enrolMessage = signal<Record<string, string>>({});

  /** Which child is picked in each Pathshala's card. */
  chosenChild: Record<string, string> = {};

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    forkJoin({
      pathshalas: this.api.list(),
      enrolments: this.api.myEnrolments(),

      // A member with no family record has no children, which is a 404 rather
      // than an empty list. That is not a reason to fail the whole screen -
      // they can still read the directory.
      children: this.members.children().pipe(catchError(() => of([] as Child[]))),
    }).subscribe({
      next: ({ pathshalas, enrolments, children }) => {
        this.pathshalas.set(pathshalas);
        this.enrolments.set(enrolments);
        this.children.set(children);

        // Each card's picker starts on its placeholder option. Without this the
        // model is undefined, which matches no option at all - not even the one
        // whose value is the empty string - so the select renders with nothing
        // selected and reads as an empty dropdown rather than "Choose a child".
        for (const pathshala of pathshalas) {
          this.chosenChild[pathshala.id] ??= '';
        }

        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  enrol(pathshala: Pathshala): void {
    const childId = this.chosenChild[pathshala.id];

    if (!childId) {
      return;
    }

    this.busy.set(true);
    this.clear(pathshala.id);

    this.api.enrol(pathshala.id, childId).subscribe({
      next: () => {
        this.busy.set(false);
        this.chosenChild[pathshala.id] = '';

        this.enrolMessage.set({
          ...this.enrolMessage(),
          [pathshala.id]:
            'Asked for. The Pathshala places them in a class, and it will show here once they have.',
        });

        this.load();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.enrolError.set({
          ...this.enrolError(),
          [pathshala.id]: describeError(failure),
        });
      },
    });
  }

  // ---- Rendering ---------------------------------------------------------

  /**
   * Children with no place at any Pathshala yet.
   *
   * Filtered on every enrolment rather than per Pathshala, because the service
   * refuses a second live enrolment for one child anywhere and offering the
   * option would be offering a 409.
   */
  enrollable(): readonly Child[] {
    const spoken = new Set(
      this.enrolments()
        .filter((enrolment) => enrolment.status === 'Requested' || enrolment.status === 'Active')
        .map((enrolment) => enrolment.childProfileId),
    );

    return this.children().filter((child) => !spoken.has(child.id));
  }

  /** A name the parent knows, or an honest admission that this one is not theirs. */
  childName(enrolment: Enrolment): string {
    return (
      this.children().find((child) => child.id === enrolment.childProfileId)?.fullName ??
      'A child on your family record'
    );
  }

  pathshalaName(id: string): string {
    return this.pathshalas().find((pathshala) => pathshala.id === id)?.name ?? 'A Pathshala';
  }

  counts(pathshala: Pathshala): string {
    const classes = `${pathshala.classCount} ${pathshala.classCount === 1 ? 'class' : 'classes'}`;
    const teachers = `${pathshala.teacherCount} ${
      pathshala.teacherCount === 1 ? 'teacher' : 'teachers'
    }`;

    return `${teachers} • ${classes}`;
  }

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

  describe(enrolment: Enrolment): string {
    switch (enrolment.status) {
      case 'Requested':
        return 'The Pathshala has not placed them in a class yet.';
      case 'Active':
        return enrolment.className === null
          ? 'Enrolled.'
          : `${enrolment.className}${
              enrolment.sessionLabel === null ? '' : ` • session ${enrolment.sessionLabel}`
            }`;
      case 'Withdrawn':
        return 'No longer attending.';
      case 'Declined':
        return 'The Pathshala did not offer a place.';
    }
  }

  private clear(pathshalaId: string): void {
    const { [pathshalaId]: _oldError, ...errors } = this.enrolError();
    const { [pathshalaId]: _oldMessage, ...messages } = this.enrolMessage();

    this.enrolError.set(errors);
    this.enrolMessage.set(messages);
  }
}
