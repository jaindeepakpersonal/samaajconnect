import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AreaScope } from '../../core/area-scope';
import { Attendee, EventScheduleItem, LookupItem, SamaajEvent } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * One event, and who is coming.
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
 * **Names are resolved here, not by events-service.** It stores an attendee as
 * a member id and a status and should not hold more of a list of who is going
 * somewhere than it has to. The panel is already authenticated to the directory.
 */
@Component({
  selector: 'app-event-detail',
  imports: [DatePipe, FormsModule, RouterLink],
  template: `
    <p><a class="btn link" routerLink="/events">← All events</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such event in {{ scope.label() }}.</p>
    } @else if (event(); as subject) {
      <h1 class="title">{{ subject.title }}</h1>
      <p class="sub">
        {{ subject.startAt | date: 'd MMM y, h:mm a' }}
        @if (subject.venue) { · {{ subject.venue }} }
        @if (eventTypeLabel(subject); as type) { · {{ type }} }
        · {{ subject.status }}
      </p>

      @if (subject.status === 'Cancelled') {
        <p class="notice">
          This event is off.
          @if (subject.cancellationReason) {
            {{ subject.cancellationReason }}
          }
          The registrations below are kept, so the people who were coming can still be told.
        </p>
      }

      @if (subject.description) {
        <p>{{ subject.description }}</p>
      }

      <!-- Schedule -------------------------------------------------------- -->
      <div class="card">
        <div class="section-head">
          <h2>Schedule</h2>
          @if (canEditSchedule(subject)) {
            <button class="btn small" type="button" (click)="openScheduleModal(null)">Add a programme</button>
          }
        </div>

        @if (schedule(subject).length === 0) {
          <p class="empty">
            No programmes yet.
            @if (canEditSchedule(subject)) {
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
                      @if (canEditSchedule(subject)) {
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
                  <input id="programme-date" class="input" type="date" name="programDate" [(ngModel)]="programDate" />
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

                @if (endsBeforeStart()) {
                  <p class="notice span-all" role="alert">A programme cannot end before it starts.</p>
                }
              </div>

              <div class="actions">
                <button class="btn" type="submit" [disabled]="busy() || !canSaveScheduleItem()">
                  {{ mode === 'add' ? 'Add' : 'Save' }}
                </button>
                <button class="btn alt" type="button" (click)="closeScheduleModal()">Cancel</button>
              </div>
            </form>
          </div>
        </div>
      }

      @if (!area.administers(subject.localityId)) {
        <p class="notice" role="status">
          This event is in another locality. Who is going is for its own administrators.
        </p>
      } @else {
      <!-- Going ---------------------------------------------------------- -->
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
                      <td><b>{{ memberName(attendee.memberId) }}</b></td>
                      <td>{{ attendee.registeredAt | date: 'd MMM y, h:mm a' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      </div>

      <!-- Waiting -------------------------------------------------------- -->
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
                      <td><b>{{ memberName(attendee.memberId) }}</b></td>
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
                    <td>{{ memberName(attendee.memberId) }}</td>
                    <td>{{ attendee.registeredAt | date: 'd MMM y, h:mm a' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }
      }
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
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

  readonly event = signal<SamaajEvent | null>(null);
  readonly attendees = signal<readonly Attendee[]>([]);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());
  private readonly eventTypes = signal<readonly LookupItem[]>([]);

  readonly registered = computed(() => this.attendees().filter((a) => a.status === 'Registered'));
  readonly waitlisted = computed(() => this.attendees().filter((a) => a.status === 'Waitlisted'));
  readonly cancelled = computed(() => this.attendees().filter((a) => a.status === 'Cancelled'));

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.load();
    this.api.listLookupItems('EventType').subscribe({
      next: (found) => this.eventTypes.set(found),
      error: () => this.eventTypes.set([]),
    });
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  // ---- The schedule ---------------------------------------------------------

  /** Add, edit the one `editingItemId` names, or neither. */
  readonly scheduleModal = signal<'add' | 'edit' | null>(null);
  readonly scheduleError = signal<string | null>(null);
  readonly busy = signal(false);
  private editingItemId: string | null = null;

  programName = '';
  programDate = '';
  programStart = '';
  programEnd = '';

  schedule(subject: SamaajEvent): readonly EventScheduleItem[] {
    return subject.schedule ?? [];
  }

  /**
   * The service's own rule, asked here so the controls are not ones that always
   * refuse: an event in this caller's locality that is not cancelled.
   */
  canEditSchedule(subject: SamaajEvent): boolean {
    return subject.status !== 'Cancelled' && this.area.administers(subject.localityId);
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

  openScheduleModal(item: EventScheduleItem | null): void {
    this.scheduleError.set(null);
    this.editingItemId = item?.id ?? null;
    this.programName = item?.programName ?? '';
    this.programDate = item?.date ?? this.defaultDate();
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

  canSaveScheduleItem(): boolean {
    return (
      this.programName.trim().length > 0 &&
      this.programDate !== '' &&
      this.programStart !== '' &&
      !this.endsBeforeStart()
    );
  }

  saveScheduleItem(subject: SamaajEvent): void {
    if (!this.canSaveScheduleItem()) {
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

    this.busy.set(true);
    this.scheduleError.set(null);

    work.subscribe({
      next: (updated) => {
        this.event.set(updated);
        this.busy.set(false);
        this.closeScheduleModal();
      },
      error: (failure: unknown) => {
        this.scheduleError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  removeScheduleItem(subject: SamaajEvent, item: EventScheduleItem): void {
    this.busy.set(true);
    this.error.set(null);

    this.api.removeEventScheduleItem(subject.id, item.id).subscribe({
      next: (updated) => {
        this.event.set(updated);
        this.busy.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  /** The event's own start date, as "yyyy-MM-dd" in local time - where most programmes land. */
  private defaultDate(): string {
    const start = this.event()?.startAt;
    const date = start ? new Date(start) : new Date();
    const pad = (n: number) => n.toString().padStart(2, '0');

    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  }

  /** Null when the event has none, or the type has since been deactivated. */
  eventTypeLabel(event: SamaajEvent): string | null {
    if (!event.eventTypeId) {
      return null;
    }

    return this.eventTypes().find((t) => t.id === event.eventTypeId)?.label ?? null;
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
