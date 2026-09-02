import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Attendee, SamaajEvent } from '../../core/admin.models';
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
  imports: [DatePipe, RouterLink],
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
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }
  `,
})
export class EventDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);

  readonly event = signal<SamaajEvent | null>(null);
  readonly attendees = signal<readonly Attendee[]>([]);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  readonly registered = computed(() => this.attendees().filter((a) => a.status === 'Registered'));
  readonly waitlisted = computed(() => this.attendees().filter((a) => a.status === 'Waitlisted'));
  readonly cancelled = computed(() => this.attendees().filter((a) => a.status === 'Cancelled'));

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.load();
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
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
        this.loadAttendees();
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
