import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import {
  AdminMember,
  Trust,
  TrustElection,
  TrustElectionCandidate,
  TrustElectionDetail,
  TrustElectionStatus,
  TrustMember,
} from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';
import { TrustCollectionsComponent } from './trust-collections.component';
import { TrustDonationsComponent } from './trust-donations.component';
import { TrustExpensesComponent } from './trust-expenses.component';

export type TrustTab = 'people' | 'donations' | 'expenses' | 'collections';

/**
 * One Trust: its roster, and the elections that fill its named positions.
 *
 * **Membership is admin-curated, not derived from where a member lives.**
 * `MemberProfile.Locality` says where somebody lives; it says nothing about
 * who administers the Trust there — confirmed with the user rather than
 * assumed (`trust-service`'s own CLAUDE.md). Adding somebody searches the
 * Samaaj directory, the same `searchMembers` call the Invite Admin screen's
 * "existing member" mode uses, because unlike a small volunteer group's
 * roster the whole Samaaj is not already on screen to pick from.
 *
 * **There is no president-equivalent split here, unlike Volunteer Groups.**
 * A Trust's positions are plural — President, Secretary, Treasurer — so
 * every admin-side command here stays behind the same `Trust.Manage`
 * permission that governs the roster; there is nothing here mirroring
 * `iCanLead`/`iAmThePresident`. The service enforces it; this screen just
 * shows the controls.
 *
 * **Removing a member has no confirmation**, the same weight the Volunteer
 * Groups screen gives assigning a position - both are an administrator
 * acting on somebody's standing in a group they run, and it is reversible:
 * re-adding a removed member reactivates their old row rather than
 * refusing them a second time.
 *
 * **The election flow mirrors Volunteer Groups' own `group-elections`
 * screen almost exactly** - nominate/approve/vote/publish, one button per
 * stage, a confirmed publish - copied deliberately rather than shared, per
 * this repo's own "copy the template, don't couple services" rule.
 *
 * **The money is three more tabs**, each its own component loading when
 * opened: donations (pledged, then collected against a receipt number),
 * expenses (recorded, then approved) and collections (what a Boli winner owes
 * this Trust). All three are `Trust.Manage`, like everything else here.
 */
