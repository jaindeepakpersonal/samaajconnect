import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { MembersApi } from '../members/members.api';
import { TrustApi } from './trust.api';
import {
  Trust,
  TrustElection,
  TrustElectionDetail,
  TrustMember,
  trustElectionStageLabel,
} from './trust.models';

/**
 * One Trust: its roster, and the elections that fill its named positions.
 *
 * No wireframe covers this - a new module. **Membership is admin-curated**,
 * so unlike Volunteer Groups there is no "Apply to join" here: the roster is
 * read-only from this screen, and a Samaaj admin manages it from the admin
 * panel.
 *
 * **The electorate is this Trust's own active roster, not the whole
 * Samaaj**, the identical rule volunteer-groups-service's own elections use.
 * The nominee picker is a `<select>` built from the roster already loaded
 * on this screen, not a name search - the same reasoning the group detail
 * screen gives for its own picker: a Trust's roster is small and already on
 * screen, so there is no case here for Celebrities of Samaaj's
 * Samaaj-wide autosuggest.
 */
@Component({
  selector: 'app-trust-detail',
  imports: [FormsModule, RouterLink],
  styleUrl: './trust.css',
  template: `
    <div class="trust-page">
      <a class="back" routerLink="/trust">‹ Back to Trusts</a>

      @if (loading()) {
        <p role="status">Loading the Trust…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (trust(); as found) {
        <h1 class="page-title">{{ found.name }}</h1>
        <p class="subtitle">
          <span class="pill" [class.warn]="found.status === 'Inactive'">{{ found.status }}</span>
          · {{ activeMembers().length }} trustee{{ activeMembers().length === 1 ? '' : 's' }}
        </p>

        @if (isActiveMember()) {
          <p class="notice info" role="status">
            You are on this Trust's roster. You can nominate a fellow trustee and vote in its
            elections.
          </p>
        }

        <!-- Roster ------------------------------------------------------- -->
        <h2 class="section-heading">Roster</h2>

        @if (activeMembers().length === 0) {
          <p class="small">Nobody has been added yet.</p>
        } @else {
          <div class="table-scroll">
            <table>
              <caption class="sr-only">This Trust's roster</caption>
              <tr>
                <th scope="col">Member</th>
                <th scope="col">Position</th>
              </tr>
              @for (person of activeMembers(); track person.id) {
                <tr>
                  <td>{{ nameFor(person.memberId) }}</td>
                  <td>{{ person.positionTitle ?? '—' }}</td>
                </tr>
              }
            </table>
          </div>
        }

        <!-- Elections ------------------------------------------------------ -->
        <h2 class="section-heading">Elections</h2>

        @if (loadingElections()) {
          <p role="status">Loading elections…</p>
        } @else if (elections().length === 0) {
          <p class="small">No elections have been run for this Trust yet.</p>
        } @else {
          <div class="queue">
            @for (election of elections(); track election.id) {
              <div class="card">
                <h3>{{ election.positionTitle }}</h3>
                <p>
                  <span class="pill" [class]="pillClass(election)">{{ stage(election) }}</span>
                </p>

                @if (election.winnerMemberId; as winnerId) {
                  <p>Elected: <b>{{ nameFor(winnerId) }}</b></p>
                }

                @if (electionActionError()[election.id]; as message) {
                  <p class="notice error" role="alert">{{ message }}</p>
                }

                <div class="actions">
                  <button class="btn link" type="button" (click)="toggleElection(election.id)">
                    {{ expandedElectionId() === election.id ? 'Hide ballot' : 'View ballot' }}
                  </button>
                </div>

                @if (expandedElectionId() === election.id) {
                  @if (expandedDetail(); as detail) {
                    @if (election.acceptsNominations && isActiveMember()) {
                      <h3 class="section-heading">Nominate somebody</h3>

                      <div class="actions">
                        <label class="sr-only" [for]="'nominee-' + election.id">Nominee</label>
                        <select
                          class="input"
                          [id]="'nominee-' + election.id"
                          [name]="'nominee-' + election.id"
                          [ngModel]="nomineeDrafts()[election.id] ?? ''"
                          (ngModelChange)="setNomineeDraft(election.id, $event)"
                        >
                          <option value="">Choose a trustee…</option>
                          @for (person of activeMembers(); track person.memberId) {
                            <option [value]="person.memberId">{{ nameFor(person.memberId) }}</option>
                          }
                        </select>
                        <button
                          class="btn small"
                          type="button"
                          [disabled]="electionBusy() || !(nomineeDrafts()[election.id])"
                          (click)="nominate(election.id)"
                        >
                          Nominate
                        </button>
                      </div>
                    } @else if (election.acceptsNominations && !isActiveMember()) {
                      <p class="notice info" role="status">
                        Only this Trust's own trustees can nominate, be nominated, or vote.
                      </p>
                    }

                    <h3 class="section-heading">
                      {{ election.status === 'Published' ? 'Result' : 'Ballot' }}
                    </h3>

                    @if (approvedCandidates(detail).length === 0) {
                      <p class="small">Nobody is on the ballot yet.</p>
                    } @else {
                      @for (candidate of approvedCandidates(detail); track candidate.id) {
                        <div class="candidate-row">
                          <span>{{ nameFor(candidate.memberId) }}</span>
                          <span class="small">
                            {{ candidate.votes }} {{ candidate.votes === 1 ? 'vote' : 'votes' }}
                          </span>

                          @if (election.myVoteCandidateId === candidate.id) {
                            <span class="pill ok">Your vote</span>
                          } @else if (
                            election.acceptsVotes &&
                            election.myVoteCandidateId === null &&
                            isActiveMember()
                          ) {
                            @if (candidate.memberId === me()) {
                              <span class="small">You cannot vote for yourself.</span>
                            } @else {
                              <button
                                class="btn small"
                                type="button"
                                [disabled]="electionBusy()"
                                (click)="castVote(election.id, candidate.id)"
                              >
                                Vote
                              </button>
                            }
                          }
                        </div>
                      }
                    }
                  } @else {
                    <p class="small" role="status">Loading the ballot…</p>
                  }
                }
              </div>
            }
          </div>
        }
      }
    </div>
  `,
})
export class TrustDetailComponent implements OnInit {
  private readonly api = inject(TrustApi);
  private readonly members = inject(MembersApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly trust = signal<Trust | null>(null);
  readonly rosterMembers = signal<readonly TrustMember[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly activeMembers = computed(() =>
    this.rosterMembers().filter((m) => m.status === 'Active'),
  );

  /** Resolved names, by member id. */
  readonly names = signal<Record<string, string>>({});

  readonly me = computed(() => this.auth.user()?.userId ?? null);

  readonly isActiveMember = computed(() =>
    this.activeMembers().some((m) => m.memberId === this.me()),
  );

  // ---- Elections ----------------------------------------------------------

  readonly elections = signal<readonly TrustElection[]>([]);
  readonly loadingElections = signal(false);

  readonly expandedElectionId = signal<string | null>(null);
  readonly expandedDetail = signal<TrustElectionDetail | null>(null);

  readonly electionBusy = signal(false);
  readonly nomineeDrafts = signal<Record<string, string>>({});
  readonly electionActionError = signal<Record<string, string>>({});

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.auth.ensureCurrentUser().subscribe({
      next: () => this.load(),
      error: () => this.load(),
    });
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.get(this.id).subscribe({
      next: (found) => {
        this.trust.set(found);
        this.loading.set(false);
        this.loadMembers();
        this.loadElections();
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private loadMembers(): void {
    this.api.members(this.id).subscribe({
      next: (found) => {
        this.rosterMembers.set(found);
        this.resolveNames(found.map((m) => m.memberId));
      },
      error: () => this.rosterMembers.set([]),
    });
  }

  private loadElections(): void {
    this.loadingElections.set(true);

    this.api.elections(this.id).subscribe({
      next: (found) => {
        this.elections.set(found);
        this.loadingElections.set(false);
      },
      error: () => {
        this.elections.set([]);
        this.loadingElections.set(false);
      },
    });
  }

  /**
   * Resolves a member's own full name, one call per not-yet-resolved id.
   * Bounded by this one Trust's own roster and nominators, not the whole
   * Samaaj directory. A failed lookup is left unresolved; `nameFor` already
   * has a fallback for that.
   */
  private resolveNames(memberIds: readonly string[]): void {
    const known = this.names();
    const me = this.me();

    const toResolve = [...new Set(memberIds)].filter((id) => id !== me && !(id in known));

    for (const id of toResolve) {
      this.members.get(id).subscribe({
        next: (member) => this.names.update((current) => ({ ...current, [id]: member.fullName })),
        error: () => {
          // Left unresolved; nameFor() falls back to a generic label.
        },
      });
    }
  }

  nameFor(memberId: string): string {
    if (memberId === this.me()) {
      return 'You';
    }

    return this.names()[memberId] ?? 'A member';
  }

  stage(election: TrustElection): string {
    return trustElectionStageLabel(election);
  }

  pillClass(election: TrustElection): string {
    if (election.status === 'Published') {
      return 'ok';
    }

    return election.acceptsVotes || election.acceptsNominations ? 'warn' : '';
  }

  // ---- Elections ------------------------------------------------------------

  toggleElection(electionId: string): void {
    if (this.expandedElectionId() === electionId) {
      this.expandedElectionId.set(null);
      this.expandedDetail.set(null);
      return;
    }

    this.expandedElectionId.set(electionId);
    this.expandedDetail.set(null);

    this.api.election(electionId).subscribe({
      next: (found) => {
        this.expandedDetail.set(found);
        this.resolveNames(found.candidates.map((c) => c.memberId));
        this.resolveNames(found.candidates.map((c) => c.nominatedBy));
      },
      error: (failure: unknown) => {
        this.setElectionActionError(electionId, describeError(failure));
        this.expandedElectionId.set(null);
      },
    });
  }

  private refreshExpanded(electionId: string): void {
    this.api.election(electionId).subscribe({
      next: (found) => this.expandedDetail.set(found),
      error: () => {
        /* The election list itself still reflects the latest status. */
      },
    });
  }

  setNomineeDraft(electionId: string, memberId: string): void {
    this.nomineeDrafts.update((current) => ({ ...current, [electionId]: memberId }));
  }

  nominate(electionId: string): void {
    const memberId = this.nomineeDrafts()[electionId];

    if (!memberId) {
      return;
    }

    this.electionBusy.set(true);
    this.clearElectionActionError(electionId);

    this.api.nominate(electionId, memberId).subscribe({
      next: () => {
        this.electionBusy.set(false);
        this.nomineeDrafts.update((current) => ({ ...current, [electionId]: '' }));
        this.refreshExpanded(electionId);
      },
      error: (failure: unknown) => {
        this.setElectionActionError(electionId, describeError(failure));
        this.electionBusy.set(false);
      },
    });
  }

  castVote(electionId: string, candidateId: string): void {
    this.electionBusy.set(true);
    this.clearElectionActionError(electionId);

    this.api.castVote(electionId, candidateId).subscribe({
      next: () => {
        this.electionBusy.set(false);
        this.loadElections();
        this.refreshExpanded(electionId);
      },
      error: (failure: unknown) => {
        this.setElectionActionError(electionId, describeError(failure));
        this.electionBusy.set(false);
      },
    });
  }

  approvedCandidates(detail: TrustElectionDetail) {
    return detail.candidates.filter((c) => c.status === 'Approved');
  }

  private setElectionActionError(id: string, message: string): void {
    this.electionActionError.set({ ...this.electionActionError(), [id]: message });
  }

  private clearElectionActionError(id: string): void {
    const { [id]: _removed, ...rest } = this.electionActionError();

    this.electionActionError.set(rest);
  }
}
