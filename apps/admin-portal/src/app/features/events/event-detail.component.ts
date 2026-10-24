import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AreaScope } from '../../core/area-scope';
import { Attendee, EventScheduleItem, LookupItem, SamaajEvent } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';
import { LocalityOptions } from '../../core/locality-options';

/**
 * One event, laid out like a page: its photo as a cover (a calendar leaf for
 * the day it starts when there is none), its name and when, then tabs - About,
 * Schedule, Going - with every change made in a popup.
 *
 * **Edit is one popup with two parts, the event and its photo.** The details
 * are `PATCH /v1/events/{id}`, the same fields the list's own Edit offers (the
 * organiser stays fixed once created); the photo is its own upload, because a
 * file and a form field are different requests and a failed upload must not
 * lose a half-typed description. Neither is offered on a cancelled event, or
 * to an Area Admin for an event in another locality - the service refuses both.
 *
 * **The attendee list was the last endpoint on this service nobody could
 * reach.** `GET /v1/events/{id}/attendees` needs `Events.Publish`, because who
 * else is going is a fact about other people and a Samaaj is a place where that
 * matters — so it was never going to appear on a member screen, and there was no
 * administrator screen to put it on.
 *
 * **The waitlist is shown in its order, and the order is the substance.** A
 * queue is only worth having because the longest wait comes off it first when a
 * place is given up; a list that did not show the order would be hiding the one
 * thing an organiser is asked about when somebody phones to ask where they
 * stand.
 *
 * **A programme has to fall on one of the event's own days.** The schedule
 * popup checks the date against the event's start and end as the organiser's
 * own calendar reads them - an event with no end takes programmes on its start
 * day only - and events-service holds the same line, with a day's slack for a
 * time zone it is not told.
 *
 * **Names are resolved here, not by events-service.** It stores an attendee as
 * a member id and a status and should not hold more of a list of who is going
 * somewhere than it has to. The panel is already authenticated to the directory.
 */
