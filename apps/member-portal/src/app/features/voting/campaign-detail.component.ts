import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { Subject, of, switchMap } from 'rxjs';
import { Member } from '../members/members.models';
import { MembersApi } from '../members/members.api';
import { VotingApi } from './voting.api';
import { Campaign, CampaignDetail, CampaignResult, Candidate, stageLabel } from './voting.models';

/**
 * One campaign: its ballot, this member's one vote, and the frozen result.
 *
 * This is the wireframe's `#celebrity` ballot and `#celebrityresults` table on
 * one screen, because they are the same campaign at two points in its life and
 * a member arriving after publication wants the result where the ballot was.
 *
 * Three things this screen has to keep straight.
 *
 * **A null vote count is not zero.** `tallyVisible` says whether this caller is
 * being shown counts at all. A campaign set to HiddenUntilClose shows a member
 * the names and no numbers until voting ends - printing 0 there would tell them
 * nobody had voted for someone, which is a claim and the wrong one.
 *
 * **Voting twice is not an error.** The service reports it as success with
 * `accepted: false`, carrying the vote they already hold, because pressing a
 * button twice is not misconduct. The unique index on (campaign, voter) is what
 * actually prevents the second vote.
 *
 * **A published result is read, not recomputed.** It comes from the stored
 * ranking, so it cannot move after the Samaaj has been told - the wireframe's
 * "Locked after publication".
 *
 * Names are member ids: names live in member-family-service and a ballot would
 * be a call per candidate. The screen marks the reader and otherwise shows the
 * category, which is what the wireframe's cards actually lead with.
 *
 * **Nominating no longer asks for a raw member id, and the category is no
 * longer optional.** Both changed the same cycle, from the same report: typing
 * a GUID copied off the directory was the whole reason a nomination read as
 * "not working" - a single mistyped character failed silently once, and before
 * that fix failed loudly with a raw 400 the service's own model binder threw
 * before validation ever ran (see celebrity-voting-service's own `CLAUDE.md`).
 * The nominee is now found by typing their name into `MembersApi.search()`,
 * the same directory search the Members screen already calls, and `memberId`
 * is set only once a result is picked - never typed by hand. A reason or
 * speciality is required for the same reason a Boli needs an opening bid: a
 * nomination with nothing said about it gave a reviewer nothing to judge.
 */
