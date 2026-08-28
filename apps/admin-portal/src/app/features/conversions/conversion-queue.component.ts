import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { ConversionRequest } from '../../core/admin.models';

/**
 * Adult Child Conversion Queue, from the admin wireframe's `#conversionqueue`
 * screen. Reached from the nav's "Families & Children", which is the only part
 * of that section with a backend today.
 *
 * Adult-child conversion was decided as admin-approved rather than
 * self-service: creating a platform login is not something a household should
 * do unilaterally. This screen is where that decision is made, so it asks for a
 * note and offers Reject as plainly as Approve - a queue whose only button is
 * Approve is a rubber stamp, not a decision.
 *
 * The wireframe's **Family** and **Age** columns are not here. The queue
 * endpoint returns neither, and both would mean a second call per row for
 * columns nobody acts on. The child's name, the identifier their account would
 * be created with, and when it was asked are what the decision turns on.
 */
@Component({
  selector: 'app-conversion-queue',
  imports: [FormsModule, DatePipe],
  template: `
    <h1 class="title">Adult Child Conversion Queue</h1>
    <p class="sub">
      Approving creates a login for a child who has turned 18, and preserves the family
      relationship and their historical records.
    </p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (decided(); as name) {
      <p class="notice ok" role="status">
        Decision recorded for {{ name }}. If it was approved, identity-tenant-service is creating
        the account now; it appears under Admin Users once the code has been redeemed.
      </p>
    }

    @if (needsSamaaj()) {
      <p class="notice">
        Conversion requests belong to a Samaaj. Choose one in the top bar to see its queue.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading the queue…</p>
    } @else if (requests().length === 0) {
      <p class="empty">Nothing is waiting for a decision.</p>
    } @else {
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Child</th>
              <th>Proposed login</th>
              <th>Requested</th>
              <th>Decision</th>
            </tr>
          </thead>
          <tbody>
            @for (request of requests(); track request.id) {
              <tr>
                <td>
                  <b>{{ request.childFullName }}</b>
                </td>
                <td>{{ request.mobileOrEmail }}</td>
                <td>{{ request.requestedAt | date: 'd MMM y' }}</td>
                <td>
                  <label class="sr-only" [for]="'note-' + request.id">
                    Note on the decision for {{ request.childFullName }}
                  </label>
                  <input
                    class="input note"
                    [id]="'note-' + request.id"
                    [(ngModel)]="notes[request.id]"
                    placeholder="e.g. Verified in person"
                  />

                  <div class="row-actions">
                    <button
                      class="btn small"
                      type="button"
                      [disabled]="busyId() !== null"
                      (click)="decide(request, true)"
                    >
                      Approve
                    </button>
                    <button
                      class="btn alt small"
                      type="button"
                      [disabled]="busyId() !== null"
                      (click)="decide(request, false)"
                    >
                      Reject
                    </button>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <p class="small">
        A note is recorded with the decision and is visible to whoever asked. Approving does not
        mark the child converted: that happens once the new member has redeemed their activation
        code, so a child record never claims an account nobody can sign in to.
      </p>

      <p class="sr-only" role="status">{{ busyId() ? 'Recording the decision' : '' }}</p>
    }
  `,
  styles: `
    .note {
      margin: 0 0 6px;
      min-width: 220px;
    }

    .row-actions {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
    }
  `,
})
export class ConversionQueueComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly scope = inject(AdminScope);

  readonly requests = signal<readonly ConversionRequest[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busyId = signal<string | null>(null);
  readonly decided = signal<string | null>(null);

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  /** Note per request id, keyed so two rows cannot share one box. */
  notes: Record<string, string> = {};

  ngOnInit(): void {
    if (this.needsSamaaj()) {
      this.loading.set(false);
      return;
    }

    this.load();
  }

  load(): void {
    this.loading.set(true);

    this.api.listConversionRequests().subscribe({
      next: (requests) => {
        this.requests.set(requests);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  decide(request: ConversionRequest, approve: boolean): void {
    this.busyId.set(request.id);
    this.error.set(null);
    this.decided.set(null);

    const note = this.notes[request.id]?.trim() ?? '';

    this.api.decideConversion(request.id, approve, note === '' ? null : note).subscribe({
      next: () => {
        // Off the queue: it is no longer pending, and the endpoint returns
        // only pending requests, so re-reading would be a second round trip
        // to learn what we already know.
        this.requests.set(this.requests().filter((r) => r.id !== request.id));
        this.busyId.set(null);
        this.decided.set(request.childFullName);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busyId.set(null);
      },
    });
  }
}
