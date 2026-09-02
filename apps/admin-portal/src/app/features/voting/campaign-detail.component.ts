import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Campaign, CampaignDetail, CampaignResult, CampaignStatus, Candidate } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * One campaign: its stage, its ballot, its running count, and its result.
 *
 * **Deciding a nomination is the endpoint nothing could reach**, and without it
 * a nomination sat as `Nominated` forever — put forward by a member and never
 * on the ballot, so a campaign could reach its voting window with nobody to
 * vote for. The service refuses `VotingOpen` on an empty ballot, which turned
 * that into a campaign that simply could not be run.
 *
 * **The stage is one button, because the sequence has one next step.** Draft →
 * NominationsOpen → VotingOpen → Closed, then publishing, which is its own call
 * rather than a sixth status. Offering four buttons and letting the server
 * refuse three would be offering choices that were never there — and an
 * election that can go backwards is not an election.
 *
 * **Removing a candidate stops being offered once voting opens**, because
 * removing them would discard the votes already cast for them. The service
 * refuses it; the screen does not offer it.
 *
 * **Publishing is refused the second time, not ignored.** Unlike a Boli result,
 * where a repeat announcement changes nothing, a second publish here would
 * compute a second ranking — and two rankings leave "the result" with no
 * referent. So the confirmation matters and the screen says what it is doing.
 */
@Component({
  selector: 'app-campaign-detail',
  imports: [DatePipe, RouterLink],
  template: `
    <p><a class="btn link" routerLink="/voting">← All campaigns</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such campaign in {{ scope.label() }}.</p>
    } @else if (campaign(); as subject) {
      <h1 class="title">{{ subject.title }}</h1>
      <p class="sub">
        {{ subject.status }} · Top {{ subject.topN }} ·
        {{ approved().length }} on the ballot
      </p>

      @if (subject.description) {
        <p>{{ subject.description }}</p>
      }

      <!-- Stage ---------------------------------------------------------- -->
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
            Voting is closed. Publishing computes the ranking from the votes cast and freezes
            it — it cannot be done twice, because two rankings would leave "the result" with
            nothing to point at.
          </p>

          <!--
            The trigger stays and stays enabled while the confirmation is open:
            replacing or disabling it destroys the focused element and drops a
            keyboard user to the body (WCAG 2.4.3).
          -->
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
                This freezes the Top {{ subject.topN }} as it stands and announces it. It cannot
                be undone or recomputed.
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
          <p class="empty">Published. This campaign is finished.</p>
        }
      </div>

      <!-- Result --------------------------------------------------------- -->
      @if (result(); as ranking) {
        <div class="card spaced">
          <h2>The result</h2>
          <p class="small">
            Announced {{ ranking.publishedAt | date: 'd MMM y, h:mm a' }}. Stored as it was
            computed and never recalculated — a result that moved after it was announced would
            be worse than none.
          </p>

          <div class="table-wrap">
            <table>
              <caption class="sr-only">The published result, in rank order</caption>
              <thead>
                <tr><th>#</th><th>Member</th><th>Votes</th></tr>
              </thead>
              <tbody>
                @for (entry of ranking.ranking; track entry.candidateId) {
                  <tr>
                    <td>{{ entry.rank }}</td>
                    <td><b>{{ memberName(entry.memberId) }}</b></td>
                    <td>{{ entry.votes }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }

      <!-- Ballot --------------------------------------------------------- -->
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
                <tr><th>Member</th><th>Category</th><th>Votes</th><th></th></tr>
              </thead>
              <tbody>
                @for (candidate of approved(); track candidate.id) {
                  <tr>
                    <td><b>{{ memberName(candidate.memberId) }}</b></td>
                    <td>{{ candidate.category ?? '—' }}</td>
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

      <!-- Nominations ---------------------------------------------------- -->
      <div class="card spaced">
        <h2>Waiting for a decision</h2>

        @if (nominated().length === 0) {
          <p class="empty">
            Nothing waiting. Members put names forward while nominations are open.
          </p>
        } @else {
          <p class="small">
            A member put these names forward. Approving puts somebody on the ballot; turning one
            down takes the nomination away. One candidacy per member however many people
            nominate them — two entries for one person would split their vote.
          </p>

          <div class="table-wrap">
            <table>
              <caption class="sr-only">Nominations waiting for a decision</caption>
              <thead>
                <tr><th>Member</th><th>Category</th><th>Nominated by</th><th></th></tr>
              </thead>
              <tbody>
                @for (candidate of nominated(); track candidate.id) {
                  <tr>
                    <td><b>{{ memberName(candidate.memberId) }}</b></td>
                    <td>{{ candidate.category ?? '—' }}</td>
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
export class CampaignDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);

  readonly detail = signal<CampaignDetail | null>(null);
  readonly result = signal<CampaignResult | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly confirmingPublish = signal(false);

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  readonly campaign = computed<Campaign | null>(() => this.detail()?.campaign ?? null);
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

  /**
   * The one stage this campaign can move to, or null when the next step is
   * publishing (or there is none).
   */
  readonly nextStatus = computed<CampaignStatus | null>(() => {
    switch (this.campaign()?.status) {
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

  moveLabel(next: CampaignStatus): string {
    switch (next) {
      case 'NominationsOpen':
        return 'Open nominations';
      case 'VotingOpen':
        return 'Open voting';
      default:
        return 'Close voting';
    }
  }

  /**
   * Whether a candidacy can still be taken away.
   *
   * Only before voting opens. Afterwards removing somebody would discard the
   * votes already cast for them, so the service refuses it and this screen does
   * not offer a button that always answers 409.
   */
  canRemove(): boolean {
    const status = this.campaign()?.status;

    return status === 'Draft' || status === 'NominationsOpen';
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.campaign(this.id).subscribe({
      next: (found) => {
        this.detail.set(found);
        this.loading.set(false);
        this.loadNames();

        if (found.campaign.status === 'Published') {
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

  /**
   * The frozen ranking, read only once a campaign is published.
   *
   * A 404 before then is the normal state rather than an error — there is no
   * result until one has been computed — so this is only called when the status
   * says there should be one.
   */
  private loadResult(): void {
    this.api.campaignResults(this.id).subscribe({
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

  move(status: CampaignStatus): void {
    this.act(this.api.moveCampaign(this.id, status), `Campaign is now ${status}.`);
  }

  decide(candidate: Candidate, approve: boolean): void {
    const who = this.memberName(candidate.memberId);

    this.act(
      this.api.decideCandidate(this.id, candidate.id, approve),
      approve ? `${who} is on the ballot.` : `${who} is off the ballot.`,
    );
  }

  publish(): void {
    this.confirmingPublish.set(false);

    this.act(
      this.api.publishCampaignResults(this.id),
      'The result is announced, and is now fixed.',
    );
  }

  /**
   * Every action re-reads the campaign. Approving somebody changes whether
   * voting can open at all, moving a stage changes whether a candidate can
   * still be removed, and publishing changes everything on the screen — the
   * server is the only thing that knows all of it at once.
   */
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
