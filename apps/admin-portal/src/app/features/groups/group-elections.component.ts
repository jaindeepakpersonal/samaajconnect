import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import {
  GroupApplication,
  GroupDetail,
  GroupElection,
  GroupElectionDetail,
  GroupElectionStatus,
} from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * One volunteer group: who leads it, and the elections that fill its positions.
 *
 * **A group's president is chosen here or by an election, and until now the
 * election half had no screen in this panel.** The service lets a Samaaj admin
 * run a group's *President* election while - and only while - the group has no
 * president, because otherwise a group created without one has nobody able to
 * accept a joiner, nominate, or vote. The member portal has the election
 * screens, but a Super Admin is not a member and an administrator should not
 * have to switch apps to start the one election that is theirs to run.
 *
 * **What this screen offers is decided by the service's own answer.**
 * `group.iCanLead` is "may this caller run this group's elections now" and
 * `group.iAmThePresident` is the other half; the screen never derives either
 * from a role and a president id. An administrator's power is narrower than the
 * president's - the President election only - so per election the screen also
 * checks the position, mirroring `GroupAuthority.CanRunElection`. That copy is
 * a UX convenience; the service re-checks and is the boundary.
 *
 * **Nominating and voting are members' acts and are not here.** They need a
 * seat in the group, which an administrator does not have by being one. This
 * screen opens the election, approves who reaches the ballot, moves it through
 * its stages, shows the tally, and publishes - which installs the winner as
 * the position's holder. Names are resolved from the member directory and fall
 * back to "A member" rather than a GUID.
 *
 * **The stage is one button, strictly forward**, as on every other election
 * screen in this panel, and **publishing is confirmed**: it installs the winner
 * and a second ranking would leave "the result" with nothing to point at.
 */
