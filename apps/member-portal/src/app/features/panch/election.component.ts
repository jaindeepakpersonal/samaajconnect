import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { Subject, of, switchMap } from 'rxjs';
import { MembersApi } from '../members/members.api';
import { Member } from '../members/members.models';
import { PanchApi } from './panch.api';
import { Candidate, Election, ElectionDetail, ElectionResult, stageLabel } from './panch.models';

/**
 * One Panch election: nominate somebody, cast your one ballot, read the result.
 *
 * Four things this screen keeps straight.
 *
 * **A ballot is up to ten names, and it is final.** Members tick the candidates
 * they back and cast once; there is no changing it afterwards, which is why the
 * button says so. Ticking stops at the seat count rather than letting somebody
 * build an eleventh choice and discover it on submit. Casting twice is not an
 * error — the service reports `accepted: false` with the ballot already held —
 * and the unique index on (election, voter) is what actually prevents it.
 *
 * **Nobody backs themselves.** The service refuses it; the screen does not
 * offer the tick box against your own name.
 *
 * **A null vote count is not zero.** `tallyVisible` says whether this caller is
 * shown counts at all; an election set to hide the running count shows names and
 * no numbers until voting ends.
 *
 * **Nominating finds the nominee by name.** The same directory search the
 * Members screen uses; `memberId` is set only by picking a result, never typed.
 * A nomination goes to an administrator before it reaches the ballot.
 */
