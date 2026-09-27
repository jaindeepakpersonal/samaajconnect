import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError, formatRupees } from '@samaajconnect/shared';
import { TrustApi } from './trust.api';
import { Trust, TrustDue } from './trust.models';

/**
 * A Samaaj's local Trusts, one per locality - trust-service's own module,
 * with no wireframe to translate.
 *
 * Setting one up, curating its roster and running its elections are all a
 * Samaaj admin's `Trust.Manage` calls and live in the admin panel; this
 * screen only ever reads. Each card links to `/trust/:id`, which is where
 * the roster and the elections actually are.
 *
 * **What the member owes comes first when there is anything.** A Boli won in a
 * locality falls due to that locality's Trust; the winner sees the amount here
 * and pays it however the Trust takes payments - this app records nothing,
 * because a payment is received by a person, who marks it in the admin panel.
 * Failing to read it leaves the rest of the screen alone.
 */
@Component({
  selector: 'app-trusts-list',
  imports: [RouterLink, DatePipe],
  styleUrl: './trust.css',
  template: `
    <div class="trust-page">
      <header class="page-head">
        <h1 class="page-title">Trusts</h1>
        <p class="subtitle">
          Our Samaaj's local Trusts, one per locality, and who runs them.
        </p>
      </header>

      @if (loading()) {
        <p role="status">Loading Trusts…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (moduleOff()) {
        <p class="notice info" role="status">
          Trusts are not switched on for our Samaaj yet.
        </p>
      } @else if (trusts().length === 0) {
        <p class="notice info" role="status">Our Samaaj has not set up a Trust yet.</p>
      } @else {
        @if (dues().length > 0) {
          <div class="card dues" role="status">
            <h2>What you owe</h2>
            <ul class="plain-list">
              @for (due of dues(); track due.id) {
                <li>
                  <b>{{ rupees(due.amount) }}</b> to {{ trustName(due.trustId) }}
                  {{ due.sourceType === 'BoliResult' ? 'for a Boli you won' : 'for your pledge' }}
                  <span class="small">, due since {{ due.createdAt | date: 'd MMM y' }}</span>
                </li>
              }
            </ul>
            <p class="small">Pay the Trust as it asks; it marks the payment received, and it leaves this list.</p>
          </div>
        }

        <div class="grid">
          @for (trust of trusts(); track trust.id) {
            <div class="card">
              <h2><a [routerLink]="['/trust', trust.id]">{{ trust.name }}</a></h2>
              <p class="small">
                <span class="pill" [class.warn]="trust.status === 'Inactive'">
                  {{ trust.status }}
                </span>
              </p>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class TrustsListComponent implements OnInit {
  private readonly api = inject(TrustApi);

  readonly trusts = signal<readonly Trust[]>([]);
  readonly dues = signal<readonly TrustDue[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly moduleOff = signal(false);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.moduleOff.set(false);

    this.api.list().subscribe({
      next: (found) => {
        this.trusts.set(found);
        this.loading.set(false);

        // Nothing can be owed to a Trust that does not exist.
        if (found.length > 0) {
          this.loadDues();
        }
      },
      error: (failure: unknown) => {
        this.loading.set(false);

        // A 404 here can only mean the module gate: listing Trusts is a list,
        // so nothing about the endpoint can 404 for a per-item reason - the
        // same reasoning Timeline's own load() uses for its feed.
        if (isNotFoundStatus(failure)) {
          this.moduleOff.set(true);
        } else {
          this.error.set(describeError(failure));
        }
      },
    });
  }

  rupees(paise: number): string {
    return formatRupees(paise);
  }

  trustName(trustId: string): string {
    return this.trusts().find((t) => t.id === trustId)?.name ?? 'a Trust';
  }

  /** A courtesy on this screen, so a failure is silent rather than an error. */
  private loadDues(): void {
    this.api.myDues().subscribe({
      next: (found) => this.dues.set(found),
      error: () => this.dues.set([]),
    });
  }
}

function isNotFoundStatus(failure: unknown): boolean {
  return (
    typeof failure === 'object' &&
    failure !== null &&
    'status' in failure &&
    (failure as { status: unknown }).status === 404
  );
}
