import { DatePipe } from '@angular/common';
import { Component, computed, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Competition } from '../../core/admin.models';
import { LocalityOptions } from '../../core/locality-options';
import { CompetitionDialogComponent } from './competition-dialog.component';
import { isNotFound } from '../../core/http-status';
import { ALL_LOCALITIES, LocalityFilterComponent, matchesLocality } from '../../core/locality-filter.component';

/**
 * Competitions - the list, and setting one up or correcting one in a popup,
 * the way Events are. Edit is offered only while competition-service would take
 * it: a Draft, or while registration is open.
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
  imports: [LocalityFilterComponent, DatePipe, RouterLink, CompetitionDialogComponent],
  template: `
    <div class="head">
      <div>
        <h1 class="title">Competitions</h1>
        <p class="sub">Set a competition up, run its rounds, and publish results.</p>
      </div>

      @if (!moduleOff() && !loading()) {
        <button class="btn" type="button" (click)="openCreate($event)">Set up a competition</button>
      }
    </div>

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

        <app-locality-filter [(value)]="localityFilter" inputId="competitions-locality" />

        @if (competitions().length > 0 && shownCompetitions().length === 0) {
          <p class="empty">Nothing here for that locality.</p>
        }

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
                  <th>Competition</th><th>Locality</th><th>Registration</th><th>Registered</th><th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (competition of shownCompetitions(); track competition.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/competitions', competition.id]">
                        <b>{{ competition.title }}</b>
                      </a>
                    </td>
                    <td>{{ localities.label(competition.localityId) }}</td>
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
                    <td>
                      @if (competition.status === 'Draft' || competition.status === 'RegistrationOpen') {
                        <button class="btn small alt" type="button" (click)="openEdit(competition, $event)">
                          Edit
                        </button>
                      }
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
    }

    @if (dialogOpen()) {
      <app-competition-dialog [competition]="editing()" (saved)="onSaved($event)" (closed)="closeDialog()" />
    }
  `,
  styles: `
    .head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

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

  /** The Locality filter - see LocalityFilterComponent. */
  readonly localityFilter = signal(ALL_LOCALITIES);

  readonly shownCompetitions = computed(() =>
    this.competitions().filter((competition) => matchesLocality(this.localityFilter(), competition.localityId)),
  );
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly localities = inject(LocalityOptions);

  readonly dialogOpen = signal(false);
  readonly editing = signal<Competition | null>(null);
  private dialogTrigger: HTMLElement | null = null;

  openCreate(event: MouseEvent): void {
    this.editing.set(null);
    this.openDialog(event);
  }

  openEdit(competition: Competition, event: MouseEvent): void {
    this.editing.set(competition);
    this.openDialog(event);
  }

  onSaved(message: string): void {
    this.closeDialog();
    this.done.set(message);
    this.load();
  }

  closeDialog(): void {
    this.dialogOpen.set(false);

    const trigger = this.dialogTrigger;
    this.dialogTrigger = null;

    if (trigger) {
      setTimeout(() => trigger.focus());
    }
  }

  private openDialog(event: MouseEvent): void {
    this.dialogTrigger = event.currentTarget as HTMLElement;
    this.done.set(null);
    this.dialogOpen.set(true);
  }

  ngOnInit(): void {
    this.localities.load();
    this.load();
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
}

