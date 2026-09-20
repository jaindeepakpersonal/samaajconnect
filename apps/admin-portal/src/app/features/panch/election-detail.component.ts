import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import {
  Election,
  ElectionCandidate,
  ElectionDetail,
  ElectionResult,
  ElectionStatus,
} from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * One Panch election: its stage, its ballot, its running count, and its result.
 *
 * **The stage is one button, because the sequence has one next step.** Draft →
 * NominationsOpen → VotingOpen → Closed, then publishing, which is its own call
 * rather than a fifth status. An election that can go backwards is not an
 * election.
 *
 * **Deciding a nomination is what puts somebody on the ballot.** Any member can
 * put a name forward; an administrator decides whether it reaches the ballot,
 * and the service refuses `VotingOpen` on an empty one.
 *
 * **Publishing seats the winners and replaces the Panch**, so it asks first and
 * says what it will do. It cannot be repeated: two rankings would leave "the
 * result" with nothing to point at.
 */
@Component({
  selector: 'app-election-detail',
  imports: [DatePipe, RouterLink],
  template: `
    <p><a class="btn link" routerLink="/panch">← Samaaj Panch</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such election in {{ scope.label() }}.</p>
    } @else if (election(); as subject) {
      <h1 class="title">{{ subject.title }}</h1>
      <p class="sub">
        {{ subject.status }} · {{ subject.seats }} seats · {{ approved().length }} on the ballot
      </p>

      @if (subject.description) {
        <p>{{ subject.description }}</p>
      }

      <div class="card">
        <h2>Stage</h2>

        <p class="small">
          Nominations {{ subject.nominationStartAt | date: 'd MMM' }} —
          {{ subject.nominationEndAt | date: 'd MMM y' }} ·
          Voting {{ subject.votingStartAt | date: 'd MMM' }} —
          {{ subject.votingEndAt | date: 'd MMM y' }}
        </p>

        @if (nextStatus(); as next) {
          <p class="small">
            Draft → nominations → voting → closed, and never backwards.
            @if (next === 'VotingOpen' && approved().length === 0) {
              Voting cannot open on an empty ballot, so approve somebody first.
            }
          </p>

          <button
            class="btn"
            type="button"
            [disabled]="busy() || (next === 'VotingOpen' && approved().length === 0)"
            (click)="move(next)"
          >
            {{ moveLabel(next) }}
          </button>
        } @else if (subject.status === 'Closed') {
          <p class="small">
            Voting is closed. Publishing counts the ballots, freezes the ranking, and seats the
            {{ subject.seats }} most-backed candidates — each with at least one vote — as the
            Panch, replacing whoever sits on it now.
          </p>

          <!-- The trigger stays enabled while the confirmation is open: replacing or
               disabling it would drop a keyboard user to the body (WCAG 2.4.3). -->
          <button
            class="btn"
            type="button"
            [disabled]="busy()"
            [attr.aria-expanded]="confirmingPublish()"
            (click)="confirmingPublish.set(true)"
          >
            Publish the result
          </button>

          @if (confirmingPublish()) {
            <div class="notice" role="status">
              <p>
                This seats the winners and unseats the current Panch. It cannot be undone or
                recomputed.
              </p>
              <div class="row-actions">
                <button class="btn" type="button" [disabled]="busy()" (click)="publish()">
                  Confirm and publish
                </button>
                <button class="btn alt" type="button" (click)="confirmingPublish.set(false)">
                  Not yet
                </button>
              </div>
            </div>
          }
        } @else {
          <p class="empty">Published. This election is finished.</p>
        }
      </div>

      @if (result(); as ranking) {
        <div class="card spaced">
          <h2>The result</h2>
          <p class="small">
            Announced {{ ranking.publishedAt | date: 'd MMM y, h:mm a' }}. Stored as it was
            computed and never recalculated.
          </p>

          <div class="table-wrap">
            <table>
              <caption class="sr-only">The published result, in rank order</caption>
              <thead>
                <tr><th>#</th><th>Member</th><th>Votes</th><th>Seat</th></tr>
              </thead>
              <tbody>
                @for (entry of ranking.ranking; track entry.candidateId) {
                  <tr>
                    <td>{{ entry.rank }}</td>
                    <td><b>{{ memberName(entry.memberId) }}</b></td>
                    <td>{{ entry.votes }}</td>
                    <td>{{ seated(ranking, entry.memberId) ? 'Seated' : '—' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }

      <div class="card spaced">
        <h2>On the ballot</h2>

        @if (approved().length === 0) {
          <p class="empty">Nobody yet. Approve a nomination below to put somebody on it.</p>
        } @else {
          @if (tallyVisible()) {
            <p class="small">
              {{ totalVotes() }} vote{{ totalVotes() === 1 ? '' : 's' }} cast.
              @if (subject.resultsVisibility === 'HiddenUntilClose') {
                Members cannot see these counts until voting closes; you can.
              }
            </p>
          }

          <div class="table-wrap">
            <table>
              <caption class="sr-only">Candidates on the ballot</caption>
              <thead>
                <tr><th>Member</th><th>Votes</th><th></th></tr>
              </thead>
              <tbody>
                @for (candidate of approved(); track candidate.id) {
                  <tr>
                    <td><b>{{ memberName(candidate.memberId) }}</b></td>
                    <td>
                      @if (candidate.votes !== null) {
                        {{ candidate.votes }}
                      } @else {
                        <span class="muted">Not visible</span>
                      }
                    </td>
                    <td>
                      @if (canRemove()) {
                        <button class="btn small alt" type="button" [disabled]="busy()"
                          (click)="decide(candidate, false)">
                          Take off the ballot
                        </button>
                      } @else {
                        <span class="muted">Voting has opened</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <div class="card spaced">
        <h2>Waiting for a decision</h2>

        @if (nominated().length === 0) {
          <p class="empty">
            Nothing waiting. Members put names forward while nominations are open.
          </p>
        } @else {
          <p class="small">
            Approving puts somebody on the ballot; turning one down takes the nomination away.
            Each member has one candidacy however many people nominate them.
          </p>

          <div class="table-wrap">
            <table>
              <caption class="sr-only">Nominations waiting for a decision</caption>
              <thead>
                <tr><th>Member</th><th>Nominated by</th><th></th></tr>
              </thead>
              <tbody>
                @for (candidate of nominated(); track candidate.id) {
                  <tr>
                    <td><b>{{ memberName(candidate.memberId) }}</b></td>
                    <td>{{ memberName(candidate.nominatedBy) }}</td>
                    <td>
                      <div class="row-actions">
                        <button class="btn small" type="button" [disabled]="busy()"
                          (click)="decide(candidate, true)">
                          Approve
                        </button>
                        @if (canRemove()) {
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="decide(candidate, false)">
                            Turn down
                          </button>
                        }
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }
  `,
})
export class ElectionDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);

  readonly detail = signal<ElectionDetail | null>(null);
  readonly result = signal<ElectionResult | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly confirmingPublish = signal(false);

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  readonly election = computed<Election | null>(() => this.detail()?.election ?? null);
  readonly tallyVisible = computed(() => this.detail()?.tallyVisible ?? false);

  readonly approved = computed(
    () => this.detail()?.candidates.filter((c) => c.status === 'Approved') ?? [],
  );

  readonly nominated = computed(
    () => this.detail()?.candidates.filter((c) => c.status === 'Nominated') ?? [],
  );

  readonly totalVotes = computed(() =>
    this.approved().reduce((sum, c) => sum + (c.votes ?? 0), 0),
  );

  /** The one stage this election can move to, or null when the next step is publishing. */
  readonly nextStatus = computed<ElectionStatus | null>(() => {
    switch (this.election()?.status) {
      case 'Draft':
        return 'NominationsOpen';
      case 'NominationsOpen':
        return 'VotingOpen';
      case 'VotingOpen':
        return 'Closed';
      default:
        return null;
    }
  });

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.load();
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  seated(result: ElectionResult, memberId: string): boolean {
    return result.seatedMemberIds.includes(memberId);
  }

  moveLabel(next: ElectionStatus): string {
    switch (next) {
      case 'NominationsOpen':
        return 'Open nominations';
      case 'VotingOpen':
        return 'Open voting';
      default:
        return 'Close voting';
    }
  }

  /** Only before voting opens: removing somebody afterwards would discard votes already cast. */
  canRemove(): boolean {
    const status = this.election()?.status;

    return status === 'Draft' || status === 'NominationsOpen';
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.election(this.id).subscribe({
      next: (found) => {
        this.detail.set(found);
        this.loading.set(false);
        this.loadNames();

        if (found.election.status === 'Published') {
          this.loadResult();
        } else {
          this.result.set(null);
        }
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.notFound.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  /** Read only once published: a 404 before then is the normal state, not an error. */
  private loadResult(): void {
    this.api.electionResults(this.id).subscribe({
      next: (found) => this.result.set(found),
      error: () => this.result.set(null),
    });
  }

  /** A failure leaves "A member" rather than a GUID, and is not an error. */
  private loadNames(): void {
    this.api.listMembers().subscribe({
      next: (found) => this.names.set(new Map(found.map((m) => [m.id, m.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }

  move(status: ElectionStatus): void {
    this.act(this.api.moveElection(this.id, status), `Election is now ${status}.`);
  }

  decide(candidate: ElectionCandidate, approve: boolean): void {
    const who = this.memberName(candidate.memberId);

    this.act(
      this.api.decideElectionCandidate(this.id, candidate.id, approve),
      approve ? `${who} is on the ballot.` : `${who} is off the ballot.`,
    );
  }

  publish(): void {
    this.confirmingPublish.set(false);

    this.act(this.api.publishElection(this.id), 'The result is announced and the Panch is seated.');
  }

  /** Every action re-reads the election: the server is the only thing that knows all of it. */
  private act(work: { subscribe: (o: object) => void }, message: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
