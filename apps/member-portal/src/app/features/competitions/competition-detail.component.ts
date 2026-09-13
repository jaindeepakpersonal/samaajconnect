import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { CompetitionsApi } from './competitions.api';
import {
  Competition,
  CompetitionDetail,
  CompetitionEntry,
  CompetitionResult,
  Round,
  RoundDetail,
  stageLabel,
} from './competitions.models';

/**
 * One competition: its rounds, this member's own registration, and - for an
 * assigned judge - the entries waiting for a mark.
 *
 * No wireframe covers this; the module is new this cycle. Shaped after
 * Celebrities of Samaaj's own campaign detail screen, with the same three
 * things worth keeping straight.
 *
 * **Individual marks are never shown to an ordinary member, before or after
 * publication.** `canSeeScores` on a round's detail is false for anyone who
 * is neither the organiser nor one of this competition's own assigned judges
 * - the screen shows who entered, never what they scored, until a round's
 * result is published, and even then only the rank and average, never a
 * single judge's own mark.
 *
 * **Registering twice is not an error.** The service reports it as success
 * with `registered: false`, the same shape a repeat vote takes in
 * celebrity-voting-service.
 *
 * **A judge's own score box is an upsert, not a one-shot vote.** Recording a
 * mark a second time corrects it rather than being refused - correcting a
 * misrecorded mark before judging closes is the ordinary case here.
 */
