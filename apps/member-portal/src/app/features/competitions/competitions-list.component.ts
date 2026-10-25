import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { CompetitionsApi } from './competitions.api';
import { Competition, stageLabel } from './competitions.models';

/**
 * Competitions - browse, and see what needs a decision from you.
 *
 * No wireframe covers this module; it is new this cycle. Shaped after
 * Celebrities of Samaaj's own list screen: a Samaaj runs a competition
 * occasionally, not continuously, so the list of competitions is the way in
 * and everything else - registering, the rounds, judging, the result - lives
 * on the detail screen.
 *
 * **Reads `acceptsRegistrations`, never the status alone.** A competition is
 * only open for registration if its status says so *and* the clock agrees,
 * and only the server knows the time it is deciding against.
 */
@Component({
  selector: 'app-competitions-list',
  imports: [RouterLink],
  styleUrl: './competitions.css',
  template: `
    <div class="competitions-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Competitions</h1>
          <p class="subtitle">Apply as a participant, follow the rounds, and see who won.</p>
        </div>
      </header>

      @if (loading()) {
        <p role="status">Loading competitions…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (competitions().length === 0) {
        <p class="notice info" role="status">Your Samaaj has not run a competition yet.</p>
      } @else {
        @if (current().length > 0) {
          <div class="grid">
            @for (competition of current(); track competition.id) {
              <div class="card">
                <h2>{{ competition.title }}</h2>

                @if (competition.description; as description) {
                  <p>{{ description }}</p>
                }

                <div class="badges">
                  <span class="pill" [class]="pillClass(competition)">
                    {{ stage(competition) }}
                  </span>

                  @if (competition.myRegistrationStatus === 'Registered') {
                    <span class="pill ok">You have applied</span>
                  }

                  @if (competition.isJudge) {
                    <span class="pill warn">You are judging</span>
                  }
                </div>

                <p class="small">{{ describe(competition) }}</p>

                <div class="actions">
                  <a class="btn small" [routerLink]="['/competitions', competition.id]">
                    {{ action(competition) }}
                  </a>
                </div>
              </div>
            }
          </div>
        }

        @if (past().length > 0) {
          <h2 class="section-heading">Past competitions</h2>

          <div class="grid">
            @for (competition of past(); track competition.id) {
              <div class="card">
                <h2>{{ competition.title }}</h2>
                <div class="badges">
                  <span class="pill">{{ stage(competition) }}</span>
                </div>
                <div class="actions">
                  <a class="btn small secondary" [routerLink]="['/competitions', competition.id]">
                    View
                  </a>
                </div>
              </div>
            }
          </div>
        }
      }
    </div>
  `,
})
export class CompetitionsListComponent implements OnInit {
  private readonly api = inject(CompetitionsApi);

  readonly competitions = signal<readonly Competition[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /** Anything still running or waiting on a result. */
  readonly current = computed(() =>
    this.competitions().filter((competition) => competition.status !== 'Closed'),
  );

  readonly past = computed(() =>
    this.competitions().filter((competition) => competition.status === 'Closed'),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.list().subscribe({
      next: (found) => {
        this.competitions.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  stage(competition: Competition): string {
    return stageLabel(competition);
  }

  pillClass(competition: Competition): string {
    if (competition.status === 'Closed') {
      return 'ok';
    }

    return competition.acceptsRegistrations ? 'warn' : '';
  }

  describe(competition: Competition): string {
    if (competition.acceptsRegistrations) {
      return `Registration closes ${this.date(competition.registrationEndAt)}.`;
    }

    switch (competition.status) {
      case 'Draft':
        return 'Not open yet.';
      case 'RegistrationOpen':
        return 'Registration has closed.';
      case 'RegistrationClosed':
        return 'Waiting for judging to start.';
      case 'InProgress':
        return 'Judging is under way.';
      case 'Closed':
        return 'This competition is finished.';
    }
  }

  action(competition: Competition): string {
    if (competition.acceptsRegistrations) {
      return competition.myRegistrationStatus === 'Registered' ? 'View' : 'Apply';
    }

    return 'View';
  }

  private date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
