import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { EventsApi } from './events.api';
import { formatDate, formatTime, hasPassed } from './events.format';
import { SamaajEvent } from './events.models';

/**
 * Events, from the member-portal wireframe's `#events` screen.
 *
 * The wireframe is a table of Event / Date / Organizer / Status with a
 * per-row action, and three rows showing the two states it cared about: Open,
 * and "Full — Waitlist" with the action changing to "Join Waitlist". Both are
 * real conditionals here, driven by `isFull` and `capacity`.
 *
 * Four states the wireframe did not show are real and are handled.
 *
 * **Cancelled.** A Samaaj can call an event off with a reason, and members who
 * were going are owed that reason rather than a row that silently disappears.
 *
 * **Already going, or already waiting.** The wireframe assumes a member who has
 * not responded. The row says where they stand and the action changes to match.
 *
 * **No capacity at all.** `capacity` is null for an event with no limit, which
 * is a different thing from a limit of zero: such an event is never full and
 * never has a waitlist, so it says neither.
 *
 * **Registration switched off.** An event can be published as an announcement
 * with nothing to RSVP to.
 *
 * The Organizer column shows the *kind* of organiser, not a name. Group names
 * live in volunteer-groups-service and the list carries an id; a name per row
 * would be a call per row.
 */
@Component({
  selector: 'app-events-list',
  imports: [RouterLink],
  styleUrl: './events.css',
  template: `
    <div class="events-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Events</h1>
          <p class="subtitle">Events organised by the Samaaj or its volunteer groups.</p>
        </div>
        <a class="btn secondary" routerLink="/home">Back to home</a>
      </header>

      @if (loading()) {
        <p role="status">Loading events…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (events().length === 0) {
        <p class="notice info" role="status">
          Your Samaaj has no events coming up.
        </p>
      } @else {
        @if (upcoming().length > 0) {
          <div class="table-scroll">
            <table>
              <caption class="sr-only">Upcoming events</caption>
              <tr>
                <th scope="col">Event</th>
                <th scope="col">Date</th>
                <th scope="col">Organiser</th>
                <th scope="col">Status</th>
                <th scope="col"><span class="sr-only">Actions</span></th>
              </tr>
              @for (event of upcoming(); track event.id) {
                <tr>
                  <td>{{ event.title }}</td>
                  <td>{{ date(event) }}</td>
                  <td>{{ organiser(event) }}</td>
                  <td>
                    <span class="pill" [class]="pillClass(event)">{{ status(event) }}</span>
                  </td>
                  <td>
                    <a class="btn small" [routerLink]="['/events', event.id]">
                      {{ action(event) }}
                    </a>
                  </td>
                </tr>
              }
            </table>
          </div>
        }

        @if (past().length > 0) {
          <!-- Kept, and kept apart. A member who wants to check what they went
               to should not have to guess, and an event that has happened
               should not sit at the top offering an RSVP. -->
          <h2 class="section-heading">Already happened</h2>

          <div class="table-scroll">
            <table>
              <caption class="sr-only">Past events</caption>
              <tr>
                <th scope="col">Event</th>
                <th scope="col">Date</th>
                <th scope="col">Organiser</th>
                <th scope="col">Status</th>
                <th scope="col"><span class="sr-only">Actions</span></th>
              </tr>
              @for (event of past(); track event.id) {
                <tr class="past">
                  <td>{{ event.title }}</td>
                  <td>{{ date(event) }}</td>
                  <td>{{ organiser(event) }}</td>
                  <td>
                    <span class="pill" [class]="pillClass(event)">{{ status(event) }}</span>
                  </td>
                  <td>
                    <a class="btn small secondary" [routerLink]="['/events', event.id]">View</a>
                  </td>
                </tr>
              }
            </table>
          </div>
        }
      }
    </div>
  `,
})
export class EventsListComponent implements OnInit {
  private readonly api = inject(EventsApi);

  readonly events = signal<readonly SamaajEvent[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly upcoming = computed(() =>
    this.events()
      .filter((event) => !hasPassed(event))
      .sort((left, right) => left.startAt.localeCompare(right.startAt)),
  );

  /** Newest first: the thing a member is looking for is the one just gone. */
  readonly past = computed(() =>
    this.events()
      .filter((event) => hasPassed(event))
      .sort((left, right) => right.startAt.localeCompare(left.startAt)),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.list().subscribe({
      next: (found) => {
        this.events.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  date(event: SamaajEvent): string {
    return `${formatDate(event.startAt)} • ${formatTime(event.startAt)}`;
  }

  organiser(event: SamaajEvent): string {
    return event.organizerType === 'VolunteerGroup' ? 'A volunteer group' : 'The Samaaj';
  }

  /**
   * The wireframe's Status column, with the states it did not draw.
   *
   * Order matters: a cancelled event is cancelled whatever else is true of it,
   * and where the member stands matters more to them than whether the event is
   * full.
   */
  status(event: SamaajEvent): string {
    if (event.status === 'Cancelled') {
      return 'Cancelled';
    }

    if (event.myRegistrationStatus === 'Registered') {
      return 'You are going';
    }

    if (event.myRegistrationStatus === 'Waitlisted') {
      return 'You are on the waitlist';
    }

    if (!event.registrationEnabled) {
      return 'No RSVP needed';
    }

    return event.isFull ? 'Full — waitlist' : 'Open';
  }

  pillClass(event: SamaajEvent): string {
    if (event.status === 'Cancelled') {
      return 'danger';
    }

    if (event.myRegistrationStatus === 'Registered') {
      return 'ok';
    }

    return event.isFull && event.registrationEnabled ? 'warn' : '';
  }

  /** The wireframe's per-row button, which changed with the state. */
  action(event: SamaajEvent): string {
    if (event.status === 'Cancelled' || !event.registrationEnabled) {
      return 'View';
    }

    if (event.myRegistrationStatus === 'Registered' || event.myRegistrationStatus === 'Waitlisted') {
      return 'View';
    }

    return event.isFull ? 'Join waitlist' : 'View / RSVP';
  }
}