@Component({
  selector: 'app-election',
  imports: [FormsModule, RouterLink],
  styleUrl: './panch.css',
  template: `
    <div class="panch-page">
      <a class="back" routerLink="/panch">‹ Back to the Samaaj Panch</a>

      @if (loading()) {
        <p role="status">Loading the election…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (detail(); as found) {
        <h1 class="page-title">{{ found.election.title }}</h1>
        <p class="subtitle">
          <span class="pill" [class]="pillClass(found.election)">{{ stage(found.election) }}</span>
          {{ found.election.seats }} seats
        </p>

        @if (found.election.description; as description) {
          <p>{{ description }}</p>
        }

        <p class="notice info" role="status">{{ window(found.election) }}</p>

        <!-- The published result ---------------------------------------- -->
        @if (result(); as published) {
          <h2 class="section-heading">Result</h2>
          <p class="small">Published {{ date(published.publishedAt) }} • locked after publication.</p>

          <div class="table-scroll">
            <table>
              <caption class="sr-only">Final ranking</caption>
              <tr>
                <th scope="col">Rank</th>
                <th scope="col">Candidate</th>
                <th scope="col">Votes</th>
                <th scope="col">Seat</th>
              </tr>
              @for (entry of published.ranking; track entry.candidateId) {
                <tr>
                  <td>{{ entry.rank }}</td>
                  <td><a [routerLink]="['/members', entry.memberId]">{{ nameFor(entry.memberId) }}</a></td>
                  <td>{{ entry.votes }}</td>
                  <td>{{ published.seatedMemberIds.includes(entry.memberId) ? 'Seated' : '—' }}</td>
                </tr>
              }
            </table>
          </div>
        }

        <!-- Nominating -------------------------------------------------- -->
        @if (found.election.acceptsNominations) {
          <h2 class="section-heading">Put somebody forward</h2>

          <form class="card" (ngSubmit)="nominate(found.election)">
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
              An administrator decides whether a nomination reaches the ballot. You can put
              yourself forward.
            </p>

            @if (nominateError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            @if (nominateMessage(); as message) {
              <p class="notice info" role="status">{{ message }}</p>
            }

            <div class="actions">
              <button class="btn" type="submit" [disabled]="busy() || memberId.length === 0">
                {{ busy() ? 'Sending…' : 'Nominate' }}
              </button>
            </div>
          </form>
        }

        <!-- The ballot -------------------------------------------------- -->
        <h2 class="section-heading">{{ result() ? 'The ballot' : 'Candidates' }}</h2>

        @if (ballot(found).length === 0) {
          <p class="small">
            @if (found.election.acceptsNominations) {
              Nobody is on the ballot yet. Nominations are still open.
            } @else {
              Nobody is on the ballot.
            }
          </p>
        } @else {
          @if (!found.tallyVisible) {
            <p class="small">
              Vote counts are hidden until voting closes, so that early voters and late voters
              see the same thing.
            </p>
          }

          @if (canVote(found.election)) {
            <p class="notice info" role="status">
              Choose up to {{ found.election.seats }} candidates. Your ballot is final once cast.
              <strong>{{ picked().length }} of {{ found.election.seats }} chosen.</strong>
            </p>
          }

          <div class="grid">
            @for (candidate of ballot(found); track candidate.id) {
              <div class="card" [class.mine]="isBacked(candidate, found.election)">
                <h2><a [routerLink]="['/members', candidate.memberId]">{{ nameFor(candidate.memberId) }}</a></h2>

                @if (found.tallyVisible && candidate.votes !== null) {
                  <p class="stat">{{ candidate.votes }}</p>
                  <p class="small">{{ candidate.votes === 1 ? 'vote' : 'votes' }}</p>
                }

                <div class="actions">
                  @if (isBacked(candidate, found.election)) {
                    <span class="pill ok">Your ballot</span>
                  } @else if (canVote(found.election)) {
                    @if (isMe(candidate)) {
                      <!-- The service refuses it; saying so beats a 409. -->
                      <span class="small">You cannot back yourself.</span>
                    } @else {
                      <label class="choice">
                        <input
                          type="checkbox"
                          [checked]="isPicked(candidate)"
                          [disabled]="!isPicked(candidate) && picked().length >= found.election.seats"
                          (change)="toggle(candidate)"
                        />
                        Choose<span class="sr-only"> {{ nameFor(candidate.memberId) }}</span>
                      </label>
                    }
                  }
                </div>
              </div>
            }
          </div>

          @if (canVote(found.election)) {
            @if (ballotError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <div class="actions">
              <button
                class="btn"
                type="button"
                [disabled]="busy() || picked().length === 0"
                (click)="cast(found.election)"
              >
                {{ busy() ? 'Casting…' : 'Cast my ballot' }}
              </button>
            </div>
          } @else if (found.election.myChoices !== null) {
            <p class="small">You have cast your ballot. It cannot be changed.</p>
          }
        }
      }
    </div>
  `,
})
export class ElectionComponent implements OnInit {
  private readonly api = inject(PanchApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly members = inject(MembersApi);

  readonly detail = signal<ElectionDetail | null>(null);
  readonly result = signal<ElectionResult | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);
  readonly names = signal<Readonly<Record<string, string>>>({});

  /** The candidate ids ticked so far, before the ballot is cast. */
  readonly picked = signal<readonly string[]>([]);
  readonly ballotError = signal<string | null>(null);

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

  private readonly me = computed(() => this.auth.user()?.userId ?? null);

