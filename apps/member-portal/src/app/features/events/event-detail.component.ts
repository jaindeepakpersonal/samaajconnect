import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { EventsApi } from './events.api';
import { describeWhen, hasPassed } from './events.format';
import { SamaajEvent } from './events.models';

/**
 * Event detail, from the member-portal wireframe's `#eventdetail` screen.
 *
 * The wireframe's two cards are both here: Details, with the capacity bar and
 * the RSVP button, and Your Status. Its "Capacity: 200 • Registered: 186" and
 * its 93% bar are real numbers from the event, not the prototype's.
 *
 * The RSVP button is one button doing one of four things, because from the
 * member's side there is one decision - am I coming? - and what it means
 * depends on a count they cannot see the current value of. RSVP and joining
 * the waitlist are the same call for the same reason: choosing between them
 * client-side would mean racing the count and sometimes asking for the wrong
 * one.
 *
 * The wireframe's "You'll receive a notification reminder 24 hours before" is
 * **not** reproduced. There is no notification channel on this platform yet
 * (`DEVELOPMENT_PLAN.md` Phase 1, still open), so printing it would be
 * promising something nothing sends.
 */
@Component({
  selector: 'app-event-detail',
  imports: [RouterLink],
  styleUrl: './events.css',
  template: `
    <div class="events-page">
      <a class="back" routerLink="/events">‹ Back to Events</a>

      @if (loading()) {
        <p role="status">Loading the event…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (event(); as found) {
        <h1 class="page-title">{{ found.title }}</h1>
        <p class="subtitle">{{ when(found) }}</p>

        @if (found.status === 'Cancelled') {
          <div class="notice error" role="alert">
            <strong>This event was cancelled.</strong>
            @if (found.cancellationReason; as reason) {
              {{ reason }}
            }
          </div>
        } @else if (past()) {
          <p class="notice info" role="status">This event has already happened.</p>
        }

        <div class="grid2">
          <!-- Details --------------------------------------------------- -->
          <div class="card">
            <h3>Details</h3>

            @if (found.description; as description) {
              <p class="event-body">{{ description }}</p>
            } @else {
              <p class="small">No description was given.</p>
            }

            @if (found.capacity; as capacity) {
              <p>
                <b>Capacity:</b> {{ capacity }} • <b>Registered:</b> {{ found.registeredCount }}
              </p>

              <div
                class="progress"
                role="progressbar"
                [attr.aria-valuenow]="found.registeredCount"
                [attr.aria-valuemin]="0"
                [attr.aria-valuemax]="capacity"
                [attr.aria-label]="'Places taken'"
              >
                <i [style.width.%]="fillPercent(found)"></i>
              </div>

              @if (found.waitlistedCount > 0) {
                <p class="small">
                  {{ found.waitlistedCount }}
                  {{ found.waitlistedCount === 1 ? 'person is' : 'people are' }} waiting.
                </p>
              }
            } @else if (found.registrationEnabled) {
              <!-- Null capacity is no limit, which is not a limit of zero. -->
              <p>
                <b>Registered:</b> {{ found.registeredCount }} • No limit on places.
              </p>
            }

            @if (actionError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <div class="actions">
              @if (!found.registrationEnabled) {
                <p class="small">This event does not need an RSVP. Just come along.</p>
              } @else if (found.status === 'Cancelled') {
                <p class="small">There is nothing to RSVP to.</p>
              } @else if (past()) {
                <p class="small">RSVPs are closed.</p>
              } @else if (isGoing(found)) {
                <button class="btn secondary" type="button" [disabled]="busy()" (click)="withdraw()">
                  {{ busy() ? 'Working…' : 'Cannot make it any more' }}
                </button>
              } @else if (isWaiting(found)) {
                <button class="btn secondary" type="button" [disabled]="busy()" (click)="withdraw()">
                  {{ busy() ? 'Working…' : 'Leave the waitlist' }}
                </button>
              } @else {
                <button class="btn" type="button" [disabled]="busy()" (click)="register()">
                  {{
                    busy()
                      ? 'Working…'
                      : found.isFull
                        ? 'Join the waitlist'
                        : 'RSVP — I am going'
                  }}
                </button>
              }
            </div>
          </div>

          <!-- Your status ----------------------------------------------- -->
          <div class="card">
            <h3>Your status</h3>

            <p>
              <span class="pill" [class]="statusClass(found)">{{ statusLabel(found) }}</span>
            </p>

            @if (isWaiting(found)) {
              <p class="small">
                @if (position(); as place) {
                  You are number {{ place }} in the queue. If somebody gives up a place, the
                  person who has waited longest gets it.
                } @else {
                  If somebody gives up a place, the person who has waited longest gets it.
                }
              </p>
            } @else if (isGoing(found)) {
              <p class="small">
                Your place is held. If you cannot make it, say so here - it goes to whoever has
                waited longest.
              </p>
            }

            @if (promotedFromWaitlist()) {
              <p class="notice info" role="status">
                Your place went to somebody who was waiting.
              </p>
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class EventDetailComponent implements OnInit {
  private readonly api = inject(EventsApi);
  private readonly route = inject(ActivatedRoute);

  readonly event = signal<SamaajEvent | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly busy = signal(false);

  /** Kept apart from `error` so a failed RSVP does not replace the event. */
  readonly actionError = signal<string | null>(null);

  /**
   * The queue place the register call reported.
   *
   * Only known from that response - the event itself carries the waitlist size,
   * not this member's place in it - so it is null on a fresh page load for a
   * member who was already waiting, and the screen says the general thing
   * instead of inventing a number.
   */
  readonly position = signal<number | null>(null);

  readonly promotedFromWaitlist = signal(false);

  readonly past = computed(() => {
    const found = this.event();

    return found !== null && hasPassed(found);
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name an event.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.get(id).subscribe({
      next: (found) => {
        this.event.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  register(): void {
    const found = this.event();

    if (found === null) {
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);
    this.promotedFromWaitlist.set(false);

    this.api.register(found.id).subscribe({
      next: (result) => {
        // The server's answer is what decides which of the two happened, and
        // the counts have moved, so the event is re-read rather than patched
        // from a guess.
        this.position.set(result.status === 'Waitlisted' ? result.position : null);
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.actionError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  withdraw(): void {
    const found = this.event();

    if (found === null) {
      return;
    }

    this.busy.set(true);
    this.actionError.set(null);

    this.api.cancelRegistration(found.id).subscribe({
      next: (result) => {
        // Somebody else got the place. Reported without naming them - who is
        // going is not this member's business once they have left.
        this.promotedFromWaitlist.set(result.promotedMemberId !== null);
        this.position.set(null);
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.actionError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  // ---- Rendering --------------------------------------------------------

  when(event: SamaajEvent): string {
    return describeWhen(event);
  }

  isGoing(event: SamaajEvent): boolean {
    return event.myRegistrationStatus === 'Registered';
  }

  isWaiting(event: SamaajEvent): boolean {
    return event.myRegistrationStatus === 'Waitlisted';
  }

  /**
   * How full the bar reads.
   *
   * Capped at 100. An event can hold more registrations than its capacity if
   * the capacity was lowered afterwards, and a bar wider than its track is a
   * rendering bug rather than information.
   */
  fillPercent(event: SamaajEvent): number {
    if (event.capacity === null || event.capacity <= 0) {
      return 0;
    }

    return Math.min(100, Math.round((100 * event.registeredCount) / event.capacity));
  }

  statusLabel(event: SamaajEvent): string {
    if (this.isGoing(event)) {
      return 'You are going';
    }

    if (this.isWaiting(event)) {
      return 'On the waitlist';
    }

    // Cancelled covers both "was going and pulled out" and "left the queue";
    // either way what is true now is that they are not registered.
    return 'Not registered';
  }

  statusClass(event: SamaajEvent): string {
    if (this.isGoing(event)) {
      return 'ok';
    }

    return this.isWaiting(event) ? 'warn' : '';
  }
}