@Component({
  selector: 'app-group-elections',
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    <p><a class="btn link" routerLink="/groups">← Volunteer groups</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such group in {{ scope.label() }}, or the module is switched off.</p>
    } @else if (detail(); as found) {
      <h1 class="title">{{ found.group.name }}</h1>
      <p class="sub">
        @if (found.group.presidentMemberId) {
          President: {{ memberName(found.group.presidentMemberId) }}
        } @else {
          No president yet
        }
        · {{ found.members.length }} member{{ found.members.length === 1 ? '' : 's' }}
      </p>

      @if (found.group.presidentMemberId === null) {
        <p class="notice" role="status">
          This group has no president. Until it has one, a Samaaj admin can run its
          <b>President</b> election: members nominate and vote in the member portal, you approve
          who reaches the ballot and publish the result.
        </p>
      } @else if (!found.group.iCanLead) {
        <p class="notice" role="status">
          {{ memberName(found.group.presidentMemberId) }} is this group's president and runs its
          elections. You can follow them here.
        </p>
      }

      @if (found.group.iCanLead) {
        <div class="card">
          <h2>Join requests</h2>
          <p class="small">
            Only members of a group can nominate, be nominated, or vote in its elections. A group
            created with no president has no members until somebody accepts its first joiners
            here.
          </p>

          @if (pending().length === 0) {
            <p class="empty">
              Nobody is waiting.
              @if (found.members.length === 0) {
                Members ask to join from the member portal.
              }
            </p>
          } @else {
            <div class="table-wrap">
              <table>
                <caption class="sr-only">Members waiting to join {{ found.group.name }}</caption>
                <thead>
                  <tr><th>Member</th><th>Note</th><th>Asked</th><th></th></tr>
                </thead>
                <tbody>
                  @for (request of pending(); track request.id) {
                    <tr>
                      <td><b>{{ memberName(request.memberId) }}</b></td>
                      <td>{{ request.note ?? '—' }}</td>
                      <td>{{ request.createdAt | date: 'd MMM y' }}</td>
                      <td>
                        <div class="row-actions">
                          <button class="btn small" type="button" [disabled]="busy()"
                            (click)="decideApplication(request, true)">
                            Accept<span class="sr-only"> {{ memberName(request.memberId) }}</span>
                          </button>
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="decideApplication(request, false)">
                            Turn down<span class="sr-only"> {{ memberName(request.memberId) }}</span>
                          </button>
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

      <div class="card spaced">
        <div class="section-head">
          <h2>Elections</h2>

          @if (found.group.iCanLead) {
            <button
              class="btn"
              type="button"
              [attr.aria-expanded]="showOpen()"
              (click)="toggleOpenForm(found)"
            >
              {{ showOpen() ? 'Cancel' : openLabel(found) }}
            </button>
          }
        </div>

        @if (showOpen() && found.group.iCanLead) {
          <form class="spaced" (ngSubmit)="open(found)">
            <div class="form-grid">
              <div class="field span-all">
                <label for="election-position">Position</label>
                <input
                  id="election-position"
                  class="input"
                  name="positionTitle"
                  [(ngModel)]="positionTitle"
                  [readonly]="!found.group.iAmThePresident"
                  maxlength="100"
                />
                @if (!found.group.iAmThePresident) {
                  <p class="small">An administrator can run only the President election.</p>
                }
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
                  Voting must start after nominations close, so that early voters and late voters
                  see the same ballot.
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
          <p class="empty">
            No elections have been run for this group yet.
          </p>
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
                <p class="notice ok">
                  Elected: <b>{{ memberName(winner) }}</b>
                </p>
              }

              <div class="row-actions">
                <button class="btn small alt" type="button"
                  [attr.aria-expanded]="expandedId() === election.id"
                  (click)="toggle(election)">
                  {{ expandedId() === election.id ? 'Hide ballot' : 'View ballot' }}
                </button>

                @if (canRun(found, election)) {
                  @if (canEditElection(election)) {
                    <button class="btn small alt" type="button" [disabled]="busy()"
                      [attr.aria-expanded]="editingId() === election.id"
                      (click)="toggleEditForm(election)">
                      {{ editingId() === election.id ? 'Cancel' : 'Edit' }}
                    </button>
                  }

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
                }
              </div>

              @if (editingId() === election.id) {
                <form class="spaced" (ngSubmit)="saveEdit(election)">
                  <p class="small">
                    Reopen nominations that closed too early by pushing their end date out - and
                    the voting window after it, if it needs to move too.
                  </p>

                  <div class="form-grid">
                    <div class="field span-all">
                      <label [for]="'edit-position-' + election.id">Position</label>
                      <input
                        [id]="'edit-position-' + election.id"
                        class="input"
                        name="editPositionTitle"
                        [(ngModel)]="editPositionTitle"
                        maxlength="100"
                      />
                    </div>

                    <div class="field">
                      <label [for]="'edit-nom-start-' + election.id">Nominations open</label>
                      <input [id]="'edit-nom-start-' + election.id" class="input" type="datetime-local"
                        name="editNominationStartAt" [(ngModel)]="editNominationStartAt" />
                    </div>
                    <div class="field">
                      <label [for]="'edit-nom-end-' + election.id">Nominations close</label>
                      <input [id]="'edit-nom-end-' + election.id" class="input" type="datetime-local"
                        name="editNominationEndAt" [(ngModel)]="editNominationEndAt"
                        [attr.aria-invalid]="closesBeforeOpening(editNominationStartAt, editNominationEndAt) ? 'true' : null" />
                    </div>
                    <div class="field">
                      <label [for]="'edit-vote-start-' + election.id">Voting opens</label>
                      <input [id]="'edit-vote-start-' + election.id" class="input" type="datetime-local"
                        name="editVotingStartAt" [(ngModel)]="editVotingStartAt"
                        [attr.aria-invalid]="editWindowsOverlap() ? 'true' : null" />
                    </div>
                    <div class="field">
                      <label [for]="'edit-vote-end-' + election.id">Voting closes</label>
                      <input [id]="'edit-vote-end-' + election.id" class="input" type="datetime-local"
                        name="editVotingEndAt" [(ngModel)]="editVotingEndAt"
                        [attr.aria-invalid]="closesBeforeOpening(editVotingStartAt, editVotingEndAt) ? 'true' : null" />
                    </div>

                    @if (closesBeforeOpening(editNominationStartAt, editNominationEndAt)) {
                      <p class="notice span-all" role="alert">Nominations cannot close before they open.</p>
                    }

                    @if (closesBeforeOpening(editVotingStartAt, editVotingEndAt)) {
                      <p class="notice span-all" role="alert">Voting cannot close before it opens.</p>
                    }

                    @if (editWindowsOverlap()) {
                      <p class="notice span-all" role="alert">
                        Voting must start after nominations close.
                      </p>
                    }
                  </div>

                  <div class="actions">
                    <button class="btn small" type="submit" [disabled]="busy() || !canSaveEdit()">
                      Save
                    </button>
                    <button class="btn small alt" type="button" (click)="editingId.set(null)">
                      Cancel
                    </button>
                  </div>
                </form>
              }

              @if (confirmingPublish() === election.id) {
                <div class="notice" role="status">
                  <p>
                    This picks the winner from the votes cast and installs them as
                    {{ election.positionTitle }}. It cannot be undone or recomputed.
                  </p>
                  <div class="row-actions">
                    <button class="btn" type="button" [disabled]="busy()" (click)="publish(election)">
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
                      Nobody has been nominated yet. Group members nominate from the member portal
                      while nominations are open.
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
                                @if (canRun(found, election) && canDecide(election)) {
                                  <div class="row-actions">
                                    @if (candidate.status === 'Nominated') {
                                      <button class="btn small" type="button" [disabled]="busy()"
                                        (click)="decide(election, candidate.id, true)">
                                        Approve
                                      </button>
                                    }
                                    <button class="btn small alt" type="button" [disabled]="busy()"
                                      (click)="decide(election, candidate.id, false)">
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
  `,
})
export class GroupElectionsComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);

  readonly detail = signal<GroupDetail | null>(null);
  readonly elections = signal<readonly GroupElection[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly applications = signal<readonly GroupApplication[]>([]);
  readonly pending = computed(() => this.applications().filter((a) => a.status === 'Pending'));

  readonly showOpen = signal(false);
  readonly expandedId = signal<string | null>(null);
  readonly ballot = signal<GroupElectionDetail | null>(null);
  readonly confirmingPublish = signal<string | null>(null);

  readonly editingId = signal<string | null>(null);
  editPositionTitle = '';
  editNominationStartAt = '';
  editNominationEndAt = '';
  editVotingStartAt = '';
  editVotingEndAt = '';

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  readonly groupId = computed(() => this.route.snapshot.paramMap.get('id') ?? '');

  positionTitle = '';
  nominationStartAt = '';
  nominationEndAt = '';
  votingStartAt = '';
  votingEndAt = '';

  ngOnInit(): void {
    this.load();
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  stage(election: GroupElection): string {
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

  openLabel(found: GroupDetail): string {
    return found.group.iAmThePresident ? 'Open an election' : 'Open the President election';
  }

  /**
   * Whether this caller may run this election. Mirrors the service's
   * `CanRunElection`: the president any election, an administrator the
   * President one - while the group has none, which `iCanLead` already says.
   */
  canRun(found: GroupDetail, election: GroupElection): boolean {
    return (
      found.group.iAmThePresident ||
      (found.group.iCanLead && election.positionTitle.trim().toLowerCase() === 'president')
    );
  }

  /** Nominations can be approved or removed only until voting opens. */
  canDecide(election: GroupElection): boolean {
    return election.status === 'Draft' || election.status === 'NominationsOpen';
  }

  /**
   * Whether the election's own details can still be corrected - the same rule
   * the service enforces (`CanBeEdited`), copied here so the button is not one
   * that always answers 409.
   */
  canEditElection(election: GroupElection): boolean {
    return election.status === 'Draft' || election.status === 'NominationsOpen';
  }

  nextStatus(election: GroupElection): GroupElectionStatus | null {
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

  moveLabel(next: GroupElectionStatus): string {
    switch (next) {
      case 'NominationsOpen':
        return 'Open nominations';
      case 'VotingOpen':
        return 'Open voting';
      default:
        return 'Close voting';
    }
  }

  // ---- Admitting members ----------------------------------------------------

  decideApplication(request: GroupApplication, accept: boolean): void {
    const found = this.detail();

    if (found === null) {
      return;
    }

    const who = this.memberName(request.memberId);

    this.act(
      this.api.decideGroupApplication(found.group.id, request.id, accept),
      accept ? `${who} is now a member of ${found.group.name}.` : `${who}'s request was turned down.`,
    );
  }

  // ---- Opening one ---------------------------------------------------------

  toggleOpenForm(found: GroupDetail): void {
    if (this.showOpen()) {
      this.showOpen.set(false);
      return;
    }

    // An administrator can run only the President election, so the field is
    // filled in and locked; the president may name any position.
    this.positionTitle = found.group.iAmThePresident ? '' : 'President';
    this.showOpen.set(true);
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

  open(found: GroupDetail): void {
    if (!this.canOpen()) {
      return;
    }

    const title = this.positionTitle.trim();

    this.act(
      this.api.openGroupElection(found.group.id, {
        positionTitle: title,
        nominationStartAt: new Date(this.nominationStartAt).toISOString(),
        nominationEndAt: new Date(this.nominationEndAt).toISOString(),
        votingStartAt: new Date(this.votingStartAt).toISOString(),
        votingEndAt: new Date(this.votingEndAt).toISOString(),
      }),
      `The ${title} election is set up as a draft. Nominations are not open yet.`,
      () => {
        this.showOpen.set(false);
        this.nominationStartAt = '';
        this.nominationEndAt = '';
        this.votingStartAt = '';
        this.votingEndAt = '';
      },
    );
  }

  // ---- Correcting one: reopening nominations that closed too early ---------

  toggleEditForm(election: GroupElection): void {
    if (this.editingId() === election.id) {
      this.editingId.set(null);
      return;
    }

    this.editPositionTitle = election.positionTitle;
    this.editNominationStartAt = toLocalInputValue(election.nominationStartAt);
    this.editNominationEndAt = toLocalInputValue(election.nominationEndAt);
    this.editVotingStartAt = toLocalInputValue(election.votingStartAt);
    this.editVotingEndAt = toLocalInputValue(election.votingEndAt);
    this.editingId.set(election.id);
  }

  editWindowsOverlap(): boolean {
    if (!this.editNominationEndAt || !this.editVotingStartAt) {
      return false;
    }

    return new Date(this.editVotingStartAt) < new Date(this.editNominationEndAt);
  }

  canSaveEdit(): boolean {
    return (
      this.editPositionTitle.trim().length > 0 &&
      this.editNominationStartAt.length > 0 &&
      this.editNominationEndAt.length > 0 &&
      this.editVotingStartAt.length > 0 &&
      this.editVotingEndAt.length > 0 &&
      !this.closesBeforeOpening(this.editNominationStartAt, this.editNominationEndAt) &&
      !this.closesBeforeOpening(this.editVotingStartAt, this.editVotingEndAt) &&
      !this.editWindowsOverlap()
    );
  }

  saveEdit(election: GroupElection): void {
    if (!this.canSaveEdit()) {
      return;
    }

    this.act(
      this.api.updateGroupElection(election.id, {
        positionTitle: this.editPositionTitle.trim(),
        nominationStartAt: new Date(this.editNominationStartAt).toISOString(),
        nominationEndAt: new Date(this.editNominationEndAt).toISOString(),
        votingStartAt: new Date(this.editVotingStartAt).toISOString(),
        votingEndAt: new Date(this.editVotingEndAt).toISOString(),
      }),
      `${this.editPositionTitle.trim()} election updated.`,
      () => this.editingId.set(null),
    );
  }

  // ---- Running one ---------------------------------------------------------

  toggle(election: GroupElection): void {
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

    this.api.groupElection(electionId).subscribe({
      next: (found) => {
        this.ballot.set(found);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.expandedId.set(null);
      },
    });
  }

  move(election: GroupElection, status: GroupElectionStatus): void {
    this.act(
      this.api.moveGroupElection(election.id, status),
      `${election.positionTitle} election: ${this.moveLabel(status).toLowerCase()} done.`,
    );
  }

  decide(election: GroupElection, candidateId: string, approve: boolean): void {
    this.act(
      this.api.decideGroupCandidate(election.id, candidateId, approve),
      approve ? 'On the ballot.' : 'Off the ballot.',
    );
  }

  publish(election: GroupElection): void {
    this.confirmingPublish.set(null);

    this.act(
      this.api.publishGroupElection(election.id),
      `The ${election.positionTitle} result is announced, and the winner now holds the position.`,
    );
  }

  // ---- Loading -------------------------------------------------------------

  /** Every action re-reads: publishing a President election changes the group itself. */
  private act(work: { subscribe: (o: object) => void }, message: string, reset?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        reset?.();
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  private load(): void {
    this.loading.set(this.detail() === null);
    this.error.set(null);

    this.api.groupDetail(this.groupId()).subscribe({
      next: (found) => {
        this.detail.set(found);
        this.loadElections();
        this.loadApplications(found);
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

  private loadElections(): void {
    this.api.listGroupElections(this.groupId()).subscribe({
      next: (found) => {
        this.elections.set(found);
        this.loading.set(false);

        // Keep an open ballot in step with the action that just ran.
        const open = this.expandedId();

        if (open !== null) {
          this.loadBallot(open);
        }
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  /**
   * The queue is asked for only when the service said this caller may decide it:
   * anybody else gets a 404, and a speculative call would put one on every visit.
   */
  private loadApplications(found: GroupDetail): void {
    if (!found.group.iCanLead) {
      this.applications.set([]);
      return;
    }

    this.api.groupApplications(found.group.id).subscribe({
      next: (list) => this.applications.set(list),
      error: () => this.applications.set([]),
    });
  }

  /** A failure leaves "A member" rather than a GUID, and is not an error. */
  private loadNames(): void {
    if (this.names().size > 0) {
      return;
    }

    this.api.listMembers().subscribe({
      next: (found) => this.names.set(new Map(found.map((m) => [m.id, m.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }
}

/** An ISO instant to the local `datetime-local` input value it renders as. */
function toLocalInputValue(iso: string): string {
  const date = new Date(iso);

  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const pad = (n: number) => n.toString().padStart(2, '0');

  return (
    `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` +
    `T${pad(date.getHours())}:${pad(date.getMinutes())}`
  );
}