  ngOnInit(): void {
    // The ballot marks the reader's own candidacy, and that needs /me.
    this.auth.ensureCurrentUser().subscribe({
      next: () => this.load(),
      error: () => this.load(),
    });

    // switchMap cancels the previous request on every keystroke, which stops a
    // slow reply for "Rav" from clobbering a faster one for "Ravi".
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
      this.error.set('That link does not name an election.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.get(id).subscribe({
      next: (found) => {
        this.detail.set(found);
        this.loading.set(false);
        this.picked.set([]);
        this.resolveNames(found.candidates.map((candidate) => candidate.memberId));

        if (found.election.status === 'Published') {
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
   * Only asked for once the election says it is published. Before that the
   * endpoint answers 404 by design.
   */
  private loadResult(id: string): void {
    this.api.result(id).subscribe({
      next: (found) => this.result.set(found),
      error: () => this.result.set(null),
    });
  }

  /** One directory call per not-yet-resolved candidate; a failure leaves a generic label. */
  private resolveNames(memberIds: readonly string[]): void {
    const known = this.names();

    for (const id of new Set(memberIds)) {
      if (id === this.me() || id in known) {
        continue;
      }

      this.members.get(id).subscribe({
        next: (member) => this.names.update((current) => ({ ...current, [id]: member.fullName })),
        error: () => {
          // Left unresolved; nameFor() falls back to a generic label.
        },
      });
    }
  }

  // ---- Nominating ---------------------------------------------------------

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

  clearNominee(): void {
    this.memberId = '';
    this.selectedNomineeName.set(null);
    this.nomineeQuery.set('');
    this.nomineeResults.set([]);
  }

  nominate(election: Election): void {
    if (this.memberId.length === 0) {
      return;
    }

    this.busy.set(true);
    this.nominateError.set(null);
    this.nominateMessage.set(null);

    this.api.nominate(election.id, this.memberId).subscribe({
      next: (outcome) => {
        this.clearNominee();
        this.busy.set(false);

        // A repeat nomination is success, not an error - say which happened.
        this.nominateMessage.set(
          outcome.nominated
            ? 'Put forward. An administrator decides whether they reach the ballot.'
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

  // ---- The ballot ---------------------------------------------------------

  /** Whether this member can still cast a ballot: open now, and none cast yet. */
  canVote(election: Election): boolean {
    return election.acceptsVotes && election.myChoices === null;
  }

  isPicked(candidate: Candidate): boolean {
    return this.picked().includes(candidate.id);
  }

  /** Ticking stops at the seat count; unticking always works. */
  toggle(candidate: Candidate): void {
    const current = this.picked();
    const seats = this.detail()?.election.seats ?? 0;

    if (current.includes(candidate.id)) {
      this.picked.set(current.filter((id) => id !== candidate.id));
    } else if (current.length < seats) {
      this.picked.set([...current, candidate.id]);
    }
  }

  cast(election: Election): void {
    if (this.picked().length === 0) {
      return;
    }

    this.busy.set(true);
    this.ballotError.set(null);

    this.api.castBallot(election.id, this.picked()).subscribe({
      next: () => {
        this.busy.set(false);

        // Re-read rather than patch: the counts have moved, and if the ballot
        // was refused as a duplicate the election carries the one actually held.
        this.load();
      },
      error: (failure: unknown) => {
        this.ballotError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  // ---- Rendering ----------------------------------------------------------

  /** Only what is actually on the ballot. A nomination is not a candidate. */
  ballot(detail: ElectionDetail): readonly Candidate[] {
    return detail.candidates.filter((candidate) => candidate.status === 'Approved');
  }

  isBacked(candidate: Candidate, election: Election): boolean {
    return election.myChoices?.includes(candidate.id) ?? false;
  }

  isMe(candidate: Candidate): boolean {
    return this.me() !== null && candidate.memberId === this.me();
  }

  nameFor(memberId: string): string {
    return memberId === this.me() ? 'You' : (this.names()[memberId] ?? 'A member');
  }

  stage(election: Election): string {
    return stageLabel(election);
  }

  pillClass(election: Election): string {
    if (election.status === 'Published') {
      return 'ok';
    }

    return election.acceptsVotes || election.acceptsNominations ? 'warn' : '';
  }

  /** What is open now, and until when - from the two flags, not the status. */
  window(election: Election): string {
    if (election.acceptsNominations) {
      return `Nominations are open until ${this.date(election.nominationEndAt)}.`;
    }

    if (election.acceptsVotes) {
      return `Voting is open until ${this.date(election.votingEndAt)}. One ballot each, up to ${election.seats} names.`;
    }

    return election.status === 'Published'
      ? 'This election is finished and its result is fixed.'
      : 'Nothing is open on this election right now.';
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
