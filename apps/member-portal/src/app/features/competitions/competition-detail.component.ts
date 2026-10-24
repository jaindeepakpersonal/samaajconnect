import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { MemberLookup, initialsOf } from '../members/member-lookup';
import { City, Locality } from '../members/members.models';
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
 * One competition, laid out like a page (2026-10-24, the user's request): a
 * cover photo, the title, where it is held and what stage it is at, Register
 * or Withdraw - then three tabs. **Details** is what it is about, the
 * registration window and its judges; **Participants** is who has registered;
 * **Rounds** is the rounds, a selected round's entries, judging and result.
 *
 * Shaped after Celebrities of Samaaj's own campaign detail screen, with the
 * same three things worth keeping straight.
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
 *
 * **People are named, a page at a time.** Participants, judges and entrants
 * are ids on the wire; `MemberLookup` resolves the ones on screen - one call
 * per member, never repeated - and the Participants tab shows them a page at a
 * time, so a competition with hundreds registered is not hundreds of lookups
 * on opening a tab. A member this reader may not see is "A member".
 */
@Component({
  selector: 'app-competition-detail',
  imports: [FormsModule, RouterLink, AuthedImageDirective],
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
        <!-- The page head: cover, title, where, stage, and registering ----- -->
        <header class="card comp-hero">
          @if (found.competition.photoUrl; as path) {
            <img class="comp-cover" [scAuthedSrc]="path" alt="" />
          } @else {
            <span class="comp-cover placeholder" aria-hidden="true">{{ initials(found.competition.title) }}</span>
          }

          <div class="comp-hero-body">
            <h1 class="page-title">{{ found.competition.title }}</h1>

            <div class="badges">
              <span class="pill" [class]="pillClass(found.competition)">{{ stage(found.competition) }}</span>
              @if (found.competition.isJudge) {
                <span class="pill ok">You are a judge</span>
              }
            </div>

            @if (whereHeld(found.competition); as where) {
              <p class="comp-place">{{ where }}</p>
            }

            <p class="small">
              {{ found.competition.registrationCount }} registered ·
              {{ found.rounds.length }} {{ found.rounds.length === 1 ? 'round' : 'rounds' }} ·
              {{ found.judges.length }} {{ found.judges.length === 1 ? 'judge' : 'judges' }}
            </p>

            @if (registerError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            @if (registerMessage(); as message) {
              <p class="notice info" role="status">{{ message }}</p>
            }

            <div class="actions">
              @if (found.competition.myRegistrationStatus === 'Registered') {
                @if (found.competition.acceptsRegistrations) {
                  <span class="pill ok">You are registered</span>
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
          </div>
        </header>

        <div class="tabs" role="tablist" aria-label="This competition">
          @for (item of tabs; track item.value) {
            <button
              type="button"
              role="tab"
              [id]="'competition-tab-' + item.value"
              [class.on]="tab() === item.value"
              [attr.aria-selected]="tab() === item.value"
              aria-controls="competition-tab-panel"
              (click)="showTab(item.value)"
            >
              {{ item.label }}
              @if (item.value === 'participants') {
                ({{ participants().length }})
              } @else if (item.value === 'rounds') {
                ({{ found.rounds.length }})
              }
            </button>
          }
        </div>

        <div id="competition-tab-panel" role="tabpanel" [attr.aria-labelledby]="'competition-tab-' + tab()">
          @switch (tab()) {
            <!-- Participants ------------------------------------------------ -->
            @case ('participants') {
              <div class="card">
                <h2>Participants</h2>

                @if (participants().length === 0) {
                  <p class="small">Nobody has registered yet.</p>
                } @else {
                  <ul class="person-grid" aria-label="Registered participants">
                    @for (memberId of shownParticipants(); track memberId) {
                      <li class="person-card">
                        @if (lookup.member(memberId)?.photoUrl; as path) {
                          <img class="person-avatar" [scAuthedSrc]="path" alt="" />
                        } @else {
                          <span class="person-avatar initials" aria-hidden="true">{{ initials(nameFor(memberId)) }}</span>
                        }
                        <a [routerLink]="['/members', memberId]">{{ nameFor(memberId) }}</a>
                      </li>
                    }
                  </ul>

                  @if (participants().length > shownParticipants().length) {
                    <div class="actions">
                      <button class="btn small secondary" type="button" (click)="showMoreParticipants()">
                        Show more ({{ participants().length - shownParticipants().length }} more)
                      </button>
                    </div>
                  }
                }
              </div>
            }

            <!-- Rounds ------------------------------------------------------ -->
            @case ('rounds') {
              @if (found.rounds.length === 0) {
                <p class="notice info" role="status">No rounds have been opened yet.</p>
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

              <!-- Selected round: entries, judging, result ------------------ -->
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
                          <h2><a [routerLink]="['/members', entry.participantMemberId]">{{ nameFor(entry.participantMemberId) }}</a></h2>

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
                          <td><a [routerLink]="['/members', entry.participantMemberId]">{{ nameFor(entry.participantMemberId) }}</a></td>
                          <td>{{ entry.averageScore }}</td>
                        </tr>
                      }
                    </table>
                  </div>
                }
              }
            }

            <!-- Details ----------------------------------------------------- -->
            @default {
              <div class="card">
                <h2>About</h2>

                @if (found.competition.description; as description) {
                  <p class="comp-description">{{ description }}</p>
                } @else {
                  <p class="small">The organiser has not described it yet.</p>
                }

                <p class="notice info" role="status">{{ window(found.competition) }}</p>

                <dl class="details">
                  <dt>Registration opens</dt>
                  <dd>{{ date(found.competition.registrationStartAt) }}</dd>
                  <dt>Registration closes</dt>
                  <dd>{{ date(found.competition.registrationEndAt) }}</dd>
                  <dt>Where</dt>
                  <dd>{{ whereHeld(found.competition) ?? 'The whole Samaaj' }}</dd>
                </dl>
              </div>

              <div class="card spaced">
                <h2>Judges</h2>

                @if (found.judges.length === 0) {
                  <p class="small">No judges have been named yet.</p>
                } @else {
                  <ul class="person-grid" aria-label="Judges">
                    @for (judge of found.judges; track judge.id) {
                      <li class="person-card">
                        @if (lookup.member(judge.memberId)?.photoUrl; as path) {
                          <img class="person-avatar" [scAuthedSrc]="path" alt="" />
                        } @else {
                          <span class="person-avatar initials" aria-hidden="true">{{ initials(nameFor(judge.memberId)) }}</span>
                        }
                        <a [routerLink]="['/members', judge.memberId]">{{ nameFor(judge.memberId) }}</a>
                      </li>
                    }
                  </ul>
                }
              </div>
            }
          }
        </div>
      }
    </div>
  `,
})
export class CompetitionDetailComponent implements OnInit {
  private readonly api = inject(CompetitionsApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly masterData = inject(MasterDataApi);

  readonly lookup = inject(MemberLookup);

  readonly detail = signal<CompetitionDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  readonly registerError = signal<string | null>(null);
  readonly registerMessage = signal<string | null>(null);

  readonly tabs: readonly { value: CompetitionTab; label: string }[] = [
    { value: 'details', label: 'Details' },
    { value: 'participants', label: 'Participants' },
    { value: 'rounds', label: 'Rounds' },
  ];

  readonly tab = signal<CompetitionTab>('details');

  readonly cities = signal<readonly City[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  readonly selectedRoundId = signal<string | null>(null);
  readonly roundDetail = signal<RoundDetail | null>(null);
  readonly roundLoading = signal(false);
  readonly result = signal<CompetitionResult | null>(null);

  /** Per-entry failures, so one refusal does not blank the round. */
  readonly scoreError = signal<Record<string, string>>({});

  /** Draft score values a judge is typing, keyed by entry id. */
  readonly scoreDrafts: Record<string, number | null> = {};

  /** How many participants the Participants tab shows - a page at a time. */
  private readonly participantsLimit = signal(PARTICIPANTS_PAGE);

  private readonly me = computed(() => this.auth.user()?.userId ?? null);

  readonly participants = computed(() => this.detail()?.registeredMemberIds ?? []);

  readonly shownParticipants = computed(() => this.participants().slice(0, this.participantsLimit()));

  readonly selectedRound = computed<Round | null>(
    () => this.detail()?.rounds.find((r) => r.id === this.selectedRoundId()) ?? null,
  );

  ngOnInit(): void {
    this.masterData.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });
    this.masterData.listLocalities().subscribe({ next: (found) => this.localities.set(found), error: () => {} });

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

        // The judges are on the Details tab, which is where the page opens.
        this.lookup.resolve(this.others(found.judges.map((j) => j.memberId)));

        if (this.tab() === 'participants') {
          this.resolveShownParticipants();
        }
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  showTab(tab: CompetitionTab): void {
    this.tab.set(tab);

    if (tab === 'participants') {
      this.resolveShownParticipants();
    }
  }

  showMoreParticipants(): void {
    this.participantsLimit.update((limit) => limit + PARTICIPANTS_PAGE);
    this.resolveShownParticipants();
  }

  /** Selecting a round opens the Rounds tab, where its entries and result are. */
  selectRound(round: Round): void {
    this.tab.set('rounds');

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
        this.lookup.resolve(this.others(found.entries.map((e) => e.participantMemberId)));

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
      next: (found) => {
        this.result.set(found);
        this.lookup.resolve(this.others(found.ranking.map((r) => r.participantMemberId)));
      },
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

  /** "You" for the reader, the member's name once resolved, "A member" otherwise - never an id. */
  nameFor(memberId: string): string {
    if (memberId === this.me()) {
      return 'You';
    }

    return this.lookup.member(memberId)?.fullName ?? 'A member';
  }

  initials(name: string): string {
    return initialsOf(name);
  }

  /**
   * Where it is held, as one line: the venue, then the locality and the city or
   * village, each only if known. Null when none of them is.
   */
  whereHeld(competition: Competition): string | null {
    const area = [
      this.localities().find((l) => l.id === competition.localityId)?.name,
      this.cities().find((c) => c.id === competition.cityId)?.name,
    ].filter((part): part is string => !!part).join(', ');

    const parts = [competition.venue, area].filter((part): part is string => !!part && part.trim().length > 0);

    return parts.length > 0 ? parts.join(' · ') : null;
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

  /** Everybody but the reader, who is "You" and needs no lookup. */
  private others(ids: readonly string[]): string[] {
    return ids.filter((id) => id !== this.me());
  }

  private resolveShownParticipants(): void {
    this.lookup.resolve(this.others(this.shownParticipants()));
  }

  private setScoreError(id: string, message: string): void {
    this.scoreError.set({ ...this.scoreError(), [id]: message });
  }

  private clearScoreError(id: string): void {
    const { [id]: _removed, ...rest } = this.scoreError();

    this.scoreError.set(rest);
  }
}

type CompetitionTab = 'details' | 'participants' | 'rounds';

/** Participants shown, and looked up, at a time. */
const PARTICIPANTS_PAGE = 48;