@Component({
  selector: 'app-event-detail',
  imports: [AuthedImageDirective, DatePipe, FormsModule, RouterLink],
  template: `
    <p><a class="btn link" routerLink="/events">← All events</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such event in {{ scope.label() }}.</p>
    } @else if (event(); as subject) {
      <!-- The cover: the photo, or the day it starts on a calendar leaf. -->
      <div class="event-cover" [class.cancelled]="subject.status === 'Cancelled'">
        @if (subject.photoUrl; as path) {
          <img class="event-cover-img" [scAuthedSrc]="path" alt="" />
        } @else {
          <div class="event-date-badge" aria-hidden="true">
            <span class="event-date-month">{{ subject.startAt | date: 'MMM' }}</span>
            <span class="event-date-day">{{ subject.startAt | date: 'd' }}</span>
          </div>
        }
      </div>

      <div class="event-head">
        <div class="event-head-text">
          <h1 class="title">{{ subject.title }}</h1>
          <p class="sub">
            {{ subject.startAt | date: 'd MMM y, h:mm a' }}
            @if (subject.endAt) { – {{ subject.endAt | date: 'd MMM y, h:mm a' }} }
            @if (subject.venue) { · {{ subject.venue }} }
          </p>
          <p class="badges">
            <span class="pill" [class.warn]="subject.status === 'Draft'">{{ subject.status }}</span>
            @if (subject.isHappeningNow) {
              <span class="pill ok">Happening now</span>
            }
            @if (eventTypeLabel(subject); as type) {
              <span class="pill">{{ type }}</span>
            }
          </p>
        </div>

        @if (canEdit(subject)) {
          <div class="event-head-actions">
            <button class="btn" type="button" (click)="openEditModal(subject)">Edit</button>
          </div>
        }
      </div>

      @if (subject.status === 'Cancelled') {
        <p class="notice">
          This event is off.
          @if (subject.cancellationReason) {
            {{ subject.cancellationReason }}
          }
          The registrations are kept, so the people who were coming can still be told.
        </p>
      }

      @if (!area.administers(subject.localityId)) {
        <p class="notice" role="status">
          This event is in another locality. Who is going, and changing it, are for its own administrators.
        </p>
      }

      <div class="tabs" role="tablist" aria-label="This event">
        @for (item of tabsFor(subject); track item.value) {
          <button
            type="button"
            role="tab"
            [id]="'event-tab-' + item.value"
            [class.on]="tab() === item.value"
            [attr.aria-selected]="tab() === item.value"
            aria-controls="event-tab-panel"
            (click)="tab.set(item.value)"
          >
            {{ item.label }}
          </button>
        }
      </div>

      <div id="event-tab-panel" role="tabpanel" [attr.aria-labelledby]="'event-tab-' + tab()">
        @switch (tab()) {
          <!-- Schedule ----------------------------------------------------- -->
          @case ('schedule') {
            <div class="card">
              <div class="section-head">
                <h2>Schedule</h2>
                @if (canEdit(subject)) {
                  <button class="btn small" type="button" (click)="openScheduleModal(null)">Add a programme</button>
                }
              </div>

              @if (schedule(subject).length === 0) {
                <p class="empty">
                  No programmes yet.
                  @if (canEdit(subject)) {
                    Add one for each session - a date, its name, and when it starts and ends.
                  }
                </p>
              } @else {
                <div class="table-wrap">
                  <table>
                    <caption class="sr-only">The programmes of {{ subject.title }}, by date</caption>
                    <thead>
                      <tr><th>Date</th><th>Programme</th><th>Start</th><th>End</th><th></th></tr>
                    </thead>
                    <tbody>
                      @for (item of schedule(subject); track item.id) {
                        <tr>
                          <td>{{ localDay(item.date) | date: 'EEE, d MMM y' }}</td>
                          <td><b>{{ item.programName }}</b></td>
                          <td>{{ clock(item.startTime) }}</td>
                          <td>{{ item.endTime ? clock(item.endTime) : '—' }}</td>
                          <td>
                            @if (canEdit(subject)) {
                              <div class="row-actions">
                                <button class="btn small alt" type="button" [disabled]="busy()"
                                  (click)="openScheduleModal(item)">
                                  Edit<span class="sr-only"> {{ item.programName }}</span>
                                </button>
                                <button class="btn small alt" type="button" [disabled]="busy()"
                                  (click)="removeScheduleItem(subject, item)">
                                  Remove<span class="sr-only"> {{ item.programName }}</span>
                                </button>
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
          }

          <!-- Going ------------------------------------------------------- -->
          @case ('going') {
            <div class="card">
              <h2>Going</h2>

              @if (!subject.registrationEnabled) {
                <p class="empty">This event does not take registrations.</p>
              } @else {
                <p class="small">
                  {{ registered().length }}
                  @if (subject.capacity !== null) {
                    of {{ subject.capacity }} places taken
                  } @else {
                    registered, with no limit set
                  }
                </p>

                @if (registered().length === 0) {
                  <p class="empty">Nobody has registered yet.</p>
                } @else {
                  <div class="table-wrap">
                    <table>
                      <caption class="sr-only">Members going to this event</caption>
                      <thead>
                        <tr><th>Member</th><th>Registered</th></tr>
                      </thead>
                      <tbody>
                        @for (attendee of registered(); track attendee.memberId) {
                          <tr>
                            <td><a [routerLink]="['/members', attendee.memberId]"><b>{{ memberName(attendee.memberId) }}</b></a></td>
                            <td>{{ attendee.registeredAt | date: 'd MMM y, h:mm a' }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }
              }
            </div>

            @if (subject.registrationEnabled) {
              <div class="card spaced">
                <h2>Waiting</h2>

                @if (waitlisted().length === 0) {
                  <p class="empty">Nobody is waiting.</p>
                } @else {
                  <p class="small">
                    In the order they joined, which is the order they come off it. Giving up a
                    confirmed place promotes whoever has waited longest, and a promoted member keeps
                    the position they had.
                  </p>

                  <div class="table-wrap">
                    <table>
                      <caption class="sr-only">Members waiting, in the order they joined the queue</caption>
                      <thead>
                        <tr><th>#</th><th>Member</th><th>Joined the queue</th></tr>
                      </thead>
                      <tbody>
                        @for (attendee of waitlisted(); track attendee.memberId; let i = $index) {
                          <tr>
                            <td>{{ i + 1 }}</td>
                            <td><a [routerLink]="['/members', attendee.memberId]"><b>{{ memberName(attendee.memberId) }}</b></a></td>
                            <td>{{ attendee.registeredAt | date: 'd MMM y, h:mm a' }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }
              </div>
            }

            @if (cancelled().length > 0) {
              <div class="card spaced">
                <h2>Gave up a place</h2>
                <p class="small">
                  Kept because a place given up is what promotes somebody off the waitlist, and an
                  organiser asked why the numbers moved should be able to see it.
                </p>

                <div class="table-wrap">
                  <table>
                    <caption class="sr-only">Members who gave up a place</caption>
                    <thead>
                      <tr><th>Member</th><th>Had registered</th></tr>
                    </thead>
                    <tbody>
                      @for (attendee of cancelled(); track attendee.memberId) {
                        <tr>
                          <td><a [routerLink]="['/members', attendee.memberId]">{{ memberName(attendee.memberId) }}</a></td>
                          <td>{{ attendee.registeredAt | date: 'd MMM y, h:mm a' }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              </div>
            }
          }

          <!-- About ------------------------------------------------------- -->
          @default {
            <div class="card">
              <h2>About</h2>

              @if (subject.description) {
                <p class="event-body">{{ subject.description }}</p>
              } @else {
                <p class="empty">No description was given.</p>
              }

              <dl class="facts">
                <dt>Starts</dt>
                <dd>{{ subject.startAt | date: 'EEE, d MMM y, h:mm a' }}</dd>
                <dt>Ends</dt>
                <dd>{{ subject.endAt ? (subject.endAt | date: 'EEE, d MMM y, h:mm a') : 'No end set' }}</dd>
                <dt>Venue</dt>
                <dd>{{ subject.venue ?? '—' }}</dd>
                <dt>Locality</dt>
                <dd>{{ localities.label(subject.localityId) }}</dd>
                <dt>Event type</dt>
                <dd>{{ eventTypeLabel(subject) ?? 'Not specified' }}</dd>
                <dt>Registration</dt>
                <dd>
                  @if (!subject.registrationEnabled) {
                    Not needed
                  } @else if (subject.capacity !== null) {
                    {{ subject.registeredCount }} of {{ subject.capacity }} places taken
                    @if (subject.waitlistedCount > 0) { · {{ subject.waitlistedCount }} waiting }
                  } @else {
                    {{ subject.registeredCount }} registered, no limit
                  }
                </dd>
              </dl>
            </div>
          }
        }
      </div>

      <!-- Edit: the event and its photo ------------------------------------- -->
      @if (editOpen()) {
        <div class="modal-backdrop" (click)="closeEditModal()">
          <div class="card modal-card modal-dialog" role="dialog" aria-modal="true"
            aria-labelledby="edit-event-title" (click)="$event.stopPropagation()"
            (keydown.escape)="closeEditModal()">
            <h2 id="edit-event-title">Edit {{ subject.title }}</h2>

            <div class="tabs" role="tablist" aria-label="What to edit">
              <button type="button" role="tab" [class.on]="editPart() === 'details'"
                [attr.aria-selected]="editPart() === 'details'" (click)="editPart.set('details')">
                Details
              </button>
              <button type="button" role="tab" [class.on]="editPart() === 'photo'"
                [attr.aria-selected]="editPart() === 'photo'" (click)="editPart.set('photo')">
                Photo
              </button>
            </div>

            @if (editError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            @if (editPart() === 'details') {
              <form (ngSubmit)="saveDetails(subject)">
                <div class="form-grid">
                  <div class="field span-all">
                    <label for="edit-title">Title</label>
                    <input id="edit-title" class="input" name="title" [(ngModel)]="title" maxlength="200" />
                  </div>

                  <div class="field span-all">
                    <label for="edit-description">Description</label>
                    <textarea id="edit-description" class="input" name="description" rows="3"
                      [(ngModel)]="description" maxlength="5000"></textarea>
                  </div>

                  <div class="field">
                    <label for="edit-venue">Venue</label>
                    <input id="edit-venue" class="input" name="venue" [(ngModel)]="venue" maxlength="300" />
                  </div>

                  <!-- An Area Admin's events are always in their own locality: shown, not offered. -->
                  <div class="field">
                    <label for="edit-locality">Locality</label>
                    <select id="edit-locality" class="input" name="locality" [(ngModel)]="localityId"
                      [disabled]="area.localityId() !== null">
                      @if (area.localityId() === null) {
                        <option value="">The whole Samaaj</option>
                      }
                      @for (option of localities.options(); track option.id) {
                        <option [value]="option.id">{{ option.label }}</option>
                      }
                    </select>
                  </div>

                  <div class="field">
                    <label for="edit-start">Starts</label>
                    <input id="edit-start" class="input" type="datetime-local" name="startAt" [(ngModel)]="startAt" />
                  </div>

                  <div class="field">
                    <label for="edit-end">Ends</label>
                    <input id="edit-end" class="input" type="datetime-local" name="endAt" [(ngModel)]="endAt"
                      [attr.aria-invalid]="endsBeforeItStarts() ? 'true' : null" />
                  </div>

                  @if (endsBeforeItStarts()) {
                    <p class="notice span-all" role="alert">An event cannot end before it starts.</p>
                  }

                  <div class="field">
                    <label for="edit-type">Event type</label>
                    <select id="edit-type" class="input" name="eventType" [(ngModel)]="eventTypeId">
                      <option value="">Not specified</option>
                      @for (option of eventTypes(); track option.id) {
                        <option [value]="option.id">{{ option.label }}</option>
                      }
                    </select>
                  </div>

                  <label class="inline-check span-all">
                    <input type="checkbox" name="registrationEnabled" [(ngModel)]="registrationEnabled" />
                    Members may register
                  </label>

                  @if (registrationEnabled) {
                    <div class="field span-all">
                      <label for="edit-capacity">Capacity</label>
                      <input id="edit-capacity" class="input inline" type="number" min="1" name="capacity"
                        [(ngModel)]="capacity" placeholder="Leave blank for no limit" />
                      @if (subject.registeredCount > 0) {
                        <p class="small">Cannot be set below the {{ subject.registeredCount }} people already registered.</p>
                      }
                    </div>
                  }
                </div>

                <div class="actions">
                  <button class="btn" type="submit" [disabled]="busy() || !canSaveDetails()">Save</button>
                  <button class="btn alt" type="button" (click)="closeEditModal()">Cancel</button>
                </div>
              </form>
            } @else {
              @if (subject.photoUrl; as path) {
                <img class="event-photo-preview" [scAuthedSrc]="path" alt="The event's current photo" />
              } @else {
                <p class="empty">No photo yet - the page shows the date instead.</p>
              }

              <label for="event-photo">{{ subject.photoUrl ? 'Replace the photo' : 'Choose a photo' }}</label>
              <input class="input" id="event-photo" type="file" accept="image/jpeg,image/png,image/webp"
                [disabled]="busy()" (change)="choosePhoto(subject, $event)" />
              <p class="small">JPEG, PNG or WebP, up to 2 MB. A wide picture suits a cover best.</p>

              <div class="actions">
                @if (subject.photoUrl) {
                  <button class="btn alt" type="button" [disabled]="busy()" (click)="removePhoto(subject)">
                    Remove the photo
                  </button>
                }
                <button class="btn alt" type="button" (click)="closeEditModal()">Done</button>
              </div>
            }
          </div>
        </div>
      }

      <!-- A programme ------------------------------------------------------ -->
      @if (scheduleModal(); as mode) {
        <div class="modal-backdrop" (click)="closeScheduleModal()">
          <div class="card modal-card modal-dialog" role="dialog" aria-modal="true"
            aria-labelledby="schedule-modal-title" (click)="$event.stopPropagation()"
            (keydown.escape)="closeScheduleModal()">
            <h2 id="schedule-modal-title">{{ mode === 'add' ? 'Add a programme' : 'Edit the programme' }}</h2>

            @if (scheduleError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <form (ngSubmit)="saveScheduleItem(subject)">
              <div class="form-grid">
                <div class="field span-all">
                  <label for="programme-name">Programme name</label>
                  <input id="programme-name" class="input" name="programName" [(ngModel)]="programName"
                    maxlength="200" placeholder="Pravachan" />
                </div>
                <div class="field">
                  <label for="programme-date">Date</label>
                  <input id="programme-date" class="input" type="date" name="programDate" [(ngModel)]="programDate"
                    [min]="firstDay(subject)" [max]="lastDay(subject)"
                    [attr.aria-invalid]="dateOutsideEvent(subject) ? 'true' : null" />
                </div>
                <div class="field">
                  <label for="programme-start">Start time</label>
                  <input id="programme-start" class="input" type="time" name="programStart" [(ngModel)]="programStart" />
                </div>
                <div class="field">
                  <label for="programme-end">End time</label>
                  <input id="programme-end" class="input" type="time" name="programEnd" [(ngModel)]="programEnd"
                    [attr.aria-invalid]="endsBeforeStart() ? 'true' : null" />
                  <p class="small">Optional.</p>
                </div>

                @if (dateOutsideEvent(subject)) {
                  <p class="notice span-all" role="alert">
                    A programme has to fall on one of the event's own days: {{ dayRange(subject) }}.
                    @if (!subject.endAt) {
                      Give the event an end date to schedule more days.
                    }
                  </p>
                }

                @if (endsBeforeStart()) {
                  <p class="notice span-all" role="alert">A programme cannot end before it starts.</p>
                }
              </div>

              <div class="actions">
                <button class="btn" type="submit" [disabled]="busy() || !canSaveScheduleItem(subject)">
                  {{ mode === 'add' ? 'Add' : 'Save' }}
                </button>
                <button class="btn alt" type="button" (click)="closeScheduleModal()">Cancel</button>
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

    .event-cover {
      display: flex;
      align-items: center;
      justify-content: flex-start;
      min-height: 120px;
      padding: var(--space-4);
      margin-bottom: var(--space-3);
      border-radius: var(--radius-md, 12px);
      overflow: hidden;
      background: linear-gradient(135deg, var(--soft), var(--panel));
    }

    .event-cover.cancelled {
      background: var(--danger-soft);
    }

    .event-cover:has(.event-cover-img) {
      padding: 0;
    }

    .event-cover-img {
      display: block;
      width: 100%;
      max-height: 240px;
      object-fit: cover;
    }

    .event-date-badge {
      display: flex;
      flex-direction: column;
      align-items: center;
      width: 72px;
      border-radius: 10px;
      overflow: hidden;
      background: var(--panel);
      border: 1px solid var(--line);
    }

    .event-date-month {
      display: block;
      width: 100%;
      padding: 2px 0;
      text-align: center;
      font-size: 0.75rem;
      font-weight: 700;
      text-transform: uppercase;
      color: var(--panel);
      background: var(--accent);
    }

    .event-date-day {
      padding: 4px 0 8px;
      font-size: 1.8rem;
      font-weight: 700;
      line-height: 1.1;
    }

    .event-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      gap: var(--space-3);
      flex-wrap: wrap;
      margin-bottom: var(--space-3);
    }

    .event-head-text {
      min-width: 0;
    }

    .event-head-actions {
      display: flex;
      gap: var(--space-2);
    }

    .inline-check {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      margin: var(--space-3) 0;
    }

    .event-head-text .title {
      overflow-wrap: anywhere;
    }

    .badges {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin: var(--space-2) 0 0;
    }

    .event-body {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    .event-photo-preview {
      display: block;
      width: 100%;
      max-height: 180px;
      object-fit: cover;
      border-radius: 10px;
      margin-bottom: var(--space-3);
    }

    .facts {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: var(--space-2) var(--space-4);
      margin: var(--space-3) 0 0;
    }

    .facts dt {
      font-weight: 600;
    }

    .facts dd {
      margin: 0;
      color: var(--muted);
      overflow-wrap: anywhere;
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
      flex-wrap: wrap;
    }
  `,
})
export class EventDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);
  readonly area = inject(AreaScope);
  readonly localities = inject(LocalityOptions);

  readonly event = signal<SamaajEvent | null>(null);
  readonly attendees = signal<readonly Attendee[]>([]);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly busy = signal(false);

  readonly tab = signal<EventTab>('about');

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());
  readonly eventTypes = signal<readonly LookupItem[]>([]);

  readonly registered = computed(() => this.attendees().filter((a) => a.status === 'Registered'));
  readonly waitlisted = computed(() => this.attendees().filter((a) => a.status === 'Waitlisted'));
  readonly cancelled = computed(() => this.attendees().filter((a) => a.status === 'Cancelled'));

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.load();
    this.localities.load();
    this.api.listLookupItems('EventType').subscribe({
      next: (found) => this.eventTypes.set(found),
      error: () => this.eventTypes.set([]),
    });
  }

  /** Going only for whoever may read the attendee list - the service answers 404 to anyone else. */
  tabsFor(subject: SamaajEvent): readonly { value: EventTab; label: string }[] {
    return [
      { value: 'about', label: 'About' },
      { value: 'schedule', label: `Schedule (${this.schedule(subject).length})` },
      ...(this.area.administers(subject.localityId)
        ? [{ value: 'going' as const, label: subject.registrationEnabled ? `Going (${this.registered().length})` : 'Going' }]
        : []),
    ];
  }

  /**
   * The service's own rule, asked here so the controls are not ones that always
   * refuse: an event in this caller's locality that is not cancelled.
   */
  canEdit(subject: SamaajEvent): boolean {
    return subject.status !== 'Cancelled' && this.area.administers(subject.localityId);
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  /** Null when the event has none, or the type has since been deactivated. */
  eventTypeLabel(event: SamaajEvent): string | null {
    if (!event.eventTypeId) {
      return null;
    }

    return this.eventTypes().find((t) => t.id === event.eventTypeId)?.label ?? null;
  }

  // ---- Edit: the event and its photo -----------------------------------------

  readonly editOpen = signal(false);
  readonly editPart = signal<'details' | 'photo'>('details');
  readonly editError = signal<string | null>(null);

  title = '';
  description = '';
  venue = '';
  startAt = '';
  endAt = '';
  eventTypeId = '';
  registrationEnabled = true;
  capacity: number | null = null;
  localityId = '';

  openEditModal(subject: SamaajEvent): void {
    this.title = subject.title;
    this.description = subject.description ?? '';
    this.venue = subject.venue ?? '';
    this.startAt = toLocalInputValue(subject.startAt);
    this.endAt = subject.endAt ? toLocalInputValue(subject.endAt) : '';
    this.eventTypeId = subject.eventTypeId ?? '';
    this.registrationEnabled = subject.registrationEnabled;
    this.capacity = subject.capacity;
    this.localityId = subject.localityId ?? '';
    this.editError.set(null);
    this.editPart.set('details');
    this.editOpen.set(true);
  }

  closeEditModal(): void {
    this.editOpen.set(false);
  }

  endsBeforeItStarts(): boolean {
    return this.startAt !== '' && this.endAt !== '' && new Date(this.endAt) <= new Date(this.startAt);
  }

  canSaveDetails(): boolean {
    return this.title.trim().length > 0 && this.startAt.length > 0 && !this.endsBeforeItStarts();
  }

  saveDetails(subject: SamaajEvent): void {
    if (!this.canSaveDetails()) {
      return;
    }

    this.run(
      this.api.updateEvent(subject.id, {
        title: this.title.trim(),
        description: this.description.trim() || null,
        startAt: new Date(this.startAt).toISOString(),
        endAt: this.endAt ? new Date(this.endAt).toISOString() : null,
        venue: this.venue.trim() || null,
        eventTypeId: this.eventTypeId || null,
        registrationEnabled: this.registrationEnabled,
        // Blank is no limit, never zero - zero is an event nobody can attend.
        capacity: this.registrationEnabled ? this.capacity || null : null,
        localityId: this.localityId || null,
      }),
      'Saved.',
      this.editError,
      () => this.closeEditModal(),
    );
  }

  choosePhoto(subject: SamaajEvent, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    input.value = '';

    if (!file) {
      return;
    }

    // The service decides; this only saves a round trip for the common mistake.
    if (file.size > 2 * 1024 * 1024) {
      this.editError.set('A photo has to be 2 MB or smaller. Resize it and try again.');
      return;
    }

    this.run(this.api.uploadEventPhoto(subject.id, file), 'The event has a new photo.', this.editError);
  }

  removePhoto(subject: SamaajEvent): void {
    this.run(this.api.removeEventPhoto(subject.id), 'The photo is down.', this.editError);
  }

  // ---- The schedule ---------------------------------------------------------

  /** Add, edit the one `editingItemId` names, or neither. */
  readonly scheduleModal = signal<'add' | 'edit' | null>(null);
  readonly scheduleError = signal<string | null>(null);
  private editingItemId: string | null = null;

  programName = '';
  programDate = '';
  programStart = '';
  programEnd = '';

  schedule(subject: SamaajEvent): readonly EventScheduleItem[] {
    return subject.schedule ?? [];
  }

  /**
   * A schedule date as a local Date. DatePipe would read "2026-11-24" as UTC
   * midnight, which is the 23rd anywhere west of Greenwich.
   */
  localDay(date: string): Date {
    const [year = 1970, month = 1, day = 1] = date.split('-').map((part) => Number(part));

    return new Date(year, month - 1, day);
  }

  /** "18:30:00" as "6:30 PM" - wall-clock time, never converted between zones. */
  clock(time: string): string {
    const [hours = 0, minutes = 0] = time.split(':').map((part) => Number(part));
    const suffix = hours < 12 ? 'AM' : 'PM';
    const hour12 = hours % 12 === 0 ? 12 : hours % 12;

    return `${hour12}:${minutes.toString().padStart(2, '0')} ${suffix}`;
  }

  /** The event's first day, as "yyyy-MM-dd" on this organiser's calendar. */
  firstDay(subject: SamaajEvent): string {
    return toLocalDay(subject.startAt);
  }

  /** Its last day - the start day when it has no end. */
  lastDay(subject: SamaajEvent): string {
    return toLocalDay(subject.endAt ?? subject.startAt);
  }

  /** "24 Nov 2026", or "24 Nov 2026 to 26 Nov 2026" - the days a programme may fall on. */
  dayRange(subject: SamaajEvent): string {
    const format = (day: string) =>
      this.localDay(day).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
    const first = this.firstDay(subject);
    const last = this.lastDay(subject);

    return first === last ? format(first) : `${format(first)} to ${format(last)}`;
  }

  /** ISO "yyyy-MM-dd" strings compare correctly as text. */
  dateOutsideEvent(subject: SamaajEvent): boolean {
    return this.programDate !== '' && (this.programDate < this.firstDay(subject) || this.programDate > this.lastDay(subject));
  }

  openScheduleModal(item: EventScheduleItem | null): void {
    const subject = this.event();

    this.scheduleError.set(null);
    this.editingItemId = item?.id ?? null;
    this.programName = item?.programName ?? '';
    this.programDate = item?.date ?? (subject ? this.firstDay(subject) : '');
    this.programStart = item?.startTime.slice(0, 5) ?? '';
    this.programEnd = item?.endTime?.slice(0, 5) ?? '';
    this.scheduleModal.set(item ? 'edit' : 'add');
  }

  closeScheduleModal(): void {
    this.scheduleModal.set(null);
    this.editingItemId = null;
  }

  endsBeforeStart(): boolean {
    return this.programStart !== '' && this.programEnd !== '' && this.programEnd <= this.programStart;
  }

  canSaveScheduleItem(subject: SamaajEvent): boolean {
    return (
      this.programName.trim().length > 0 &&
      this.programDate !== '' &&
      this.programStart !== '' &&
      !this.endsBeforeStart() &&
      !this.dateOutsideEvent(subject)
    );
  }

  saveScheduleItem(subject: SamaajEvent): void {
    if (!this.canSaveScheduleItem(subject)) {
      return;
    }

    // `<input type="time">` gives "HH:mm"; the service takes "HH:mm:ss".
    const item = {
      date: this.programDate,
      programName: this.programName.trim(),
      startTime: `${this.programStart}:00`,
      endTime: this.programEnd ? `${this.programEnd}:00` : null,
    };

    const work = this.editingItemId
      ? this.api.updateEventScheduleItem(subject.id, this.editingItemId, item)
      : this.api.addEventScheduleItem(subject.id, item);

    this.run(work, this.editingItemId ? 'Programme saved.' : 'Programme added.', this.scheduleError, () =>
      this.closeScheduleModal(),
    );
  }

  removeScheduleItem(subject: SamaajEvent, item: EventScheduleItem): void {
    this.run(this.api.removeEventScheduleItem(subject.id, item.id), `${item.programName} is off the schedule.`, this.error);
  }

  // ---- Loading -------------------------------------------------------------

  /** Every change answers with the whole event, so the page redraws from what the service holds. */
  private run(
    work: { subscribe: (observer: { next: (updated: SamaajEvent | void) => void; error: (failure: unknown) => void }) => unknown },
    message: string,
    errorTo: { set: (value: string | null) => void },
    after?: () => void,
  ): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);
    errorTo.set(null);

    work.subscribe({
      next: (updated) => {
        if (updated) {
          this.event.set(updated);
        }

        this.done.set(message);
        this.busy.set(false);
        after?.();
      },
      error: (failure: unknown) => {
        errorTo.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    // One event, not the list filtered down. The service answers 404 for a
    // draft to anyone who cannot publish, which is the check this screen wants
    // applied — filtering a list client-side would have got the same rows and
    // asked the wrong question to get them.
    this.api.event(this.id).subscribe({
      next: (subject) => {
        this.event.set(subject);
        this.loading.set(false);

        // The attendee list answers 404 to an Area Admin for an event
        // elsewhere, so it is not asked for.
        if (this.area.administers(subject.localityId)) {
          this.loadAttendees();
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

  private loadAttendees(): void {
    this.api.attendees(this.id).subscribe({
      next: (found) => {
        this.attendees.set(found);
        this.loadNames();
      },
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  /** A failure leaves "A member" rather than a GUID, and is not an error. */
  private loadNames(): void {
    this.api.listMembers().subscribe({
      next: (found) => this.names.set(new Map(found.map((m) => [m.id, m.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }
}

type EventTab = 'about' | 'schedule' | 'going';

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

/** An ISO instant as the "yyyy-MM-dd" day it falls on in this browser's time zone. */
function toLocalDay(iso: string): string {
  const date = new Date(iso);
  const pad = (n: number) => n.toString().padStart(2, '0');

  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
