import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Competition } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * Competitions - the list, and setting one up.
 *
 * No wireframe covers this module; it is new this cycle. Shaped after
 * Celebrities / Voting's own list screen, the closest existing precedent: a
 * registration window instead of two nomination/voting windows, and no
 * results-visibility choice, because a competition's per-entry marks are
 * never shown to an ordinary member at all - only the published ranking is,
 * once a round is closed and its result published.
 */
@Component({
  selector: 'app-competition-list',
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    <h1 class="title">Competitions</h1>
    <p class="sub">Set a competition up, run its rounds, and publish results.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the competitions module. Switch it on under the
        Samaaj's settings.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else {
      <div class="card">
        <h2>Competitions</h2>

        @if (competitions().length === 0) {
          <p class="empty">
            None yet. A competition starts as a draft with registration not yet open.
          </p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Competitions</caption>
              <thead>
                <tr>
                  <th>Competition</th><th>Registration</th><th>Registered</th><th>Status</th>
                </tr>
              </thead>
              <tbody>
                @for (competition of competitions(); track competition.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/competitions', competition.id]">
                        <b>{{ competition.title }}</b>
                      </a>
                    </td>
                    <td>
                      {{ competition.registrationStartAt | date: 'd MMM' }} —
                      {{ competition.registrationEndAt | date: 'd MMM y' }}
                      @if (competition.acceptsRegistrations) {
                        <div class="muted">Open now</div>
                      }
                    </td>
                    <td>{{ competition.registrationCount }}</td>
                    <td>
                      <span class="pill" [class.warn]="competition.status === 'Draft'">
                        {{ competition.status }}
                      </span>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <p class="small">
            "Open now" means the status says so <b>and</b> the clock agrees. A competition left
            open past its registration close date because nobody moved it on still stops taking
            registrations.
          </p>
        }
      </div>

      <!-- Create --------------------------------------------------------- -->
      <div class="card spaced">
        <h2>Set up a competition</h2>

        <form (ngSubmit)="create()">
          <div class="form-grid">
            <div class="field">
              <label for="competition-title">Title</label>
              <input id="competition-title" class="input" name="title" [(ngModel)]="title"
                maxlength="200" placeholder="Rangoli Contest 2026" />
            </div>

            <div class="field">
              <label for="competition-description">Description</label>
              <input id="competition-description" class="input" name="description"
                [(ngModel)]="description" maxlength="2000" />
            </div>
          </div>

          <h3 class="section-heading">Registration</h3>
          <div class="form-grid">
            <div class="field">
              <label for="reg-start">Open</label>
              <input id="reg-start" class="input" type="datetime-local" name="registrationStartAt"
                [(ngModel)]="registrationStartAt" />
            </div>
            <div class="field">
              <label for="reg-end">Close</label>
              <input id="reg-end" class="input" type="datetime-local" name="registrationEndAt"
                [(ngModel)]="registrationEndAt"
                [attr.aria-invalid]="windowInvalid() ? 'true' : null" />
            </div>

            @if (windowInvalid()) {
              <p class="notice span-all" role="alert">
                Registration cannot close before it opens.
              </p>
            }
          </div>

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

    .section-heading {
      margin-top: var(--space-5);
    }
  `,
})
export class CompetitionListComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);

  readonly competitions = signal<readonly Competition[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  title = '';
  description = '';
  registrationStartAt = '';
  registrationEndAt = '';

  ngOnInit(): void {
    this.load();
  }

  windowInvalid(): boolean {
    if (!this.registrationStartAt || !this.registrationEndAt) {
      return false;
    }

    return new Date(this.registrationEndAt) <= new Date(this.registrationStartAt);
  }

  canCreate(): boolean {
    return (
      this.title.trim().length > 0 &&
      this.registrationStartAt.length > 0 &&
      this.registrationEndAt.length > 0 &&
      !this.windowInvalid()
    );
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.listCompetitions().subscribe({
      next: (found) => {
        this.competitions.set(found);
        this.loading.set(false);
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

  create(): void {
    if (!this.canCreate()) {
      return;
    }

    const title = this.title.trim();

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api
      .createCompetition({
        title,
        description: blankToNull(this.description),
        competitionTypeId: null,
        registrationStartAt: new Date(this.registrationStartAt).toISOString(),
        registrationEndAt: new Date(this.registrationEndAt).toISOString(),
      })
      .subscribe({
        next: () => {
          this.done.set(`${title} created as a draft. Registration is not open yet.`);
          this.busy.set(false);
          this.title = '';
          this.description = '';
          this.registrationStartAt = '';
          this.registrationEndAt = '';
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