@Component({
  selector: 'app-trust-detail',
  imports: [
    FormsModule,
    DatePipe,
    RouterLink,
    TrustDonationsComponent,
    TrustExpensesComponent,
    TrustCollectionsComponent,
  ],
  template: `
    <p><a class="btn link" routerLink="/trust">← Trusts</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such Trust in {{ scope.label() }}, or the module is switched off.</p>
    } @else if (trust(); as found) {
      <h1 class="title">{{ found.name }}</h1>
      <p class="sub">
        <span class="pill" [class.warn]="found.status === 'Inactive'">{{ found.status }}</span>
        · {{ activeMembers().length }} trustee{{ activeMembers().length === 1 ? '' : 's' }}
      </p>

      <div class="tabs" role="tablist" aria-label="This Trust">
        @for (item of tabs; track item.value) {
          <button
            type="button"
            role="tab"
            [id]="'trust-tab-' + item.value"
            [class.on]="tab() === item.value"
            [attr.aria-selected]="tab() === item.value"
            aria-controls="trust-tab-panel"
            (click)="selectTab(item.value)"
          >
            {{ item.label }}
          </button>
        }
      </div>

      <div id="trust-tab-panel" role="tabpanel" [attr.aria-labelledby]="'trust-tab-' + tab()">
      @switch (tab()) {
      @case ('donations') {
        <app-trust-donations [trustId]="found.id" [names]="memberNames()" />
      }
      @case ('expenses') {
        <app-trust-expenses [trustId]="found.id" [names]="memberNames()" />
      }
      @case ('collections') {
        <app-trust-collections [trustId]="found.id" [names]="memberNames()" />
      }
      @default {
      <!-- Roster ------------------------------------------------------- -->
      <div class="card">
        <h2>Roster</h2>

        @if (members().length === 0) {
          <p class="empty">Nobody added yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">This Trust's roster</caption>
              <thead>
                <tr><th>Member</th><th>Position</th><th>Status</th><th></th></tr>
              </thead>
              <tbody>
                @for (member of members(); track member.id) {
                  <tr>
                    <td><b>{{ memberName(member.memberId) }}</b></td>
                    <td>
                      @if (editingPosition() === member.id) {
                        <form class="inline-form" (ngSubmit)="savePosition(member)">
                          <label class="sr-only" [for]="'position-' + member.id">Position</label>
                          <input class="input" [id]="'position-' + member.id" name="position"
                            [(ngModel)]="positionDraft" maxlength="100" placeholder="Secretary" />
                          <button class="btn small" type="submit" [disabled]="busy()">Save</button>
                          <button class="btn small alt" type="button"
                            (click)="editingPosition.set(null)">Cancel</button>
                        </form>
                      } @else {
                        {{ member.positionTitle ?? '—' }}
                      }
                    </td>
                    <td>
                      <span class="pill" [class.warn]="member.status === 'Removed'">
                        {{ member.status }}
                      </span>
                    </td>
                    <td>
                      @if (member.status === 'Active' && editingPosition() !== member.id) {
                        <div class="row-actions">
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="editPosition(member)">
                            {{ member.positionTitle ? 'Change position' : 'Give a position' }}
                          </button>
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="removeMember(member)">
                            Remove
                          </button>
                        </div>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        <p class="small">
          Removing keeps the row: adding the same member back later reactivates it rather
          than starting a new one.
        </p>
      </div>

      <div class="card spaced">
        <h2>Add a trustee</h2>

        <form (ngSubmit)="search()">
          <label for="member-search-term">Name</label>
          <div class="search-row">
            <input class="input" id="member-search-term" name="searchTerm"
              [(ngModel)]="searchTerm" placeholder="Part of a name" />
            <button class="btn" type="submit" [disabled]="searching()">Search</button>
          </div>
        </form>

        @if (selectedMember(); as picked) {
          <p class="small">
            Adding <b>{{ picked.fullName }}</b>.
            <button type="button" class="btn link" (click)="clearSelection()">Change</button>
          </p>

          <label for="new-member-position">Position (optional)</label>
          <input class="input" id="new-member-position" name="newPosition"
            [(ngModel)]="newPositionTitle" maxlength="100" placeholder="Treasurer" />

          <button class="btn" type="button" [disabled]="busy()" (click)="addMember(picked)">
            Add to roster
          </button>
        } @else {
          @if (searching()) {
            <p class="small" role="status">Searching…</p>
          } @else if (searched() && searchResults().length === 0) {
            <p class="small">No member found by that name.</p>
          } @else if (searchResults().length > 0) {
            <ul class="suggestions" role="listbox" aria-label="Matching members">
              @for (candidate of searchResults(); track candidate.id) {
                <li role="option">
                  <button type="button" class="btn link" (click)="selectMember(candidate)">
                    {{ candidate.fullName }}
                  </button>
                </li>
              }
            </ul>
          }
        }
      </div>

      <!-- Elections ------------------------------------------------------ -->
      <div class="card spaced">
        <div class="section-head">
          <h2>Elections</h2>
          <button class="btn" type="button" [attr.aria-expanded]="showOpen()"
            (click)="toggleOpenForm()">
            {{ showOpen() ? 'Cancel' : 'Open an election' }}
          </button>
        </div>

        @if (showOpen()) {
          <form class="spaced" (ngSubmit)="open(found)">
            <div class="form-grid">
              <div class="field span-all">
                <label for="election-position">Position</label>
                <input id="election-position" class="input" name="positionTitle"
                  [(ngModel)]="positionTitle" maxlength="100" placeholder="Treasurer" />
              </div>

              <div class="field">
                <label for="nom-start">Nominations open</label>
                <input id="nom-start" class="input" type="datetime-local" name="nominationStartAt"
                  [(ngModel)]="nominationStartAt" />
              </div>
              <div class="field">
                <label for="nom-end">Nominations close</label>
                <input id="nom-end" class="input" type="datetime-local" name="nominationEndAt"
                  [(ngModel)]="nominationEndAt"
                  [attr.aria-invalid]="closesBeforeOpening(nominationStartAt, nominationEndAt) ? 'true' : null" />
              </div>
              <div class="field">
                <label for="vote-start">Voting opens</label>
                <input id="vote-start" class="input" type="datetime-local" name="votingStartAt"
                  [(ngModel)]="votingStartAt"
                  [attr.aria-invalid]="windowsOverlap() ? 'true' : null" />
              </div>
              <div class="field">
                <label for="vote-end">Voting closes</label>
                <input id="vote-end" class="input" type="datetime-local" name="votingEndAt"
                  [(ngModel)]="votingEndAt"
                  [attr.aria-invalid]="closesBeforeOpening(votingStartAt, votingEndAt) ? 'true' : null" />
              </div>

              @if (closesBeforeOpening(nominationStartAt, nominationEndAt)) {
                <p class="notice span-all" role="alert">Nominations cannot close before they open.</p>
              }

              @if (closesBeforeOpening(votingStartAt, votingEndAt)) {
                <p class="notice span-all" role="alert">Voting cannot close before it opens.</p>
              }

              @if (windowsOverlap()) {
                <p class="notice span-all" role="alert">
                  Voting must start after nominations close, so early and late voters see the
                  same ballot.
                </p>
              }
            </div>

            <div class="actions">
              <button class="btn" type="submit" [disabled]="busy() || !canOpen()">
                Open as a draft
              </button>
            </div>
          </form>
        }

        @if (elections().length === 0) {
          <p class="empty">No elections have been run for this Trust yet.</p>
        } @else {
          @for (election of elections(); track election.id) {
            <div class="election">
              <div class="section-head">
                <div>
                  <h3>{{ election.positionTitle }}</h3>
                  <p class="small">
                    Nominations {{ election.nominationStartAt | date: 'd MMM' }} —
                    {{ election.nominationEndAt | date: 'd MMM y' }} · Voting
                    {{ election.votingStartAt | date: 'd MMM' }} —
                    {{ election.votingEndAt | date: 'd MMM y' }}
                  </p>
                </div>
                <span class="pill" [class.warn]="election.status === 'Draft'">
                  {{ stage(election) }}
                </span>
              </div>

              @if (election.winnerMemberId; as winner) {
                <p class="notice ok">Elected: <b>{{ memberName(winner) }}</b></p>
              }

              <div class="row-actions">
                <button class="btn small alt" type="button"
                  [attr.aria-expanded]="expandedId() === election.id"
                  (click)="toggle(election)">
                  {{ expandedId() === election.id ? 'Hide ballot' : 'View ballot' }}
                </button>

                @if (nextStatus(election); as next) {
                  <button class="btn small" type="button" [disabled]="busy()"
                    (click)="move(election, next)">
                    {{ moveLabel(next) }}
                  </button>
                } @else if (election.status === 'Closed') {
                  <button class="btn small" type="button" [disabled]="busy()"
                    [attr.aria-expanded]="confirmingPublish() === election.id"
                    (click)="confirmingPublish.set(election.id)">
                    Publish the result
                  </button>
                }
              </div>

              @if (confirmingPublish() === election.id) {
                <div class="notice" role="status">
                  <p>
                    This picks the winner from the votes cast and installs them as
                    {{ election.positionTitle }}, clearing anyone else who held that title. It
                    cannot be undone or recomputed.
                  </p>
                  <div class="row-actions">
                    <button class="btn" type="button" [disabled]="busy()"
                      (click)="publish(election)">
                      Confirm and publish
                    </button>
                    <button class="btn alt" type="button" (click)="confirmingPublish.set(null)">
                      Not yet
                    </button>
                  </div>
                </div>
              }

              @if (expandedId() === election.id) {
                @if (ballot(); as shown) {
                  @if (shown.candidates.length === 0) {
                    <p class="empty">
                      Nobody has been nominated yet. Active trustees nominate from the member
                      portal while nominations are open.
                    </p>
                  } @else {
                    <div class="table-wrap">
                      <table>
                        <caption class="sr-only">Candidates for {{ election.positionTitle }}</caption>
                        <thead>
                          <tr><th>Member</th><th>Nominated by</th><th>Status</th><th>Votes</th><th></th></tr>
                        </thead>
                        <tbody>
                          @for (candidate of shown.candidates; track candidate.id) {
                            <tr>
                              <td><b>{{ memberName(candidate.memberId) }}</b></td>
                              <td>{{ memberName(candidate.nominatedBy) }}</td>
                              <td>
                                <span class="pill" [class.warn]="candidate.status === 'Nominated'">
                                  {{ candidate.status === 'Approved' ? 'On the ballot' : 'Waiting' }}
                                </span>
                              </td>
                              <td>{{ candidate.votes }}</td>
                              <td>
                                @if (canDecide(election)) {
                                  <div class="row-actions">
                                    @if (candidate.status === 'Nominated') {
                                      <button class="btn small" type="button" [disabled]="busy()"
                                        (click)="decide(election, candidate, true)">
                                        Approve
                                      </button>
                                    }
                                    <button class="btn small alt" type="button" [disabled]="busy()"
                                      (click)="decide(election, candidate, false)">
                                      {{ candidate.status === 'Approved' ? 'Take off the ballot' : 'Turn down' }}
                                    </button>
                                  </div>
                                }
                              </td>
                            </tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  }
                } @else {
                  <p class="empty" role="status">Loading the ballot…</p>
                }
              }
            </div>
          }
        }
      </div>
      }
      }
      </div>
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .election {
      margin-top: var(--space-4);
      padding-top: var(--space-4);
      border-top: 1px solid var(--line);
    }

    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .section-head h2,
    .section-head h3 {
      margin: 0;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
      margin-top: var(--space-2);
    }

    .search-row {
      display: flex;
      gap: var(--space-2);
      align-items: flex-start;
    }

    .search-row .input {
      flex: 1;
    }

    .suggestions {
      list-style: none;
      margin: var(--space-2) 0;
      padding: 0;
      border: 1px solid var(--line);
      border-radius: var(--radius-sm);
      max-height: 220px;
      overflow-y: auto;
    }

    .suggestions li + li {
      border-top: 1px solid var(--line);
    }

    .suggestions button {
      display: block;
      width: 100%;
      text-align: left;
      padding: var(--space-2) var(--space-3);
    }

    .inline-form {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex-wrap: wrap;
    }
  `,
})
export class TrustDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);

  readonly trust = signal<Trust | null>(null);
  readonly members = signal<readonly TrustMember[]>([]);
  readonly elections = signal<readonly TrustElection[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly activeMembers = computed(() => this.members().filter((m) => m.status === 'Active'));

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  /** The same names, for the money tabs' payers, donors and approvers. */
  readonly memberNames = this.names.asReadonly();

  readonly tabs: readonly { value: TrustTab; label: string }[] = [
    { value: 'people', label: 'Roster & elections' },
    { value: 'donations', label: 'Donations' },
    { value: 'expenses', label: 'Expenses' },
    { value: 'collections', label: 'Collections' },
  ];

  readonly tab = signal<TrustTab>('people');

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  // ---- Roster ---------------------------------------------------------------

  readonly editingPosition = signal<string | null>(null);
  positionDraft = '';

  searchTerm = '';
  readonly searching = signal(false);
  readonly searched = signal(false);
  readonly searchResults = signal<readonly AdminMember[]>([]);
  readonly selectedMember = signal<AdminMember | null>(null);
  newPositionTitle = '';

  // ---- Elections --------------------------------------------------------

  readonly showOpen = signal(false);
  readonly expandedId = signal<string | null>(null);
  readonly ballot = signal<TrustElectionDetail | null>(null);
  readonly confirmingPublish = signal<string | null>(null);

  positionTitle = '';
  nominationStartAt = '';
  nominationEndAt = '';
  votingStartAt = '';
  votingEndAt = '';

  ngOnInit(): void {
    this.load();
  }

  selectTab(tab: TrustTab): void {
    this.done.set(null);
    this.error.set(null);
    this.tab.set(tab);
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.getTrust(this.id).subscribe({
      next: (found) => {
        this.trust.set(found);
        this.loading.set(false);
        this.loadMembers();
        this.loadElections();
        this.loadNames();
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

  private loadMembers(): void {
    this.api.listTrustMembers(this.id).subscribe({
      next: (found) => this.members.set(found),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  private loadElections(): void {
    this.api.listTrustElections(this.id).subscribe({
      next: (found) => {
        this.elections.set(found);

        const open = this.expandedId();

        if (open !== null) {
          this.loadBallot(open);
        }
      },
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  /** A failure leaves "A member" rather than a GUID, and is not an error. */
  private loadNames(): void {
    this.api.listMembers().subscribe({
      next: (found) => this.names.set(new Map(found.map((m) => [m.id, m.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }

  // ---- Roster actions ---------------------------------------------------

  search(): void {
    const term = this.searchTerm.trim();

    if (term.length === 0) {
      return;
    }

    this.searching.set(true);
    this.searched.set(false);
    this.error.set(null);

    this.api.searchMembers(term, null).subscribe({
      next: (found) => {
        this.searchResults.set(found.items);
        this.searching.set(false);
        this.searched.set(true);
      },
      error: (failure: unknown) => {
        this.searching.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  selectMember(member: AdminMember): void {
    this.selectedMember.set(member);
    this.searchResults.set([]);
  }

  clearSelection(): void {
    this.selectedMember.set(null);
    this.newPositionTitle = '';
    this.searched.set(false);
    this.searchTerm = '';
  }

  addMember(member: AdminMember): void {
    const positionTitle = this.newPositionTitle.trim();

    this.act(
      this.api.addTrustMember(this.id, member.id, positionTitle.length > 0 ? positionTitle : null),
      `${member.fullName} added to the roster.`,
      () => {
        this.clearSelection();
        this.loadMembers();
      },
    );
  }

  editPosition(member: TrustMember): void {
    this.positionDraft = member.positionTitle ?? '';
    this.editingPosition.set(member.id);
  }

  savePosition(member: TrustMember): void {
    const positionTitle = this.positionDraft.trim();

    this.act(
      this.api.updateTrustMemberPosition(
        this.id,
        member.id,
        positionTitle.length > 0 ? positionTitle : null,
      ),
      `${this.memberName(member.memberId)}'s position is updated.`,
      () => {
        this.editingPosition.set(null);
        this.loadMembers();
      },
    );
  }

  /** No confirmation: reversible, and re-adding the same member reactivates the row. */
  removeMember(member: TrustMember): void {
    this.act(
      this.api.removeTrustMember(this.id, member.id),
      `${this.memberName(member.memberId)} is off the roster.`,
      () => this.loadMembers(),
    );
  }

  // ---- Election actions ---------------------------------------------------

  toggleOpenForm(): void {
    this.showOpen.set(!this.showOpen());

    if (this.showOpen()) {
      this.positionTitle = '';
      this.nominationStartAt = '';
      this.nominationEndAt = '';
      this.votingStartAt = '';
      this.votingEndAt = '';
    }
  }

  windowsOverlap(): boolean {
    if (!this.nominationEndAt || !this.votingStartAt) {
      return false;
    }

    return new Date(this.votingStartAt) < new Date(this.nominationEndAt);
  }

  closesBeforeOpening(open: string, close: string): boolean {
    if (!open || !close) {
      return false;
    }

    return new Date(close) <= new Date(open);
  }

  canOpen(): boolean {
    return (
      this.positionTitle.trim().length > 0 &&
      this.nominationStartAt.length > 0 &&
      this.nominationEndAt.length > 0 &&
      this.votingStartAt.length > 0 &&
      this.votingEndAt.length > 0 &&
      !this.closesBeforeOpening(this.nominationStartAt, this.nominationEndAt) &&
      !this.closesBeforeOpening(this.votingStartAt, this.votingEndAt) &&
      !this.windowsOverlap()
    );
  }

  open(found: Trust): void {
    if (!this.canOpen()) {
      return;
    }

    const title = this.positionTitle.trim();

    this.act(
      this.api.openTrustElection(found.id, {
        positionTitle: title,
        nominationStartAt: new Date(this.nominationStartAt).toISOString(),
        nominationEndAt: new Date(this.nominationEndAt).toISOString(),
        votingStartAt: new Date(this.votingStartAt).toISOString(),
        votingEndAt: new Date(this.votingEndAt).toISOString(),
      }),
      `The ${title} election is set up as a draft. Nominations are not open yet.`,
      () => {
        this.showOpen.set(false);
        this.loadElections();
      },
    );
  }

  stage(election: TrustElection): string {
    if (election.acceptsNominations) {
      return 'Nominations open';
    }

    if (election.acceptsVotes) {
      return 'Voting open';
    }

    switch (election.status) {
      case 'Draft':
        return 'Not started';
      case 'NominationsOpen':
        return 'Nominations closed';
      case 'VotingOpen':
      case 'Closed':
        return 'Voting closed';
      case 'Published':
        return 'Result published';
    }
  }

  nextStatus(election: TrustElection): TrustElectionStatus | null {
    switch (election.status) {
      case 'Draft':
        return 'NominationsOpen';
      case 'NominationsOpen':
        return 'VotingOpen';
      case 'VotingOpen':
        return 'Closed';
      default:
        return null;
    }
  }

  moveLabel(next: TrustElectionStatus): string {
    switch (next) {
      case 'NominationsOpen':
        return 'Open nominations';
      case 'VotingOpen':
        return 'Open voting';
      default:
        return 'Close voting';
    }
  }

  /** Nominations can be approved or removed only until voting opens. */
  canDecide(election: TrustElection): boolean {
    return election.status === 'Draft' || election.status === 'NominationsOpen';
  }

  toggle(election: TrustElection): void {
    if (this.expandedId() === election.id) {
      this.expandedId.set(null);
      this.ballot.set(null);
      return;
    }

    this.expandedId.set(election.id);
    this.loadBallot(election.id);
  }

  private loadBallot(electionId: string): void {
    this.ballot.set(null);

    this.api.getTrustElection(electionId).subscribe({
      next: (found) => this.ballot.set(found),
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.expandedId.set(null);
      },
    });
  }

  move(election: TrustElection, status: TrustElectionStatus): void {
    this.act(
      this.api.moveTrustElection(election.id, status),
      `${election.positionTitle} election: ${this.moveLabel(status).toLowerCase()} done.`,
      () => this.loadElections(),
    );
  }

  decide(election: TrustElection, candidate: TrustElectionCandidate, approve: boolean): void {
    this.act(
      this.api.decideTrustCandidate(election.id, candidate.id, approve),
      approve ? 'On the ballot.' : 'Off the ballot.',
      () => this.loadBallot(election.id),
    );
  }

  publish(election: TrustElection): void {
    this.confirmingPublish.set(null);

    this.act(
      this.api.publishTrustElection(election.id),
      `The ${election.positionTitle} result is announced, and the winner now holds the position.`,
      () => {
        this.loadElections();
        this.loadMembers();
      },
    );
  }

  private act(work: { subscribe: (o: object) => void }, message: string, after?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        after?.();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
