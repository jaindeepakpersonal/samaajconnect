import { Component, OnInit, WritableSignal, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { MemberLookup, initialsOf } from '../members/member-lookup';
import { City, Locality, LookupItem } from '../members/members.models';
import { CompetitionsApi } from './competitions.api';
import {
  Competition,
  CompetitionDetail,
  CompetitionFinalResult,
  CompetitionResult,
  JudgePanel,
  JudgingEntry,
  JudgingSheet,
  Round,
  RoundDetail,
  stageLabel,
} from './competitions.models';

/**
 * One competition, laid out like a page: a cover photo, the title, where it is
 * held and what stage it is at, Apply or Withdraw - then tabs (2026-10-24,
 * reshaped 2026-10-25).
 *
 * **Details** is what it is about: the type, the window, how it is judged -
 * rounds, max marks, the audition - and its judges. **Participants** is who
 * applied. **Rounds** is there only when the competition has rounds: one
 * with none is a single judging, and members see its top three and nothing
 * about rounds at all. **Results** is the winners once published. **Judging**
 * is drawn only for a judge: their own sheet, a mark and a comment for every
 * participant in each round their panel judges.
 *
 * **Individual marks are never shown to an ordinary member, before or after
 * publication.** Members see the winners and their totals; a judge sees only
 * the marks they gave; every judge's mark is the organiser's, in the admin
 * panel.
 *
 * **Applying twice is not an error.** The service reports it as success with
 * `registered: false`. Applying and withdrawing each ask first, in a popup,
 * because applying says what the member is signing up to - an audition date,
 * how many rounds.
 *
 * **A judge's mark is an upsert, not a one-shot vote.** Saving it again
 * corrects it while the round is open.
 *
 * **People are named, a page at a time**, through `MemberLookup` - one call
 * per member on screen, never repeated. A member this reader may not see is
 * "A member".
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
        @let subject = found.competition;

        <!-- The page head: cover, title, where, stage, and applying -------- -->
        <header class="card comp-hero">
          @if (subject.photoUrl; as path) {
            <img class="comp-cover" [scAuthedSrc]="path" alt="" />
          } @else {
            <span class="comp-cover placeholder" aria-hidden="true">{{ initials(subject.title) }}</span>
          }

          <div class="comp-hero-body">
            <h1 class="page-title">{{ subject.title }}</h1>

            <div class="badges">
              <span class="pill" [class]="pillClass(subject)">{{ stage(subject) }}</span>
              @if (typeName(subject); as type) {
                <span class="pill">{{ type }}</span>
              }
              @if (subject.isJudge) {
                <span class="pill ok">You are a judge</span>
              }
            </div>

            @if (whereHeld(subject); as where) {
              <p class="comp-place">{{ where }}</p>
            }

            <p class="small">
              {{ subject.registrationCount }} applied ·
              {{ format(subject) }}
            </p>

            @if (registerMessage(); as message) {
              <p class="notice ok" role="status">{{ message }}</p>
            }

            <div class="actions">
              @if (subject.myRegistrationStatus === 'Registered') {
                <span class="pill ok">You have applied</span>
                @if (subject.acceptsRegistrations) {
                  <button class="btn secondary" type="button" [disabled]="busy()" (click)="openPopup('withdraw')">
                    Withdraw
                  </button>
                }
              } @else if (subject.acceptsRegistrations) {
                <button class="btn" type="button" [disabled]="busy()" (click)="openPopup('apply')">
                  Apply as participant
                </button>
              }
            </div>
          </div>
        </header>

        <div class="tabs" role="tablist" aria-label="This competition">
          @for (item of tabs(); track item.value) {
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
                  <p class="small">Nobody has applied yet.</p>
                } @else {
                  <ul class="person-grid" aria-label="Participants">
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

            <!-- Rounds - only when it has rounds ---------------------------- -->
            @case ('rounds') {
              @if (found.rounds.length === 0) {
                <p class="notice info" role="status">
                  The rounds begin when judging starts: {{ plannedRounds(subject) }}.
                </p>
              } @else {
                <div class="grid">
                  @for (round of found.rounds; track round.id) {
                    <div class="card">
                      <h2>{{ round.name }}</h2>
                      <div class="badges">
                        <span class="pill" [class.warn]="round.status === 'Open'">
                          {{ round.status === 'Open' ? 'Under way' : 'Finished' }}
                        </span>
                        @if (round.hasPublishedResult) {
                          <span class="pill ok">Result published</span>
                        }
                      </div>
                      <p class="small">
                        {{ round.entryCount }} {{ round.entryCount === 1 ? 'participant' : 'participants' }}
                        @if (round.kind === 'Audition') { · the top {{ subject.finalistCount }} go through }
                      </p>
                      <div class="actions">
                        <button class="btn small secondary" type="button" (click)="showRound(round)">
                          View<span class="sr-only"> {{ round.name }}</span>
                        </button>
                      </div>
                    </div>
                  }
                </div>
              }
            }

            <!-- Results ----------------------------------------------------- -->
            @case ('results') {
              <div class="card">
                <h2>Results</h2>

                @if (finalResult(); as final) {
                  <p class="small">Announced {{ date(final.publishedAt) }}.</p>

                  <ol class="podium" aria-label="Winners">
                    @for (winner of final.winners; track winner.memberId) {
                      <li class="podium-place" [class.first]="winner.rank === 1">
                        <span class="podium-rank">{{ placeName(winner.rank) }}</span>
                        @if (lookup.member(winner.memberId)?.photoUrl; as path) {
                          <img class="person-avatar" [scAuthedSrc]="path" alt="" />
                        } @else {
                          <span class="person-avatar initials" aria-hidden="true">{{ initials(nameFor(winner.memberId)) }}</span>
                        }
                        <a [routerLink]="['/members', winner.memberId]"><b>{{ nameFor(winner.memberId) }}</b></a>
                        <span class="small">{{ points(winner.total) }} of {{ highestTotal(subject) }}</span>
                      </li>
                    }
                  </ol>
                } @else if (subject.status === 'Closed' || subject.status === 'InProgress') {
                  <p class="small">The winners are announced once judging finishes.</p>
                } @else {
                  <p class="small">Results are announced after the competition is judged.</p>
                }
              </div>
            }

            <!-- Judging - a judge's own sheet -------------------------------- -->
            @case ('judging') {
              @if (sheetLoading()) {
                <p role="status">Loading your sheet…</p>
              } @else if (sheetError(); as message) {
                <div class="notice error" role="alert">{{ message }}</div>
              } @else if (sheet(); as mine) {
                <p class="notice info" role="status">
                  You judge {{ panelLabel(mine.panel, subject) }}. Each mark is out of {{ mine.maxMarks }},
                  and you can correct it until the round closes. Nobody but the organiser sees your marks.
                </p>

                @if (mine.rounds.length === 0) {
                  <p class="small">Nothing to mark until judging starts.</p>
                }

                @for (sheetRound of mine.rounds; track sheetRound.round.id) {
                  <div class="card spaced">
                    <div class="section-head">
                      <h2>{{ sheetRound.round.name }}</h2>
                      <span class="pill" [class.warn]="sheetRound.canScore">{{ sheetRound.canScore ? 'Open for marks' : 'Closed' }}</span>
                    </div>

                    @if (sheetRound.entries.length === 0) {
                      <p class="small">
                        {{ sheetRound.round.kind === 'Main' && subject.auditionRequired ? 'The finalists appear here when the audition closes.' : 'Nobody to mark in this round.' }}
                      </p>
                    } @else {
                      <div class="table-scroll">
                        <table class="judging-table">
                          <caption class="sr-only">Your marks for {{ sheetRound.round.name }}</caption>
                          <tr>
                            <th scope="col">Participant</th>
                            <th scope="col">Mark</th>
                            <th scope="col">Comment</th>
                            <th scope="col"><span class="sr-only">Save</span></th>
                          </tr>
                          @for (entry of sheetRound.entries; track entry.entryId) {
                            <tr>
                              <td><a [routerLink]="['/members', entry.participantMemberId]">{{ nameFor(entry.participantMemberId) }}</a></td>
                              @if (sheetRound.canScore) {
                                <td>
                                  <label class="sr-only" [for]="'mark-' + entry.entryId">Mark for {{ nameFor(entry.participantMemberId) }}</label>
                                  <input class="input mark-input" type="number" min="0" [max]="mine.maxMarks"
                                    [id]="'mark-' + entry.entryId" [name]="'mark-' + entry.entryId"
                                    [(ngModel)]="marks[entry.entryId]"
                                    [attr.aria-invalid]="markProblem(entry, mine) ? 'true' : null" />
                                </td>
                                <td>
                                  <label class="sr-only" [for]="'comment-' + entry.entryId">Comment on {{ nameFor(entry.participantMemberId) }}</label>
                                  <input class="input" maxlength="500" [id]="'comment-' + entry.entryId"
                                    [name]="'comment-' + entry.entryId" [(ngModel)]="comments[entry.entryId]" />
                                </td>
                                <td>
                                  <button class="btn small" type="button" [disabled]="busy() || markProblem(entry, mine) !== null"
                                    (click)="saveMark(entry, mine)">
                                    {{ entry.myScore === null ? 'Save' : 'Update' }}<span class="sr-only"> the mark for {{ nameFor(entry.participantMemberId) }}</span>
                                  </button>
                                  @if (saved()[entry.entryId]) {
                                    <span class="pill ok">Saved</span>
                                  }
                                </td>
                              } @else {
                                <td>{{ entry.myScore ?? 'Not marked' }}</td>
                                <td>{{ entry.myComments ?? '' }}</td>
                                <td></td>
                              }
                            </tr>
                            @if (markErrors()[entry.entryId]; as message) {
                              <tr><td colspan="4"><p class="notice error" role="alert">{{ message }}</p></td></tr>
                            }
                          }
                        </table>
                      </div>
                    }
                  </div>
                }
              }
            }

            <!-- Details ----------------------------------------------------- -->
            @default {
              <div class="card">
                <h2>About</h2>

                @if (subject.description; as description) {
                  <p class="comp-description">{{ description }}</p>
                } @else {
                  <p class="small">The organiser has not described it yet.</p>
                }

                <p class="notice info" role="status">{{ window(subject) }}</p>

                <dl class="details">
                  <dt>Type</dt>
                  <dd>{{ typeName(subject) ?? 'Not specified' }}</dd>
                  <dt>Applications open</dt>
                  <dd>{{ date(subject.registrationStartAt) }}</dd>
                  <dt>Applications close</dt>
                  <dd>{{ date(subject.registrationEndAt) }}</dd>
                  <dt>Where</dt>
                  <dd>{{ whereHeld(subject) ?? 'The whole Samaaj' }}</dd>
                  <dt>Audition</dt>
                  <dd>
                    @if (subject.auditionRequired) {
                      {{ dateOnly(subject.auditionDate) }} - the top {{ subject.finalistCount }} go through to the competition
                    } @else {
                      None - everybody who applies takes part
                    }
                  </dd>
                  <dt>Rounds</dt>
                  <dd>{{ subject.roundCount === 0 ? 'One judging' : subject.roundCount }}</dd>
                  <dt>Marks</dt>
                  <dd>Out of {{ subject.maxMarks }} from each judge{{ subject.roundCount > 1 ? ', in every round' : '' }}</dd>
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
                        @if (subject.auditionRequired) {
                          <span class="small">{{ panelLabel(judge.panel, subject) }}</span>
                        }
                      </li>
                    }
                  </ul>
                }
              </div>
            }
          }
        </div>

        <!-- Popups ---------------------------------------------------------- -->
        @if (popup() === 'apply' || popup() === 'withdraw') {
          <div class="modal-backdrop" (click)="closePopup()">
            <div class="card modal-card modal-dialog" role="dialog" aria-modal="true" aria-labelledby="apply-popup-title"
              (click)="$event.stopPropagation()" (keydown.escape)="closePopup()">
              <h2 id="apply-popup-title">{{ popup() === 'apply' ? 'Apply as a participant' : 'Withdraw your application' }}</h2>

              @if (registerError(); as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              @if (popup() === 'apply') {
                <p>You are applying to take part in <b>{{ subject.title }}</b>.</p>
                <ul class="small">
                  @if (subject.auditionRequired) {
                    <li>There is an audition on {{ dateOnly(subject.auditionDate) }}; the top {{ subject.finalistCount }} go through.</li>
                  }
                  <li>{{ subject.roundCount === 0 ? 'It is judged once.' : 'It has ' + subject.roundCount + ' round(s), and every participant takes part in each.' }}</li>
                  <li>You can withdraw until applications close on {{ date(subject.registrationEndAt) }}.</li>
                </ul>
              } @else {
                <p class="notice warn" role="status">
                  You will no longer take part. You can apply again while applications are open.
                </p>
              }

              <div class="actions">
                @if (popup() === 'apply') {
                  <button class="btn" type="button" [disabled]="busy()" (click)="register()">Apply</button>
                } @else {
                  <button class="btn" type="button" [disabled]="busy()" (click)="withdraw()">Withdraw</button>
                }
                <button class="btn secondary" type="button" (click)="closePopup()">Cancel</button>
              </div>
            </div>
          </div>
        }

        @if (popup() === 'round') {
          @if (selectedRound(); as round) {
            <div class="modal-backdrop" (click)="closePopup()">
              <div class="card modal-card modal-dialog" role="dialog" aria-modal="true" aria-labelledby="round-popup-title"
                (click)="$event.stopPropagation()" (keydown.escape)="closePopup()">
                <h2 id="round-popup-title">{{ round.name }}</h2>

                @if (roundLoading()) {
                  <p role="status">Loading…</p>
                } @else if (roundResult(); as published) {
                  <p class="small">Result published {{ date(published.publishedAt) }}.</p>
                  <ol class="podium" aria-label="Ranking for {{ round.name }}">
                    @for (entry of published.ranking; track entry.entryId) {
                      <li class="podium-place">
                        <span class="podium-rank">{{ entry.rank }}</span>
                        <a [routerLink]="['/members', entry.participantMemberId]">{{ nameFor(entry.participantMemberId) }}</a>
                        <span class="small">{{ points(entry.averageScore) }} of {{ subject.maxMarks }}</span>
                      </li>
                    }
                  </ol>
                } @else if (roundDetail(); as rd) {
                  @if (rd.entries.length === 0) {
                    <p class="small">Nobody is in this round yet.</p>
                  } @else {
                    <ul class="person-grid" aria-label="Taking part in {{ round.name }}">
                      @for (entry of rd.entries; track entry.id) {
                        <li class="person-card">
                          <a [routerLink]="['/members', entry.participantMemberId]">{{ nameFor(entry.participantMemberId) }}</a>
                        </li>
                      }
                    </ul>
                    <p class="small">Marks are not shown until the result is published.</p>
                  }
                }

                <div class="actions">
                  <button class="btn secondary" type="button" (click)="closePopup()">Close</button>
                </div>
              </div>
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
  private readonly masterData = inject(MasterDataApi);

  readonly lookup = inject(MemberLookup);

  readonly detail = signal<CompetitionDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  readonly registerError = signal<string | null>(null);
  readonly registerMessage = signal<string | null>(null);

  /** Rounds only when it has rounds; Judging only for a judge. */
  readonly tabs = computed<readonly { value: CompetitionTab; label: string }[]>(() => {
    const competition = this.detail()?.competition;
    const tabs: { value: CompetitionTab; label: string }[] = [
      { value: 'details', label: 'Details' },
      { value: 'participants', label: 'Participants' },
    ];

    if ((competition?.roundCount ?? 0) > 0) {
      tabs.push({ value: 'rounds', label: 'Rounds' });
    }

    tabs.push({ value: 'results', label: 'Results' });

    if (competition?.isJudge) {
      tabs.push({ value: 'judging', label: 'Judging' });
    }

    return tabs;
  });

  readonly tab = signal<CompetitionTab>('details');

  readonly popup = signal<'apply' | 'withdraw' | 'round' | null>(null);

  readonly cities = signal<readonly City[]>([]);
  readonly localities = signal<readonly Locality[]>([]);
  readonly types = signal<readonly LookupItem[]>([]);

  readonly selectedRoundId = signal<string | null>(null);
  readonly roundDetail = signal<RoundDetail | null>(null);
  readonly roundLoading = signal(false);
  readonly roundResult = signal<CompetitionResult | null>(null);

  readonly finalResult = signal<CompetitionFinalResult | null>(null);

  /** The judge's own sheet, loaded when the Judging tab is opened. */
  readonly sheet = signal<JudgingSheet | null>(null);
  readonly sheetLoading = signal(false);
  readonly sheetError = signal<string | null>(null);

  /** What a judge is typing, by entry id. */
  readonly marks: Record<string, number | null> = {};
  readonly comments: Record<string, string> = {};

  /** Per-entry outcomes, so one refusal does not blank the sheet. */
  readonly markErrors = signal<Record<string, string>>({});
  readonly saved = signal<Record<string, boolean>>({});

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
    this.masterData.listCompetitionTypes().subscribe({ next: (found) => this.types.set(found), error: () => {} });

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

    this.loading.set(this.detail() === null);
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

        if (found.hasPublishedResult) {
          this.loadFinalResult(found.competition.id);
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

    if (tab === 'judging') {
      this.loadSheet();
    }
  }

  showMoreParticipants(): void {
    this.participantsLimit.update((limit) => limit + PARTICIPANTS_PAGE);
    this.resolveShownParticipants();
  }

  openPopup(which: 'apply' | 'withdraw'): void {
    this.registerError.set(null);
    this.registerMessage.set(null);
    this.popup.set(which);
  }

  closePopup(): void {
    this.popup.set(null);
    this.selectedRoundId.set(null);
  }

  /** A round's popup: who is in it, or its published ranking. */
  showRound(round: Round): void {
    const competitionId = this.detail()?.competition.id;

    if (!competitionId) {
      return;
    }

    this.selectedRoundId.set(round.id);
    this.popup.set('round');
    this.roundLoading.set(true);
    this.roundResult.set(null);
    this.roundDetail.set(null);

    if (round.hasPublishedResult) {
      this.api.roundResult(competitionId, round.id).subscribe({
        next: (found) => {
          this.roundResult.set(found);
          this.roundLoading.set(false);
          this.lookup.resolve(this.others(found.ranking.map((r) => r.participantMemberId)));
        },
        error: () => this.roundLoading.set(false),
      });
      return;
    }

    this.api.round(competitionId, round.id).subscribe({
      next: (found) => {
        this.roundDetail.set(found);
        this.roundLoading.set(false);
        this.lookup.resolve(this.others(found.entries.map((e) => e.participantMemberId)));
      },
      error: () => this.roundLoading.set(false),
    });
  }

  /** A 404 is "not published yet" - this is only asked once the detail says it is. */
  private loadFinalResult(competitionId: string): void {
    this.api.result(competitionId).subscribe({
      next: (found) => {
        this.finalResult.set(found);
        this.lookup.resolve(this.others(found.winners.map((w) => w.memberId)));
      },
      error: () => this.finalResult.set(null),
    });
  }

  private loadSheet(): void {
    const competitionId = this.detail()?.competition.id;

    if (!competitionId) {
      return;
    }

    this.sheetLoading.set(this.sheet() === null);
    this.sheetError.set(null);

    this.api.judging(competitionId).subscribe({
      next: (found) => {
        this.sheet.set(found);
        this.sheetLoading.set(false);

        for (const sheetRound of found.rounds) {
          for (const entry of sheetRound.entries) {
            this.marks[entry.entryId] = entry.myScore;
            this.comments[entry.entryId] = entry.myComments ?? '';
          }

          this.lookup.resolve(this.others(sheetRound.entries.map((e) => e.participantMemberId)));
        }
      },
      error: (failure: unknown) => {
        this.sheetError.set(describeError(failure));
        this.sheetLoading.set(false);
      },
    });
  }

  register(): void {
    const id = this.detail()?.competition.id;

    if (!id) {
      return;
    }

    this.busy.set(true);
    this.registerError.set(null);

    this.api.register(id).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.popup.set(null);
        this.registerMessage.set(result.registered ? 'You have applied.' : 'You had already applied.');
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

    this.api.withdraw(id).subscribe({
      next: () => {
        this.busy.set(false);
        this.popup.set(null);
        this.registerMessage.set('You have withdrawn. You can apply again while applications are open.');
        this.load();
      },
      error: (failure: unknown) => {
        this.registerError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  /** Said before sending, as the service would say it after. */
  markProblem(entry: JudgingEntry, sheet: JudgingSheet): string | null {
    const value = this.marks[entry.entryId];

    if (value === null || value === undefined || `${value}` === '') {
      return 'Give a mark.';
    }

    return Number.isInteger(value) && value >= 0 && value <= sheet.maxMarks
      ? null
      : `A whole number from 0 to ${sheet.maxMarks}.`;
  }

  saveMark(entry: JudgingEntry, sheet: JudgingSheet): void {
    const competitionId = this.detail()?.competition.id;
    const value = this.marks[entry.entryId];

    if (!competitionId || value === null || value === undefined || this.markProblem(entry, sheet)) {
      return;
    }

    this.busy.set(true);
    this.setFor(this.markErrors, entry.entryId, null);
    this.setFor(this.saved, entry.entryId, null);

    const comment = (this.comments[entry.entryId] ?? '').trim();

    this.api.recordScore(competitionId, entry.entryId, value, comment.length > 0 ? comment : null).subscribe({
      next: () => {
        this.busy.set(false);
        this.setFor(this.saved, entry.entryId, true);
        this.loadSheet();
      },
      error: (failure: unknown) => {
        this.setFor(this.markErrors, entry.entryId, describeError(failure));
        this.busy.set(false);
      },
    });
  }

  // ---- Rendering ---------------------------------------------------------

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

  typeName(competition: Competition): string | null {
    return this.types().find((t) => t.id === competition.competitionTypeId)?.label ?? null;
  }

  /** "Audition, then 3 rounds, out of 10" - the format in a line. */
  format(competition: Competition): string {
    const rounds = competition.roundCount === 0
      ? 'judged once'
      : `${competition.roundCount} ${competition.roundCount === 1 ? 'round' : 'rounds'}`;

    return `${competition.auditionRequired ? 'audition, then ' : ''}${rounds}, out of ${competition.maxMarks}`;
  }

  plannedRounds(competition: Competition): string {
    const main = competition.roundCount === 1 ? 'Round 1' : `Round 1 to ${competition.roundCount}`;

    return competition.auditionRequired ? `the audition, then ${main}` : main;
  }

  panelLabel(panel: JudgePanel | null, competition: Competition): string {
    switch (panel) {
      case 'Audition':
        return 'the audition';
      case 'Both':
        return 'the audition and the rounds';
      default:
        return competition.auditionRequired ? 'the rounds' : 'every participant';
    }
  }

  placeName(rank: number): string {
    return rank === 1 ? 'First' : rank === 2 ? 'Second' : 'Third';
  }

  /** The most a total can be: the max marks in every main round. */
  highestTotal(competition: Competition): number {
    return competition.maxMarks * Math.max(competition.roundCount, 1);
  }

  points(value: number): string {
    return Number.isInteger(value) ? `${value}` : value.toFixed(2);
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
      return `Applications are open until ${this.date(competition.registrationEndAt)}.`;
    }

    return competition.status === 'Closed'
      ? 'This competition is finished.'
      : 'Applications are not open right now.';
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }

  /** A \`yyyy-MM-dd\` date as the reader writes dates - read as a calendar day, not a UTC instant. */
  dateOnly(value: string | null): string {
    if (!value) {
      return '';
    }

    const [year, month, day] = value.split('-').map(Number);

    return this.date(new Date(year ?? 0, (month ?? 1) - 1, day ?? 1).toISOString());
  }

  /** Everybody but the reader, who is "You" and needs no lookup. */
  private others(ids: readonly string[]): string[] {
    return ids.filter((id) => id !== this.me());
  }

  private resolveShownParticipants(): void {
    this.lookup.resolve(this.others(this.shownParticipants()));
  }

  private setFor<T>(target: WritableSignal<Record<string, T>>, id: string, value: T | null): void {
    const { [id]: _removed, ...rest } = target();

    target.set(value === null ? rest : { ...rest, [id]: value });
  }
}

type CompetitionTab = 'details' | 'participants' | 'rounds' | 'results' | 'judging';

/** Participants shown, and looked up, at a time. */
const PARTICIPANTS_PAGE = 48;