@Component({
  selector: 'app-competition-detail',
  imports: [FormsModule, RouterLink],
  styleUrl: './competitions.css',
  template: `
    <div class="competitions-page">
      <a class="back" routerLink="/competitions">‹ Back to Competitions</a>

      @if (loading()) {
        <p role="status">Loading the competition…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (detail(); as found) {
        <h1 class="page-title">{{ found.competition.title }}</h1>
        <p class="subtitle">
          <span class="pill" [class]="pillClass(found.competition)">
            {{ stage(found.competition) }}
          </span>
          {{ found.competition.registrationCount }} registered
        </p>

        @if (found.competition.description; as description) {
          <p>{{ description }}</p>
        }

        <p class="notice info" role="status">{{ window(found.competition) }}</p>

        <!-- Registering ---------------------------------------------------- -->
        <div class="actions">
          @if (registerError(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          @if (registerMessage(); as message) {
            <p class="notice info" role="status">{{ message }}</p>
          }

          @if (found.competition.myRegistrationStatus === 'Registered') {
            @if (found.competition.acceptsRegistrations) {
              <button class="btn secondary" type="button" [disabled]="busy()" (click)="withdraw()">
                Withdraw
              </button>
            } @else {
              <span class="pill ok">You are registered</span>
            }
          } @else if (found.competition.acceptsRegistrations) {
            <button class="btn" type="button" [disabled]="busy()" (click)="register()">
              Register
            </button>
          }
        </div>

        <!-- Rounds ----------------------------------------------------------- -->
        <h2 class="section-heading">Rounds</h2>

        @if (found.rounds.length === 0) {
          <p class="small">No rounds have been opened yet.</p>
        } @else {
          <div class="grid">
            @for (round of found.rounds; track round.id) {
              <div class="card" [class.mine]="selectedRoundId() === round.id">
                <h2>{{ round.name }}</h2>
                <div class="badges">
                  <span class="pill" [class.warn]="round.status === 'Open'">
                    {{ round.status }}
                  </span>
                  @if (round.hasPublishedResult) {
                    <span class="pill ok">Result published</span>
                  }
                </div>
                <p class="small">{{ round.entryCount }} entr{{ round.entryCount === 1 ? 'y' : 'ies' }}</p>
                <div class="actions">
                  <button class="btn small secondary" type="button" (click)="selectRound(round)">
                    {{ selectedRoundId() === round.id ? 'Hide' : 'View' }}
                  </button>
                </div>
              </div>
            }
          </div>
        }

        <!-- Selected round: entries, judging, result -------------------------- -->
        @if (selectedRound(); as round) {
          <h2 class="section-heading">{{ round.name }}</h2>

          @if (roundLoading()) {
            <p role="status">Loading…</p>
          } @else if (roundDetail(); as rd) {
            @if (rd.entries.length === 0) {
              <p class="small">Nobody has an entry in this round yet.</p>
            } @else {
              @if (!rd.canSeeScores) {
                <p class="small">
                  Marks are visible only to the organiser and this competition's own judges.
                </p>
              }

              <div class="grid">
                @for (entry of rd.entries; track entry.id) {
                  <div class="card">
                    <h2>{{ nameFor(entry.participantMemberId) }}</h2>

                    @if (rd.canSeeScores) {
                      @if (entry.averageScore !== null) {
                        <p class="stat">{{ entry.averageScore }}</p>
                        <p class="small">average so far</p>
                      } @else {
                        <p class="small">Not scored yet.</p>
                      }
                    }

                    @if (found.competition.isJudge && round.status === 'Open') {
                      <form class="score-form" (ngSubmit)="recordScore(entry)">
                        <label class="sr-only" [for]="'score-' + entry.id">
                          Score for {{ nameFor(entry.participantMemberId) }}
                        </label>
                        <input
                          class="input"
                          [id]="'score-' + entry.id"
                          type="number"
                          min="0"
                          max="100"
                          [(ngModel)]="scoreDrafts[entry.id]"
                          [name]="'score-' + entry.id"
                          placeholder="0-100"
                        />
                        <button class="btn small" type="submit" [disabled]="busy()">
                          {{ entry.myScore !== null ? 'Update mark' : 'Record mark' }}
                        </button>

                        @if (entry.myScore !== null) {
                          <span class="small">Your mark: {{ entry.myScore }}</span>
                        }
                      </form>

                      @if (scoreError()[entry.id]; as message) {
                        <p class="notice error" role="alert">{{ message }}</p>
                      }
                    }
                  </div>
                }
              </div>
            }
          }

          @if (result(); as published) {
            <h2 class="section-heading">Result</h2>
            <p class="small">
              Published {{ date(published.publishedAt) }} • locked after publication.
            </p>

            <div class="table-scroll">
              <table>
                <caption class="sr-only">Final ranking for {{ round.name }}</caption>
                <tr>
                  <th scope="col">Rank</th>
                  <th scope="col">Participant</th>
                  <th scope="col">Average</th>
                </tr>
                @for (entry of published.ranking; track entry.entryId) {
                  <tr>
                    <td>{{ entry.rank }}</td>
                    <td>{{ nameFor(entry.participantMemberId) }}</td>
                    <td>{{ entry.averageScore }}</td>
                  </tr>
                }
              </table>
            </div>
          }
        }
      }
    </div>
  `,
})
export class CompetitionDetailComponent implements OnInit {
  private readonly api = inject(CompetitionsApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly detail = signal<CompetitionDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  readonly registerError = signal<string | null>(null);
  readonly registerMessage = signal<string | null>(null);

  readonly selectedRoundId = signal<string | null>(null);
  readonly roundDetail = signal<RoundDetail | null>(null);
  readonly roundLoading = signal(false);
  readonly result = signal<CompetitionResult | null>(null);

  /** Per-entry failures, so one refusal does not blank the round. */
  readonly scoreError = signal<Record<string, string>>({});

  /** Draft score values a judge is typing, keyed by entry id. */
  readonly scoreDrafts: Record<string, number | null> = {};

  private readonly me = computed(() => this.auth.user()?.userId ?? null);

  readonly selectedRound = computed<Round | null>(
    () => this.detail()?.rounds.find((r) => r.id === this.selectedRoundId()) ?? null,
  );

  ngOnInit(): void {
    this.auth.ensureCurrentUser().subscribe({
      next: () => this.load(),
      error: () => this.load(),
    });
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name a competition.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.get(id).subscribe({
      next: (found) => {
        this.detail.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  selectRound(round: Round): void {
    if (this.selectedRoundId() === round.id) {
      this.selectedRoundId.set(null);
      return;
    }

    this.selectedRoundId.set(round.id);
    this.loadRound(round);
  }

  private loadRound(round: Round): void {
    const competitionId = this.detail()?.competition.id;

    if (!competitionId) {
      return;
    }

    this.roundLoading.set(true);
    this.result.set(null);

    this.api.round(competitionId, round.id).subscribe({
      next: (found) => {
        this.roundDetail.set(found);
        this.roundLoading.set(false);

        for (const entry of found.entries) {
          this.scoreDrafts[entry.id] = entry.myScore;
        }

        if (found.round.hasPublishedResult) {
          this.loadResult(competitionId, round.id);
        }
      },
      error: () => {
        this.roundDetail.set(null);
        this.roundLoading.set(false);
      },
    });
  }

  /** A 404 before a result exists is the normal state, not an error. */
  private loadResult(competitionId: string, roundId: string): void {
    this.api.roundResult(competitionId, roundId).subscribe({
      next: (found) => this.result.set(found),
      error: () => this.result.set(null),
    });
  }

  register(): void {
    const id = this.detail()?.competition.id;

    if (!id) {
      return;
    }

    this.busy.set(true);
    this.registerError.set(null);
    this.registerMessage.set(null);

    this.api.register(id).subscribe({
      next: (result) => {
        this.busy.set(false);

        this.registerMessage.set(
          result.registered ? 'You are registered.' : 'You were already registered.',
        );

        this.load();
      },
      error: (failure: unknown) => {
        this.registerError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  withdraw(): void {
    const id = this.detail()?.competition.id;

    if (!id) {
      return;
    }

    this.busy.set(true);
    this.registerError.set(null);
    this.registerMessage.set(null);

    this.api.withdraw(id).subscribe({
      next: () => {
        this.busy.set(false);
        this.registerMessage.set('You have withdrawn. You can register again while it is still open.');
        this.load();
      },
      error: (failure: unknown) => {
        this.registerError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  recordScore(entry: CompetitionEntry): void {
    const competitionId = this.detail()?.competition.id;
    const round = this.selectedRound();
    const value = this.scoreDrafts[entry.id];

    if (!competitionId || !round || value === null || value === undefined) {
      return;
    }

    this.busy.set(true);
    this.clearScoreError(entry.id);

    this.api.recordScore(competitionId, entry.id, value, null).subscribe({
      next: () => {
        this.busy.set(false);
        this.loadRound(round);
      },
      error: (failure: unknown) => {
        this.setScoreError(entry.id, describeError(failure));
        this.busy.set(false);
      },
    });
  }

  /** Ids, not names. The reader is the one identity this screen can resolve. */
  nameFor(memberId: string): string {
    return memberId === this.me() ? 'You' : 'A member';
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

  window(competition: Competition): string {
    if (competition.acceptsRegistrations) {
      return `Registration is open until ${this.date(competition.registrationEndAt)}.`;
    }

    return competition.status === 'Closed'
      ? 'This competition is finished.'
      : 'Registration is not open right now.';
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }

  private setScoreError(id: string, message: string): void {
    this.scoreError.set({ ...this.scoreError(), [id]: message });
  }

  private clearScoreError(id: string): void {
    const { [id]: _removed, ...rest } = this.scoreError();

    this.scoreError.set(rest);
  }
}