@Component({
  selector: 'app-campaign-detail',
  imports: [FormsModule, RouterLink],
  styleUrl: './voting.css',
  template: `
    <div class="voting-page">
      <a class="back" routerLink="/voting">‹ Back to Celebrities of Samaaj</a>

      @if (loading()) {
        <p role="status">Loading the campaign…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (detail(); as found) {
        <h1 class="page-title">{{ found.campaign.title }}</h1>
        <p class="subtitle">
          <span class="pill" [class]="pillClass(found.campaign)">
            {{ stage(found.campaign) }}
          </span>
          Top {{ found.campaign.topN }}
        </p>

        @if (found.campaign.description; as description) {
          <p>{{ description }}</p>
        }

        <p class="notice info" role="status">{{ window(found.campaign) }}</p>

        <!-- The published result --------------------------------------- -->
        @if (result(); as published) {
          <h2 class="section-heading">Result</h2>
          <p class="small">
            Published {{ date(published.publishedAt) }} • locked after publication.
          </p>

          <div class="table-scroll">
            <table>
              <caption class="sr-only">Final ranking</caption>
              <tr>
                <th scope="col">Rank</th>
                <th scope="col">Candidate</th>
                <th scope="col">Category</th>
                <th scope="col">Votes</th>
              </tr>
              @for (entry of published.ranking; track entry.candidateId) {
                <tr>
                  <td>{{ entry.rank }}</td>
                  <td><a [routerLink]="['/members', entry.memberId]">{{ nameFor(entry.memberId) }}</a></td>
                  <td>{{ categoryFor(entry.candidateId, found) }}</td>
                  <td>{{ entry.votes }}</td>
                </tr>
              }
            </table>
          </div>
        }

        <!-- Nominating -------------------------------------------------- -->
        @if (found.campaign.acceptsNominations) {
          <h2 class="section-heading">Put somebody forward</h2>

          <form class="card" (ngSubmit)="nominate(found.campaign)">
            <label for="nominee">Their name</label>
            <input
              class="input"
              id="nominee"
              name="nomineeQuery"
              [ngModel]="nomineeQuery()"
              (ngModelChange)="onNomineeQueryChange($event)"
              placeholder="Start typing a member's name"
              autocomplete="off"
              required
            />

            @if (nomineeSearching()) {
              <p class="small" role="status">Searching…</p>
            } @else if (nomineeResults().length > 0) {
              <ul class="suggestions" role="listbox" aria-label="Matching members">
                @for (match of nomineeResults(); track match.id) {
                  <li role="option">
                    <button type="button" class="btn link" (click)="selectNominee(match)">
                      {{ match.fullName }}
                    </button>
                  </li>
                }
              </ul>
            } @else if (nomineeQuery().trim().length >= 2 && memberId.length === 0) {
              <p class="small">No member found by that name.</p>
            }

            @if (memberId.length > 0) {
              <p class="small">
                Nominating <strong>{{ selectedNomineeName() }}</strong>.
                <button type="button" class="btn link" (click)="clearNominee()">Change</button>
              </p>
            }

            <p class="small">
              The directory is at <a routerLink="/members">Members</a>. A nomination goes to a
              reviewer before it reaches the ballot.
            </p>

            <label for="category">Reason or speciality</label>
            <input
              class="input"
              id="category"
              name="category"
              [(ngModel)]="category"
              maxlength="100"
              placeholder="Community service"
              required
            />

            @if (nominateError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            @if (nominateMessage(); as message) {
              <p class="notice ok" role="status">{{ message }}</p>
            }

            <div class="actions">
              <button class="btn" type="submit" [disabled]="busy() || !canNominate()">
                {{ busy() ? 'Sending…' : 'Nominate' }}
              </button>
            </div>
          </form>
        }

        <!-- The ballot -------------------------------------------------- -->
        <h2 class="section-heading">{{ result() ? 'The ballot' : 'Candidates' }}</h2>

        @if (ballot(found).length === 0) {
          <p class="small">
            @if (found.campaign.acceptsNominations) {
              Nobody is on the ballot yet. Nominations are still open.
            } @else {
              Nobody reached the ballot.
            }
          </p>
        } @else {
          @if (!found.tallyVisible) {
            <!-- The one flag that separates "no votes yet" from "you may not
                 see the votes". -->
            <p class="small">
              Vote counts are hidden until voting closes, so that early voters and late voters
              see the same thing.
            </p>
          }

          <div class="grid">
            @for (candidate of ballot(found); track candidate.id) {
              <div class="card" [class.mine]="isMyVote(candidate, found.campaign)">
                <h2><a [routerLink]="['/members', candidate.memberId]">{{ nameFor(candidate.memberId) }}</a></h2>
                <p>{{ candidate.category ?? 'No category given' }}</p>

                @if (found.tallyVisible && candidate.votes !== null) {
                  <p class="stat">{{ candidate.votes }}</p>
                  <p class="small">
                    {{ candidate.votes === 1 ? 'vote' : 'votes' }}
                  </p>
                }

                @if (voteError()[candidate.id]; as message) {
                  <p class="notice error" role="alert">{{ message }}</p>
                }

                <div class="actions">
                  @if (isMyVote(candidate, found.campaign)) {
                    <span class="pill ok">Your vote</span>
                  } @else if (found.campaign.acceptsVotes) {
                    @if (found.campaign.myVoteCandidateId === null) {
                      @if (isMe(candidate)) {
                        <!-- The service refuses it; saying so beats a 409. -->
                        <span class="small">You cannot vote for yourself.</span>
                      } @else {
                        <button
                          class="btn"
                          type="button"
                          [disabled]="busy()"
                          (click)="vote(found.campaign, candidate)"
                        >
                          Vote
                        </button>
                      }
                    } @else {
                      <span class="small">You have already voted.</span>
                    }
                  }
                </div>
              </div>
            }
          </div>
        }
      }
    </div>
  `,
})
export class CampaignDetailComponent implements OnInit {
  private readonly api = inject(VotingApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly members = inject(MembersApi);

  readonly detail = signal<CampaignDetail | null>(null);
  readonly result = signal<CampaignResult | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly busy = signal(false);

  /** Per-candidate failures, so one refusal does not blank the ballot. */
  readonly voteError = signal<Record<string, string>>({});

  readonly nominateError = signal<string | null>(null);
  readonly nominateMessage = signal<string | null>(null);

  /** What the nominee search box holds - not necessarily a chosen member. */
  readonly nomineeQuery = signal('');
  readonly nomineeResults = signal<readonly Member[]>([]);
  readonly nomineeSearching = signal(false);
  readonly selectedNomineeName = signal<string | null>(null);
  private readonly nomineeQuery$ = new Subject<string>();

  /** Set only by picking a search result, never typed directly. */
  memberId = '';
  category = '';

  private readonly me = computed(() => this.auth.user()?.userId ?? null);

  ngOnInit(): void {
    // The ballot marks the reader's own candidacy and their own vote, and both
    // need /me.
    this.auth.ensureCurrentUser().subscribe({
      next: () => this.load(),
      error: () => this.load(),
    });

    // switchMap rather than a debounce: this codebase has no debounced-input
    // infrastructure anywhere else, and cancelling the previous request on
    // every keystroke already stops a slow reply for "Rav" from clobbering a
    // faster one for "Ravi" - the actual race a debounce would guard against.
    this.nomineeQuery$
      .pipe(switchMap((term) => (term.trim().length < 2 ? of([]) : this.members.search(term))))
      .subscribe((found) => {
        this.nomineeSearching.set(false);
        this.nomineeResults.set(found);
      });
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name a campaign.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.get(id).subscribe({
      next: (found) => {
        this.detail.set(found);
        this.loading.set(false);

        if (found.campaign.status === 'Published') {
          this.loadResult(id);
        } else {
          this.result.set(null);
        }
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  /**
   * Only asked for once the campaign says it is published. Before that the
   * endpoint answers 404 by design, and a speculative call would put an
   * expected 404 on every visit.
   */
  private loadResult(id: string): void {
    this.api.result(id).subscribe({
      next: (found) => this.result.set(found),
      error: () => this.result.set(null),
    });
  }

  // ---- Nominating ---------------------------------------------------------

  /** Debounced: fires the search only after typing settles, not per keystroke. */
  onNomineeQueryChange(value: string): void {
    this.nomineeQuery.set(value);
    this.memberId = '';
    this.selectedNomineeName.set(null);
    this.nominateError.set(null);

    if (value.trim().length < 2) {
      this.nomineeResults.set([]);
      this.nomineeSearching.set(false);
      return;
    }

    this.nomineeSearching.set(true);
    this.nomineeQuery$.next(value);
  }

  selectNominee(member: Member): void {
    this.memberId = member.id;
    this.selectedNomineeName.set(member.fullName);
    this.nomineeQuery.set(member.fullName);
    this.nomineeResults.set([]);
  }

  /** Back to typing a different name - `memberId` is not carried over. */
  clearNominee(): void {
    this.memberId = '';
    this.selectedNomineeName.set(null);
    this.nomineeQuery.set('');
    this.nomineeResults.set([]);
  }

  canNominate(): boolean {
    return this.memberId.length > 0 && this.category.trim().length > 0;
  }

  // ---- Acting ------------------------------------------------------------

  nominate(campaign: Campaign): void {
    if (!this.canNominate()) {
      return;
    }

    this.busy.set(true);
    this.nominateError.set(null);
    this.nominateMessage.set(null);

    this.api.nominate(campaign.id, this.memberId, this.category.trim()).subscribe({
      next: (result) => {
        this.clearNominee();
        this.category = '';
        this.busy.set(false);

        // A repeat nomination is success, not an error - the second nominator
        // has done nothing wrong. The message says which happened.
        this.nominateMessage.set(
          result.nominated
            ? 'Put forward. A reviewer decides whether they reach the ballot.'
            : 'They had already been put forward. One candidacy each keeps a vote from splitting.',
        );

        this.load();
      },
      error: (failure: unknown) => {
        this.nominateError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  vote(campaign: Campaign, candidate: Candidate): void {
    this.busy.set(true);
    this.clearVoteError(candidate.id);

    this.api.vote(campaign.id, candidate.id).subscribe({
      next: () => {
        this.busy.set(false);

        // Re-read rather than patch: the counts have moved, and if the vote
        // was refused as a duplicate the campaign carries the vote actually
        // held.
        this.load();
      },
      error: (failure: unknown) => {
        this.setVoteError(candidate.id, describeError(failure));
        this.busy.set(false);
      },
    });
  }

  // ---- Rendering ---------------------------------------------------------

  /** Only what is actually on the ballot. A nomination is not a candidate. */
  ballot(detail: CampaignDetail): readonly Candidate[] {
    return detail.candidates.filter((candidate) => candidate.status === 'Approved');
  }

  isMyVote(candidate: Candidate, campaign: Campaign): boolean {
    return campaign.myVoteCandidateId === candidate.id;
  }

  isMe(candidate: Candidate): boolean {
    return this.me() !== null && candidate.memberId === this.me();
  }

  /** Ids, not names. The reader is the one identity this screen can resolve. */
  nameFor(memberId: string): string {
    return memberId === this.me() ? 'You' : 'A member';
  }

  categoryFor(candidateId: string, detail: CampaignDetail): string {
    return (
      detail.candidates.find((candidate) => candidate.id === candidateId)?.category ?? '—'
    );
  }

  stage(campaign: Campaign): string {
    return stageLabel(campaign);
  }

  pillClass(campaign: Campaign): string {
    if (campaign.status === 'Published') {
      return 'ok';
    }

    return campaign.acceptsVotes || campaign.acceptsNominations ? 'warn' : '';
  }

  /** What is open now, and until when - from the two flags, not the status. */
  window(campaign: Campaign): string {
    if (campaign.acceptsNominations) {
      return `Nominations are open until ${this.date(campaign.nominationEndAt)}.`;
    }

    if (campaign.acceptsVotes) {
      return `Voting is open until ${this.date(campaign.votingEndAt)}. One vote each.`;
    }

    return campaign.status === 'Published'
      ? 'This campaign is finished and its result is fixed.'
      : 'Nothing is open on this campaign right now.';
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }

  private setVoteError(id: string, message: string): void {
    this.voteError.set({ ...this.voteError(), [id]: message });
  }

  private clearVoteError(id: string): void {
    const { [id]: _removed, ...rest } = this.voteError();

    this.voteError.set(rest);
  }
}
