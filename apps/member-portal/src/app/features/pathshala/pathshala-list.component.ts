import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { MembersApi } from '../members/members.api';
import { MasterDataApi } from '../members/master-data.api';
import { Child, City, Locality, LookupItem, State } from '../members/members.models';
import { PathshalaApi } from './pathshala.api';
import {
  Enrolment,
  EnrolmentStatusLabels,
  Pathshala,
  PathshalaAnnouncement,
} from './pathshala.models';

/** What pathshala-service accepts for a phone number, character for character. */
const PHONE = /^\+?[0-9 ()-]{7,20}$/;

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
 *
 * **Asking for a place asks for a name and a phone too**, the one the
 * Pathshala will call about the child. It sees that number whatever the
 * member's directory privacy setting says, so the form says so, and it starts
 * filled from the member's own profile - loaded only once a child is picked,
 * because most visits are a parent checking on a child, not enrolling one.
 *
 * **Messages from the Pathshala come first when there are any**: a teacher
 * telling parents there is no class on Sunday is the most time-bound thing on
 * the screen. A failure to read them leaves the rest of the page alone.
 */
@Component({
  selector: 'app-pathshala-list',
  imports: [FormsModule, RouterLink, DatePipe],
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
        <!-- Messages from the Pathshala ----------------------------------- -->
        @if (announcements().length > 0) {
          <h2 class="section-heading">Messages from the Pathshala</h2>

          <div class="card">
            <ul class="messages">
              @for (message of announcements(); track message.id) {
                <li>
                  <h3>{{ message.title }}</h3>
                  <p class="small">
                    {{ message.pathshalaName }} · {{ message.sentAt | date: 'd MMM y, h:mm a' }}
                  </p>
                  <p class="message-body">{{ message.body }}</p>
                </li>
              }
            </ul>
          </div>
        }

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

                @if (location(pathshala); as where) {
                  <p class="small">{{ where }}</p>
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
                      [ngModel]="chosenChild[pathshala.id]"
                      (ngModelChange)="chooseChild(pathshala.id, $event)"
                    >
                      <option value="">Choose a child…</option>
                      @for (child of enrollable(); track child.id) {
                        <option [value]="child.id">{{ child.fullName }}</option>
                      }
                    </select>

                    @if (chosenChild[pathshala.id]) {
                      <label [attr.for]="'contact-name-' + pathshala.id">Your name</label>
                      <input
                        class="input"
                        [id]="'contact-name-' + pathshala.id"
                        [name]="'contact-name-' + pathshala.id"
                        [(ngModel)]="contactName"
                        maxlength="200"
                      />

                      <label [attr.for]="'contact-phone-' + pathshala.id">
                        Phone the Pathshala can call
                      </label>
                      <input
                        class="input"
                        type="tel"
                        [id]="'contact-phone-' + pathshala.id"
                        [name]="'contact-phone-' + pathshala.id"
                        [(ngModel)]="contactPhone"
                        maxlength="20"
                        [attr.aria-invalid]="phoneInvalid() ? 'true' : null"
                      />
                      <p class="small">
                        The Pathshala's staff see this name and number, whatever your directory
                        privacy setting, so they can reach you about your child.
                      </p>
                    }

                    <button
                      class="btn"
                      type="button"
                      [disabled]="busy() || !canEnrol(pathshala)"
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
  private readonly master = inject(MasterDataApi);

  readonly pathshalas = signal<readonly Pathshala[]>([]);
  readonly enrolments = signal<readonly Enrolment[]>([]);
  readonly children = signal<readonly Child[]>([]);
  readonly announcements = signal<readonly PathshalaAnnouncement[]>([]);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  /**
   * The master-data lists, fetched once so a Pathshala's ids read as names. A
   * failure leaves the free-text line on its own rather than failing the
   * directory: the location is a courtesy, enrolling is the point.
   */
  private readonly countries = signal<readonly LookupItem[]>([]);
  private readonly states = signal<readonly State[]>([]);
  private readonly cities = signal<readonly City[]>([]);
  private readonly localities = signal<readonly Locality[]>([]);

  /** Per-Pathshala, so one refusal does not blank the directory. */
  readonly enrolError = signal<Record<string, string>>({});
  readonly enrolMessage = signal<Record<string, string>>({});

  /** Which child is picked in each Pathshala's card. */
  chosenChild: Record<string, string> = {};

  /** Who the Pathshala calls. One pair for every card: it is the same parent. */
  contactName = '';
  contactPhone = '';
  private prefilled = false;

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

      // Messages are a courtesy on this screen, not its point.
      announcements: this.api
        .announcements()
        .pipe(catchError(() => of([] as PathshalaAnnouncement[]))),
    }).subscribe({
      next: ({ pathshalas, enrolments, children, announcements }) => {
        this.pathshalas.set(pathshalas);
        this.enrolments.set(enrolments);
        this.children.set(children);
        this.announcements.set(announcements);

        // Each card's picker starts on its placeholder option. Without this the
        // model is undefined, which matches no option at all - not even the one
        // whose value is the empty string - so the select renders with nothing
        // selected and reads as an empty dropdown rather than "Choose a child".
        for (const pathshala of pathshalas) {
          this.chosenChild[pathshala.id] ??= '';
        }

        this.loading.set(false);
        this.loadLocationNames(pathshalas);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private loadLocationNames(pathshalas: readonly Pathshala[]): void {
    const hasIds = pathshalas.some(
      (p) => p.address !== null && (p.address.cityId || p.address.localityId || p.address.stateId || p.address.countryId),
    );

    if (!hasIds) {
      return;
    }

    forkJoin({
      countries: this.master.listCountries().pipe(catchError(() => of([] as LookupItem[]))),
      states: this.master.listStates().pipe(catchError(() => of([] as State[]))),
      cities: this.master.listCities().pipe(catchError(() => of([] as City[]))),
      localities: this.master.listLocalities().pipe(catchError(() => of([] as Locality[]))),
    }).subscribe(({ countries, states, cities, localities }) => {
      this.countries.set(countries);
      this.states.set(states);
      this.cities.set(cities);
      this.localities.set(localities);
    });
  }

  /**
   * "12 Temple Road, Hiran Magri, Udaipur, Rajasthan, India" - the free-text
   * line, then whichever of locality, city, state and country resolve to a
   * name. Anything that does not resolve is left out rather than shown as an id.
   */
  location(pathshala: Pathshala): string {
    const address = pathshala.address;

    if (address === null) {
      return '';
    }

    const parts = [
      address.line,
      this.localities().find((x) => x.id === address.localityId)?.name,
      this.cities().find((x) => x.id === address.cityId)?.name,
      this.states().find((x) => x.id === address.stateId)?.name,
      this.countries().find((x) => x.id === address.countryId)?.label,
    ];

    return parts.filter((part): part is string => !!part && part.trim().length > 0).join(', ');
  }

  chooseChild(pathshalaId: string, childId: string): void {
    this.chosenChild[pathshalaId] = childId;

    if (childId && !this.prefilled) {
      this.prefillContact();
    }
  }

  /**
   * Starts the contact on the member's own name and mobile, once. A failure
   * leaves the boxes empty to be typed into, which is all they were before.
   */
  private prefillContact(): void {
    this.prefilled = true;

    this.members.me().subscribe({
      next: (me) => {
        this.contactName ||= me.fullName;
        this.contactPhone ||= me.mobile ?? '';
      },
      error: () => undefined,
    });
  }

  phoneInvalid(): boolean {
    const phone = this.contactPhone.trim();

    return phone.length > 0 && !PHONE.test(phone);
  }

  canEnrol(pathshala: Pathshala): boolean {
    return (
      !!this.chosenChild[pathshala.id] &&
      this.contactName.trim().length > 0 &&
      this.contactPhone.trim().length > 0 &&
      !this.phoneInvalid()
    );
  }

  enrol(pathshala: Pathshala): void {
    const childId = this.chosenChild[pathshala.id];

    if (!childId || !this.canEnrol(pathshala)) {
      return;
    }

    this.busy.set(true);
    this.clear(pathshala.id);

    this.api.enrol(pathshala.id, childId, this.contactName.trim(), this.contactPhone.trim()).subscribe({
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
