import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { OrganizerGroup, OrganizerType, SamaajEvent } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * Events — creating them, publishing them, calling them off.
 *
 * Wireframe `#events`.
 *
 * **Members could register for events nobody could create.** Creating,
 * publishing, cancelling and reading the attendee list were four complete,
 * tested, curl-only endpoints, while the member portal's events screens — the
 * capacity pill, the waitlist, the promotion when a place is given up — had
 * been shipped against events that could only be conjured with curl.
 *
 * **Drafts are the default view, and that is the point of the screen.** An
 * event exists in somebody's head long before the Samaaj should be told about
 * it, which is why creating and publishing are separate commands. The wireframe
 * lists Draft alongside Published for the same reason. `includeDrafts` is
 * honoured for a caller holding `Events.Publish` and quietly ignored otherwise,
 * so this screen asks for them and a member who somehow reached it would simply
 * see the published list.
 *
 * **Cancelling asks for a reason and then asks again.** The service requires
 * one — somebody who rearranged their day is owed better than "Cancelled" — and
 * a cancelled event cannot be republished, so the confirmation is not
 * ceremony.
 */
@Component({
  selector: 'app-events-list',
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    <h1 class="title">Events</h1>
    <p class="sub">Create, publish and monitor Samaaj and group events.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the community module, which events sit behind. Switch it
        on under the Samaaj's settings.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else {
      <div class="card">
        <h2>All events</h2>

        <label class="inline-check">
          <input type="checkbox" name="includePast" [ngModel]="includePast()"
            (ngModelChange)="setIncludePast($event)" />
          Show events that have already happened
        </label>

        @if (events().length === 0) {
          <p class="empty">
            Nothing yet. An event starts as a draft and tells the Samaaj nothing until you
            publish it.
          </p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Events, drafts and published together</caption>
              <thead>
                <tr>
                  <th>Event</th><th>Organiser</th><th>Date</th><th>RSVP</th>
                  <th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (event of events(); track event.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/events', event.id]"><b>{{ event.title }}</b></a>
                      @if (event.venue) {
                        <div class="muted">{{ event.venue }}</div>
                      }
                    </td>
                    <td>{{ organiser(event) }}</td>
                    <td>{{ event.startAt | date: 'd MMM y, h:mm a' }}</td>
                    <td>
                      {{ rsvp(event) }}
                      @if (event.waitlistedCount > 0) {
                        <div class="muted">{{ event.waitlistedCount }} waiting</div>
                      }
                    </td>
                    <td>
                      <span class="pill" [class.warn]="event.status === 'Draft'">
                        {{ event.status }}
                      </span>
                      @if (event.status === 'Cancelled' && event.cancellationReason) {
                        <div class="muted">{{ event.cancellationReason }}</div>
                      }
                    </td>
                    <td>
                      <div class="row-actions">
                        @if (event.status === 'Draft') {
                          <button class="btn small" type="button" [disabled]="busy()"
                            (click)="publish(event)">
                            Publish
                          </button>
                        }

                        @if (event.status !== 'Cancelled') {
                          <button
                            class="btn small alt"
                            type="button"
                            [disabled]="busy()"
                            [attr.aria-expanded]="cancelling() === event.id"
                            (click)="startCancelling(event)"
                          >
                            Cancel
                          </button>
                        }
                      </div>

                      @if (cancelling() === event.id) {
                        <div class="notice" role="status">
                          <label [attr.for]="'reason-' + event.id">
                            Why is <b>{{ event.title }}</b> off?
                          </label>
                          <input
                            class="input"
                            [id]="'reason-' + event.id"
                            [name]="'reason-' + event.id"
                            [(ngModel)]="reason"
                            maxlength="500"
                            placeholder="The hall is unavailable."
                          />
                          <p class="small">
                            Everyone registered is told this. A cancelled event cannot be
                            published again — the people told it is off will not be told twice.
                          </p>
                          <div class="row-actions">
                            <button class="btn" type="button"
                              [disabled]="busy() || !reason.trim()" (click)="cancel(event)">
                              Cancel the event
                            </button>
                            <button class="btn alt" type="button" (click)="cancelling.set(null)">
                              Keep it
                            </button>
                          </div>
                        </div>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <!-- Create --------------------------------------------------------- -->
      <div class="card spaced">
        <h2>Create an event</h2>

        <form (ngSubmit)="create()">
          <label for="event-title">Title</label>
          <input id="event-title" class="input" name="title" [(ngModel)]="title"
            maxlength="200" placeholder="Paryushan Lecture" />

          <label for="event-description">Description</label>
          <input id="event-description" class="input" name="description"
            [(ngModel)]="description" maxlength="2000" />

          <label for="event-venue">Venue</label>
          <input id="event-venue" class="input" name="venue" [(ngModel)]="venue"
            maxlength="200" placeholder="Jain Bhavan, Hiran Magri" />

          <div class="filter-row">
            <div>
              <label for="event-start">Starts</label>
              <input id="event-start" class="input" type="datetime-local" name="startAt"
                [(ngModel)]="startAt" />
            </div>
            <div>
              <label for="event-end">Ends</label>
              <input id="event-end" class="input" type="datetime-local" name="endAt"
                [(ngModel)]="endAt" />
            </div>
          </div>

          <label for="event-organiser">Organiser</label>
          <select id="event-organiser" class="input" name="organiser" [(ngModel)]="organizerId">
            <option value="">{{ scope.label() }} itself</option>
            @for (group of groups(); track group.id) {
              <option [value]="group.id">{{ group.name }}</option>
            }
          </select>

          <label class="inline-check">
            <input type="checkbox" name="registrationEnabled" [(ngModel)]="registrationEnabled" />
            Members may register
          </label>

          @if (registrationEnabled) {
            <label for="event-capacity">Capacity</label>
            <input id="event-capacity" class="input inline" type="number" min="1"
              name="capacity" [(ngModel)]="capacity" placeholder="Leave blank for no limit" />

            <p class="small">
              Blank means no limit. It is not the same as zero, which the service refuses — an
              event nobody can attend is a mistake rather than an intention.
            </p>
          }

          <button class="btn" type="submit" [disabled]="busy() || !canCreate()">
            Create as a draft
          </button>
        </form>
      </div>
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .filter-row {
      display: flex;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .filter-row > div {
      flex: 1 1 200px;
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

    .inline-check {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      margin: var(--space-3) 0;
    }
  `,
})
export class EventsListComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);

  readonly events = signal<readonly SamaajEvent[]>([]);
  readonly groups = signal<readonly OrganizerGroup[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly includePast = signal(false);

  /** Which event is being asked about, if any. Cancelling is not undoable. */
  readonly cancelling = signal<string | null>(null);

  title = '';
  description = '';
  venue = '';
  startAt = '';
  endAt = '';
  organizerId = '';
  registrationEnabled = true;
  capacity: number | null = null;
  reason = '';

  /** Group id → name, for the Organiser column and the create form. */
  private readonly groupNames = computed(
    () => new Map(this.groups().map((g) => [g.id, g.name])),
  );

  ngOnInit(): void {
    this.load();
  }

  canCreate(): boolean {
    return this.title.trim().length > 0 && this.startAt.length > 0;
  }

  /**
   * The Organiser column.
   *
   * A Samaaj event says the Samaaj's name; a group's event says the group's,
   * falling back to "A volunteer group" rather than printing a GUID at somebody
   * scanning a table.
   */
  organiser(event: SamaajEvent): string {
    if (event.organizerType === 'Samaaj') {
      return this.scope.label();
    }

    return this.groupNames().get(event.organizerId ?? '') ?? 'A volunteer group';
  }

  /**
   * The wireframe's RSVP column: "186 / 200" with a capacity, "94" without.
   *
   * No denominator is not a missing number — it is the unlimited case, and
   * printing "94 / 0" or "94 / ∞" would both say something the data does not.
   */
  rsvp(event: SamaajEvent): string {
    if (!event.registrationEnabled) {
      return 'No registration';
    }

    return event.capacity === null
      ? `${event.registeredCount}`
      : `${event.registeredCount} / ${event.capacity}`;
  }

  setIncludePast(value: boolean): void {
    this.includePast.set(value);
    this.load();
  }

  startCancelling(event: SamaajEvent): void {
    this.reason = '';
    this.cancelling.set(event.id);
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    // Drafts always: this screen exists to show them. The service ignores the
    // flag for anyone without Events.Publish rather than refusing.
    this.api.listEvents(true, this.includePast()).subscribe({
      next: (found) => {
        this.events.set(found);
        this.loading.set(false);
        this.loadGroups();
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.moduleOff.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  /** Names are a convenience: a failure leaves "A volunteer group" on screen. */
  private loadGroups(): void {
    this.api.organizerGroups().subscribe({
      next: (found) => this.groups.set(found),
      error: () => this.groups.set([]),
    });
  }

  create(): void {
    if (!this.canCreate()) {
      return;
    }

    const title = this.title.trim();
    const organizerType: OrganizerType = this.organizerId ? 'VolunteerGroup' : 'Samaaj';

    this.act(
      this.api.createEvent({
        title,
        description: blankToNull(this.description),
        startAt: new Date(this.startAt).toISOString(),
        endAt: this.endAt ? new Date(this.endAt).toISOString() : null,
        venue: blankToNull(this.venue),
        organizerType,
        organizerId: this.organizerId || null,
        registrationEnabled: this.registrationEnabled,

        // Only when registration is on, and null rather than 0 for "no limit".
        capacity: this.registrationEnabled ? (this.capacity || null) : null,
      }),
      `${title} created as a draft. Nobody has been told yet.`,
      () => {
        this.title = '';
        this.description = '';
        this.venue = '';
        this.startAt = '';
        this.endAt = '';
        this.organizerId = '';
        this.capacity = null;
      },
    );
  }

  publish(event: SamaajEvent): void {
    this.act(this.api.publishEvent(event.id), `${event.title} is published.`);
  }

  cancel(event: SamaajEvent): void {
    const reason = this.reason.trim();

    if (reason.length === 0) {
      return;
    }

    this.cancelling.set(null);

    this.act(
      this.api.cancelEvent(event.id, reason),
      `${event.title} is off. Everyone registered keeps their place on the record.`,
      () => (this.reason = ''),
    );
  }

  /**
   * Every action re-reads the list. Publishing changes which buttons a row
   * carries, cancelling adds a reason to it, and a registration that landed a
   * second ago changes the RSVP count — the server is the only thing that knows
   * all of it at once.
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
