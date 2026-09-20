import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { TrustApi } from './trust.api';
import { Trust } from './trust.models';

/**
 * A Samaaj's local Trusts, one per locality - trust-service's own module,
 * with no wireframe to translate.
 *
 * Setting one up, curating its roster and running its elections are all a
 * Samaaj admin's `Trust.Manage` calls and live in the admin panel; this
 * screen only ever reads. Each card links to `/trust/:id`, which is where
 * the roster and the elections actually are.
 */
@Component({
  selector: 'app-trusts-list',
  imports: [RouterLink],
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
}

function isNotFoundStatus(failure: unknown): boolean {
  return (
    typeof failure === 'object' &&
    failure !== null &&
    'status' in failure &&
    (failure as { status: unknown }).status === 404
  );
}
