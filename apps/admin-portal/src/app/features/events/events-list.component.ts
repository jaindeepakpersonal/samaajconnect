import { DatePipe } from '@angular/common';
import {
  Component,
  ElementRef,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { forkJoin } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AreaScope, labelLocalities } from '../../core/area-scope';
import { LookupItem, OrganizerGroup, OrganizerType, SamaajEvent } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';
import { ALL_LOCALITIES, LocalityFilterComponent, matchesLocality } from '../../core/locality-filter.component';

/**
 * Events — creating them, editing them, publishing them, calling them off.
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
 *
 * **Create and Edit are one modal, added 2026-09-13, the same shape Master
 * Data's own Add/Edit dialog already uses.** The create form used to sit
 * permanently below the list, which put it further down the page every time
 * the Samaaj had one more event - and there was no way to correct a typo in
 * a title or venue at all, since `events-service` had no update command
 * until the same change. The two share one form: Edit omits the Organiser
 * field, because `UpdateEventCommand` deliberately does not take one -
 * reassigning who is holding an event that may already carry registrations
 * is a bigger change than a correction. Edit is offered on a Draft or a
 * Published event and not on a Cancelled one, matching the service's own
 * refusal, so the button is not one that always answers 409.
 */
@Component({
  selector: 'app-events-list',
  imports: [LocalityFilterComponent, FormsModule, DatePipe, RouterLink],
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
        <div class="section-head">
          <h2>All events</h2>
          <button class="btn small" type="button" (click)="openCreateModal($event)">
            Create an event
          </button>
        </div>

        <label class="inline-check">
          <input type="checkbox" name="includePast" [ngModel]="includePast()"
            (ngModelChange)="setIncludePast($event)" />
          Show events that are over
        </label>

        <app-locality-filter [(value)]="localityFilter" inputId="events-locality" />

        @if (events().length > 0 && shownEvents().length === 0) {
          <p class="empty">Nothing here for that locality.</p>
        }

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
                  <th>Event</th><th>Organiser</th><th>Starts</th><th>Ends</th><th>RSVP</th>
                  <th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (event of shownEvents(); track event.id) {
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
                      @if (event.endAt) {
                        {{ event.endAt | date: 'd MMM y, h:mm a' }}
                      } @else {
                        <span class="muted">No end set</span>
                      }
                    </td>
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
                      @if (event.isHappeningNow) {
                        <span class="pill ok">Happening now</span>
                      }
                      @if (event.status === 'Cancelled' && event.cancellationReason) {
                        <div class="muted">{{ event.cancellationReason }}</div>
                      }
                    </td>
                    <td>
                      @if (!area.administers(event.localityId)) {
                        <span class="muted small">Another locality</span>
                      } @else {
                      <div class="row-actions">
                        @if (event.status !== 'Cancelled') {
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="openEditModal($event, event)">
                            Edit
                          </button>
                        }

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

    <!-- Create / Edit modal ---------------------------------------------- -->
    @if (modalOpen()) {
      <div class="modal-backdrop" (click)="closeModal()">
        <div
          class="card modal-card modal-dialog"
          role="dialog"
          aria-modal="true"
          aria-labelledby="event-modal-title"
          (click)="$event.stopPropagation()"
          (keydown)="onModalKeydown($event)"
        >
          <h2 id="event-modal-title">
            {{ modalMode() === 'create' ? 'Create an event' : 'Edit ' + editingEvent()?.title }}
          </h2>

          <form (ngSubmit)="submitModal()">
            <div class="form-grid">
              <div class="field">
                <label for="event-title">Title</label>
                <input id="event-title" class="input" name="title" [(ngModel)]="title"
                  maxlength="200" placeholder="Paryushan Lecture" #firstField />
              </div>

              <div class="field">
                <label for="event-description">Description</label>
                <input id="event-description" class="input" name="description"
                  [(ngModel)]="description" maxlength="2000" />
              </div>

              <div class="field">
                <label for="event-venue">Venue</label>
                <input id="event-venue" class="input" name="venue" [(ngModel)]="venue"
                  maxlength="200" placeholder="Jain Bhavan, Hiran Magri" />
              </div>

              <!-- An Area Admin's events are always in their own locality: shown, not offered. -->
              <div class="field">
                <label for="event-locality">Locality</label>
                <select id="event-locality" class="input" name="locality" [(ngModel)]="localityId"
                  [disabled]="area.localityId() !== null">
                  @if (area.localityId() === null) {
                    <option value="">The whole Samaaj</option>
                  }
                  @for (option of localityOptions(); track option.id) {
                    <option [value]="option.id">{{ option.label }}</option>
                  }
                </select>
              </div>

              <div class="field">
                <label for="event-start">Starts</label>
                <input id="event-start" class="input" type="datetime-local" name="startAt"
                  [(ngModel)]="startAt" />
              </div>

              <div class="field">
                <label for="event-end">Ends</label>
                <input id="event-end" class="input" type="datetime-local" name="endAt"
                  [(ngModel)]="endAt" />
              </div>

              <div class="field">
                <label for="event-type">Event type</label>
                <select id="event-type" class="input" name="eventType" [(ngModel)]="eventTypeId">
                  <option value="">Not specified</option>
                  @for (option of eventTypes(); track option.id) {
                    <option [value]="option.id">{{ option.label }}</option>
                  }
                </select>
              </div>

              @if (modalMode() === 'create') {
                <div class="field">
                  <label for="event-organiser">Organiser</label>
                  <select id="event-organiser" class="input" name="organiser" [(ngModel)]="organizerId">
                    <option value="">{{ scope.label() }} itself</option>
                    @for (group of groups(); track group.id) {
                      <option [value]="group.id">{{ group.name }}</option>
                    }
                  </select>
                </div>
              } @else {
                <p class="small span-all">
                  Organiser: {{ organiser(editingEvent()!) }} (fixed once created)
                </p>
              }

              <label class="inline-check span-all">
                <input type="checkbox" name="registrationEnabled" [(ngModel)]="registrationEnabled" />
                Members may register
              </label>

              @if (registrationEnabled) {
                <div class="field span-all">
                  <label for="event-capacity">Capacity</label>
                  <input id="event-capacity" class="input inline" type="number" min="1"
                    name="capacity" [(ngModel)]="capacity" placeholder="Leave blank for no limit" />

                  <p class="small">
                    Blank means no limit. It is not the same as zero, which the service refuses — an
                    event nobody can attend is a mistake rather than an intention.
                    @if (modalMode() === 'edit' && editingEvent()!.registeredCount > 0) {
                      Cannot be set below the {{ editingEvent()!.registeredCount }} people already
                      registered.
                    }
                  </p>
                </div>
              }
            </div>

            <div class="actions">
              <button class="btn" type="submit" [disabled]="busy() || !canSubmit()">
                {{ modalMode() === 'create' ? 'Create as a draft' : 'Save' }}
              </button>
              <button class="btn alt" type="button" (click)="closeModal()">Cancel</button>
            </div>
          </form>
        </div>
      </div>
    }
  `,
  styles: `
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
export class EventsListComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);
  readonly area = inject(AreaScope);

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLInputElement>;

  readonly events = signal<readonly SamaajEvent[]>([]);

  /** The Locality filter - see LocalityFilterComponent. */
  readonly localityFilter = signal(ALL_LOCALITIES);

  readonly shownEvents = computed(() =>
    this.events().filter((event) => matchesLocality(this.localityFilter(), event.localityId)),
  );
  readonly groups = signal<readonly OrganizerGroup[]>([]);
  readonly eventTypes = signal<readonly LookupItem[]>([]);
  readonly localityOptions = signal<readonly { id: string; label: string }[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly includePast = signal(false);

  /** Which event is being asked about, if any. Cancelling is not undoable. */
  readonly cancelling = signal<string | null>(null);

  readonly modalOpen = signal(false);
  readonly modalMode = signal<'create' | 'edit'>('create');
  readonly editingEvent = signal<SamaajEvent | null>(null);
  private modalTrigger: HTMLElement | null = null;

  title = '';
  description = '';
  venue = '';
  startAt = '';
  endAt = '';
  eventTypeId = '';
  organizerId = '';
  registrationEnabled = true;
  capacity: number | null = null;
  localityId = '';
  reason = '';

  /** Group id → name, for the Organiser column and the create form. */
  private readonly groupNames = computed(
    () => new Map(this.groups().map((g) => [g.id, g.name])),
  );

  ngOnInit(): void {
    this.load();
    this.loadEventTypes();

    // Names for the popup's Locality picker. A failure leaves it offering only
    // the whole Samaaj, and the events still load.
    forkJoin({ localities: this.api.listLocalities(), cities: this.api.listCities() }).subscribe({
      next: ({ localities, cities }) => this.localityOptions.set(labelLocalities(localities, cities)),
      error: () => this.localityOptions.set([]),
    });
  }

  canSubmit(): boolean {
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

  /** Optional: a Samaaj that has not seeded any event types yet gets an empty list. */
  private loadEventTypes(): void {
    this.api.listLookupItems('EventType').subscribe({
      next: (found) => this.eventTypes.set(found),
      error: () => this.eventTypes.set([]),
    });
  }

  // ---- Modal ----------------------------------------------------------------

  openCreateModal(event: MouseEvent): void {
    this.modalMode.set('create');
    this.editingEvent.set(null);
    this.title = '';
    this.description = '';
    this.venue = '';
    this.startAt = '';
    this.endAt = '';
    this.eventTypeId = '';
    this.organizerId = '';
    this.registrationEnabled = true;
    this.capacity = null;
    this.localityId = this.area.localityId() ?? '';
    this.openModal(event);
  }

  openEditModal(event: MouseEvent, samaajEvent: SamaajEvent): void {
    this.modalMode.set('edit');
    this.editingEvent.set(samaajEvent);
    this.title = samaajEvent.title;
    this.description = samaajEvent.description ?? '';
    this.venue = samaajEvent.venue ?? '';
    this.startAt = toLocalInputValue(samaajEvent.startAt);
    this.endAt = samaajEvent.endAt ? toLocalInputValue(samaajEvent.endAt) : '';
    this.eventTypeId = samaajEvent.eventTypeId ?? '';
    this.registrationEnabled = samaajEvent.registrationEnabled;
    this.capacity = samaajEvent.capacity;
    this.localityId = samaajEvent.localityId ?? '';
    this.openModal(event);
  }

  private openModal(event: MouseEvent): void {
    this.modalTrigger = event.currentTarget as HTMLElement;
    this.error.set(null);
    this.done.set(null);
    this.modalOpen.set(true);
    setTimeout(() => this.firstFieldRef?.nativeElement.focus());
  }

  closeModal(): void {
    if (!this.modalOpen()) {
      return;
    }

    this.modalOpen.set(false);

    const trigger = this.modalTrigger;
    this.modalTrigger = null;

    if (trigger) {
      setTimeout(() => trigger.focus());
    }
  }

  onModalKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.closeModal();

      return;
    }

    if (event.key !== 'Tab') {
      return;
    }

    const dialog = event.currentTarget as HTMLElement;
    const focusable = dialog.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
    );

    if (focusable.length === 0) {
      return;
    }

    const first = focusable[0]!;
    const last = focusable[focusable.length - 1]!;

    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  submitModal(): void {
    if (this.modalMode() === 'create') {
      this.create();
    } else {
      this.update();
    }
  }

  create(): void {
    if (!this.canSubmit()) {
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
        eventTypeId: this.eventTypeId || null,
        organizerType,
        organizerId: this.organizerId || null,
        registrationEnabled: this.registrationEnabled,

        // Only when registration is on, and null rather than 0 for "no limit".
        capacity: this.registrationEnabled ? (this.capacity || null) : null,
        localityId: this.localityId || null,
      }),
      `${title} created as a draft. Nobody has been told yet.`,
      () => this.closeModal(),
    );
  }

  update(): void {
    const editing = this.editingEvent();

    if (!editing || !this.canSubmit()) {
      return;
    }

    const title = this.title.trim();

    this.act(
      this.api.updateEvent(editing.id, {
        title,
        description: blankToNull(this.description),
        startAt: new Date(this.startAt).toISOString(),
        endAt: this.endAt ? new Date(this.endAt).toISOString() : null,
        venue: blankToNull(this.venue),
        eventTypeId: this.eventTypeId || null,
        registrationEnabled: this.registrationEnabled,
        capacity: this.registrationEnabled ? (this.capacity || null) : null,
        localityId: this.localityId || null,
      }),
      `${title} updated.`,
      () => this.closeModal(),
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

/** An ISO instant to the local `datetime-local` input value it renders as. */
function toLocalInputValue(iso: string): string {
  const date = new Date(iso);

  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const pad = (n: number) => n.toString().padStart(2, '0');

  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}
