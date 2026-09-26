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
  GroupRole,
  GroupRosterMember,
} from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * One volunteer group: its roles, who holds them, and the elections that fill
 * them.
 *
 * **Every member holds one of the group's roles.** Volunteer and President are
 * built in; the group defines the rest - Secretary, three Coordinators - each
 * with the seats an election for it fills. An administrator of the group (or its
 * president) defines them here, moves a member into one directly, and runs an
 * election for any of them, at any time (changed 2026-09-26 - an administrator
 * used to be able to run only the President election, and only while there was
 * no president).
 *
 * **What this screen offers is decided by the service's own answer.**
 * `group.iCanRunElections` is "may this caller manage roles and run elections",
 * and `group.iCanLead` is "may they decide join requests" - which an
 * administrator still may only while the group has no president. The screen
 * never derives either from a role and a president id; the service re-checks
 * and is the boundary.
 *
 * **Nominating and voting are members' acts and are not here.** They need a
 * seat in the group, which an administrator does not have by being one. This
 * screen opens the election, approves who reaches the ballot, moves it through
 * its stages, shows the tally, and publishes - which installs the winners in
 * the role and sends anyone who held it and lost back to Volunteer. Names are
 * resolved from the member directory and fall back to "A member" rather than a
 * GUID.
 *
 * **The stage is one button, strictly forward**, as on every other election
 * screen in this panel, and **publishing is confirmed**: it installs the winners
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
          This group has no president. Until it has one you accept its joiners here. Run its
          <b>President</b> election below: members nominate and vote in the member portal, you
          approve who reaches the ballot and publish the result.
        </p>
      } @else if (!found.group.iCanRunElections) {
        <p class="notice" role="status">
          {{ memberName(found.group.presidentMemberId) }} is this group's president and runs its
          roles and elections. You can follow them here.
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

      <!-- Volunteer roles ---------------------------------------------- -->
      <div class="card spaced">
        <h2>Volunteer roles</h2>
        <p class="small">
          Every member holds one role. Everybody starts as a Volunteer; each other role is filled by
          an election for as many seats as it has.
        </p>

        <div class="table-wrap">
          <table>
            <caption class="sr-only">Volunteer roles in {{ found.group.name }}</caption>
            <thead>
              <tr><th>Role</th><th>Seats</th><th>Held by</th><th></th></tr>
            </thead>
            <tbody>
              @for (role of found.roles; track role.id) {
                <tr>
                  @if (editingRoleId() === role.id) {
                    <td>
                      <label class="sr-only" [for]="'role-title-' + role.id">Title</label>
                      <input [id]="'role-title-' + role.id" class="input" name="editRoleTitle"
                        [(ngModel)]="editRoleTitle" maxlength="100" />
                    </td>
                    <td>
                      <label class="sr-only" [for]="'role-seats-' + role.id">Seats</label>
                      <input [id]="'role-seats-' + role.id" class="input seats" type="number" min="1"
                        max="100" name="editRoleSeats" [(ngModel)]="editRoleSeats" />
                    </td>
                    <td>{{ role.holderCount }}</td>
                    <td>
                      <div class="row-actions">
                        <button class="btn small" type="button" (click)="saveRole(found, role)"
                          [disabled]="busy() || !validRole(editRoleTitle, editRoleSeats)">
                          Save
                        </button>
                        <button class="btn small alt" type="button" (click)="editingRoleId.set(null)">
                          Cancel
                        </button>
                      </div>
                    </td>
                  } @else {
                    <td>
                      <b>{{ role.title }}</b>
                      @if (!role.isElected) {
                        <span class="small"> · everybody's until elected to another</span>
                      }
                    </td>
                    <td>{{ role.seats ?? 'No limit' }}</td>
                    <td>{{ role.holderCount }}</td>
                    <td>
                      @if (found.group.iCanRunElections && role.isEditable) {
                        <div class="row-actions">
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="startEditRole(role)">
                            Edit<span class="sr-only"> {{ role.title }}</span>
                          </button>
                          <button class="btn small alt" type="button" [disabled]="busy() || role.holderCount > 0"
                            [title]="role.holderCount > 0 ? 'Move its holders to another role first' : ''"
                            (click)="removeRole(found, role)">
                            Remove<span class="sr-only"> {{ role.title }}</span>
                          </button>
                        </div>
                      }
                    </td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>

        @if (found.group.iCanRunElections) {
          <form class="add-role" (ngSubmit)="defineRole(found)">
            <div>
              <label for="new-role-title">New role</label>
              <input id="new-role-title" class="input" name="newRoleTitle" [(ngModel)]="newRoleTitle"
                maxlength="100" placeholder="Secretary" />
            </div>
            <div>
              <label for="new-role-seats">Seats</label>
              <input id="new-role-seats" class="input seats" type="number" min="1" max="100"
                name="newRoleSeats" [(ngModel)]="newRoleSeats" />
            </div>
            <button class="btn" type="submit" [disabled]="busy() || !validRole(newRoleTitle, newRoleSeats)">
              Add role
            </button>
          </form>
        }
      </div>

      <!-- Members and their roles ---------------------------------------- -->
      <div class="card spaced">
        <h2>Members</h2>

        @if (found.members.length === 0) {
          <p class="empty">Nobody is in this group yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Members of {{ found.group.name }} and their roles</caption>
              <thead>
                <tr><th>Member</th><th>Role</th><th>Joined</th></tr>
              </thead>
              <tbody>
                @for (member of found.members; track member.memberId) {
                  <tr>
                    <td><b>{{ memberName(member.memberId) }}</b></td>
                    <td>
                      @if (found.group.iCanRunElections && member.memberId !== found.group.presidentMemberId) {
                        <label class="sr-only" [for]="'member-role-' + member.memberId">
                          Role for {{ memberName(member.memberId) }}
                        </label>
                        <select [id]="'member-role-' + member.memberId" class="input inline"
                          [ngModel]="member.roleId" (ngModelChange)="assignRole(found, member, $event)"
                          [disabled]="busy()">
                          @for (role of assignableRoles(found); track role.id) {
                            <option [value]="role.id">{{ role.title }}</option>
                          }
                        </select>
                      } @else {
                        {{ member.rolePosition }}
                      }
                    </td>
                    <td>{{ member.joinedAt | date: 'd MMM y' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (found.group.iCanRunElections) {
            <p class="small">
              President moves only with the presidency - change the president, or run a President
              election.
            </p>
          }
        }
      </div>

      <div class="card spaced">
        <div class="section-head">
          <h2>Elections</h2>

          @if (found.group.iCanRunElections) {
            <button
              class="btn"
              type="button"
              [attr.aria-expanded]="showOpen()"
              (click)="toggleOpenForm(found)"
            >
              {{ showOpen() ? 'Cancel' : 'Open an election' }}
            </button>
          }
        </div>

        @if (showOpen() && found.group.iCanRunElections) {
          <form class="spaced" (ngSubmit)="open(found)">
            <div class="form-grid">
              <div class="field span-all">
                <label for="election-role">Role</label>
                <select id="election-role" class="input" name="roleId" [(ngModel)]="roleId">
                  <option value="">Choose a role</option>
                  @for (role of electedRoles(found); track role.id) {
                    <option [value]="role.id">{{ role.title }} ({{ seatsLabel(role.seats ?? 1) }})</option>
                  }
                </select>
                <p class="small">
                  A role runs one election at a time. Add a role above to elect something other than
                  a president.
                </p>
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
                    {{ seatsLabel(election.seats) }} · Nominations
                    {{ election.nominationStartAt | date: 'd MMM' }} —
                    {{ election.nominationEndAt | date: 'd MMM y' }} · Voting
                    {{ election.votingStartAt | date: 'd MMM' }} —
                    {{ election.votingEndAt | date: 'd MMM y' }}
                  </p>
                </div>
                <span class="pill" [class.warn]="election.status === 'Draft'">
                  {{ stage(election) }}
                </span>
              </div>

              @if (election.winnerMemberIds.length > 0) {
                <p class="notice ok">
                  Elected: <b>{{ winnerNames(election) }}</b>
                </p>
              }

              <div class="row-actions">
                <button class="btn small alt" type="button"
                  [attr.aria-expanded]="expandedId() === election.id"
                  (click)="toggle(election)">
                  {{ expandedId() === election.id ? 'Hide ballot' : 'View ballot' }}
                </button>

                @if (canRun(found)) {
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
                      <label [for]="'edit-role-' + election.id">Role</label>
                      <select [id]="'edit-role-' + election.id" class="input" name="editRoleId"
                        [(ngModel)]="editRoleId">
                        @for (role of electedRoles(found); track role.id) {
                          <option [value]="role.id">{{ role.title }} ({{ seatsLabel(role.seats ?? 1) }})</option>
                        }
                      </select>
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
                    This picks the top {{ seatsLabel(election.seats) }} from the votes cast and
                    installs them as {{ election.positionTitle }}. Anyone who holds it now and did
                    not win goes back to Volunteer. It cannot be undone or recomputed.
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
                                @if (canRun(found) && canDecide(election)) {
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

    .add-role {
      display: flex;
      gap: var(--space-3);
      align-items: flex-end;
      flex-wrap: wrap;
      margin-top: var(--space-4);
    }

    .seats {
      max-width: 6rem;
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
  editRoleId = '';
  editNominationStartAt = '';
  editNominationEndAt = '';
  editVotingStartAt = '';
  editVotingEndAt = '';

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  readonly groupId = computed(() => this.route.snapshot.paramMap.get('id') ?? '');

  roleId = '';
  nominationStartAt = '';
  nominationEndAt = '';
  votingStartAt = '';
  votingEndAt = '';

  newRoleTitle = '';
  newRoleSeats = 1;
  readonly editingRoleId = signal<string | null>(null);
  editRoleTitle = '';
  editRoleSeats = 1;

  ngOnInit(): void {
    this.load();
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  winnerNames(election: GroupElection): string {
    return election.winnerMemberIds.map((id) => this.memberName(id)).join(', ');
  }

  seatsLabel(seats: number): string {
    return seats === 1 ? '1 seat' : `${seats} seats`;
  }

  /** The roles an election can be run for: every one but Volunteer. */
  electedRoles(found: GroupDetail): readonly GroupRole[] {
    return found.roles.filter((r) => r.isElected);
  }

  /** The roles a member can be moved into by hand: every one but President. */
  assignableRoles(found: GroupDetail): readonly GroupRole[] {
    return found.roles.filter((r) => r.kind !== 'President');
  }

  // ---- Roles ---------------------------------------------------------------

  validRole(title: string, seats: number | null): boolean {
    return title.trim().length > 0 && Number.isInteger(seats) && seats! >= 1 && seats! <= 100;
  }

  defineRole(found: GroupDetail): void {
    const title = this.newRoleTitle.trim();

    if (!this.validRole(title, this.newRoleSeats)) {
      return;
    }

    this.act(
      this.api.defineGroupRole(found.group.id, title, this.newRoleSeats),
      `${title} is a role in ${found.group.name} now, with ${this.seatsLabel(this.newRoleSeats)}.`,
      () => {
        this.newRoleTitle = '';
        this.newRoleSeats = 1;
      },
    );
  }

  startEditRole(role: GroupRole): void {
    this.editRoleTitle = role.title;
    this.editRoleSeats = role.seats ?? 1;
    this.editingRoleId.set(role.id);
  }

  saveRole(found: GroupDetail, role: GroupRole): void {
    const title = this.editRoleTitle.trim();

    if (!this.validRole(title, this.editRoleSeats)) {
      return;
    }

    this.act(
      this.api.updateGroupRole(found.group.id, role.id, title, this.editRoleSeats),
      `${title} saved.`,
      () => this.editingRoleId.set(null),
    );
  }

  removeRole(found: GroupDetail, role: GroupRole): void {
    this.act(this.api.removeGroupRole(found.group.id, role.id), `${role.title} is no longer a role.`);
  }

  assignRole(found: GroupDetail, member: GroupRosterMember, roleId: string): void {
    if (roleId === member.roleId) {
      return;
    }

    const title = found.roles.find((r) => r.id === roleId)?.title ?? 'that role';

    this.act(
      this.api.assignGroupRole(found.group.id, member.memberId, roleId),
      `${this.memberName(member.memberId)} is now ${title}.`,
    );
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

  /**
   * Whether this caller may run this group's elections - the service's own
   * answer, the same for every election in the group.
   */
  canRun(found: GroupDetail): boolean {
    return found.group.iCanRunElections;
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

    // A group with no president most likely wants its President election
    // first, so that is picked to start with; otherwise nothing is.
    this.roleId =
      found.group.presidentMemberId === null
        ? (found.roles.find((r) => r.kind === 'President')?.id ?? '')
        : '';
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
      this.roleId.length > 0 &&
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

    const title = found.roles.find((r) => r.id === this.roleId)?.title ?? 'new';

    this.act(
      this.api.openGroupElection(found.group.id, {
        roleId: this.roleId,
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

    this.editRoleId = election.roleId;
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
      this.editRoleId.length > 0 &&
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
        roleId: this.editRoleId,
        nominationStartAt: new Date(this.editNominationStartAt).toISOString(),
        nominationEndAt: new Date(this.editNominationEndAt).toISOString(),
        votingStartAt: new Date(this.editVotingStartAt).toISOString(),
        votingEndAt: new Date(this.editVotingEndAt).toISOString(),
      }),
      'Election updated.',
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
      `The ${election.positionTitle} result is announced, and the winners now hold the role.`,
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
