import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { MembersApi } from '../members/members.api';
import { GroupsApi } from './groups.api';
import {
  Election,
  ElectionDetail,
  GroupApplication,
  GroupDetail,
  VolunteerGroup,
  electionStageLabel,
  votingNote,
} from './groups.models';

/**
 * Group detail, from the member-portal wireframe's `#groupdetail` screen.
 *
 * The wireframe's two cards are both here: About, with the Apply button, and
 * "My Application Status". Its subtitle keeps the count and the focus area,
 * which the group does carry, and drops the president's name from that one
 * line the same way the rest of this screen used to drop every name - see
 * below for why that changed.
 *
 * The **president's review queue** is the substantial addition. The wireframe
 * has no screen for it, but the endpoint exists, deciding an application is
 * the other half of applying, and without somewhere to do it every application
 * a member sends sits unanswered forever. It is only fetched when the group
 * says the reader is its president: the endpoint answers 404 to anyone else,
 * and asking speculatively would mean a 404 on every ordinary member's visit.
 *
 * Assigning a position is here for the same reason - the wireframe's own
 * subtitle promises "Group President assigns roles/positions" and nothing else
 * in the portal does it.
 *
 * **Every member and applicant is named and linked to their profile.**
 * volunteer-groups-service's own response only ever carries a member id -
 * this screen used to render every id but the reader's own as "The
 * president" or "A member", which left a president reviewing several
 * applications at once unable to tell them apart, or look the applicant up,
 * without opening curl. `MembersApi.get(id)` resolves each distinct id this
 * screen shows, once, the same "the client resolves names once, in the
 * response" pattern this codebase already uses elsewhere - a handful of
 * calls bounded by this one group's own roster, not the whole Samaaj
 * directory, so it stays correct however large the Samaaj is. A name still
 * reads as "You" for the viewer's own row, and falls back to "A member"
 * only while a name is still loading or if that one lookup fails.
 *
 * **Positions are filled by election now, added 2026-09-12.** Accepting a
 * join request used to carry an optional free-text position in the same
 * breath - a unilateral president decision with no request, no nomination,
 * no vote - and that input is gone from the review queue below. The
 * "Positions" section is new: the president opens an election for a named
 * position, any current group member nominates another, the president
 * approves nominations onto the ballot, voting opens, and publishing installs
 * the winner - which then shows up in the Members table above like any other
 * position. `AssignRolePositionCommand`'s own "Give a position"/"Change
 * position" control on each member row is untouched and still there as the
 * manual override it always was; nothing about elections removes it.
 *
 * The nominee picker is a `<select>` built from this group's own roster
 * (`found.members`), not a name search: unlike Celebrities of Samaaj's
 * Samaaj-wide ballot, a group's roster is already loaded and small, so there
 * is no case here for the same autosuggest-over-typed-id fix that screen
 * needed.
 */
@Component({
  selector: 'app-group-detail',
  imports: [FormsModule, RouterLink],
  styleUrl: './groups.css',
  template: `
    <div class="groups-page">
      <a class="back" routerLink="/groups">‹ Back to Volunteer Groups</a>

      @if (loading()) {
        <p role="status">Loading the group…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (detail(); as found) {
        <h1 class="page-title">{{ found.group.name }}</h1>
        <p class="subtitle">
          {{ found.group.memberCount }}
          {{ found.group.memberCount === 1 ? 'member' : 'members' }}
          @if (found.group.focusArea; as area) {
            • {{ area }}
          }
        </p>

        @if (found.group.status === 'Inactive') {
          <p class="notice info" role="status">
            This group is not active. It keeps its members and its history, and takes no new
            applications.
          </p>
        }

        <div class="grid2">
          <!-- About ----------------------------------------------------- -->
          <div class="card">
            <h2>About</h2>

            @if (found.group.description; as description) {
              <p class="group-body">{{ description }}</p>
            } @else {
              <p class="small">No description was given.</p>
            }

            @if (applyError(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <div class="actions">
              @if (canApply(found)) {
                <button class="btn" type="button" [disabled]="busy()" (click)="showApply.set(true)">
                  Apply to join
                </button>
              }
            </div>

            @if (showApply() && canApply(found)) {
              <form class="apply" (ngSubmit)="apply()">
                <label for="apply-note">Anything the president should know? (optional)</label>
                <textarea
                  class="input"
                  id="apply-note"
                  name="note"
                  [(ngModel)]="note"
                  rows="3"
                  maxlength="1000"
                  placeholder="Why you would like to join, or when you are free."
                ></textarea>

                <div class="actions">
                  <button class="btn" type="submit" [disabled]="busy()">
                    {{ busy() ? 'Sending…' : 'Send application' }}
                  </button>
                  <button class="btn secondary" type="button" (click)="cancelApply()">
                    Cancel
                  </button>
                </div>
              </form>
            }
          </div>

          <!-- My application status ------------------------------------- -->
          <div class="card">
            <h2>My application status</h2>

            <p>
              <span class="pill" [class]="standingClass(found)">{{ standing(found) }}</span>
            </p>

            @if (found.group.myApplicationStatus === 'Pending') {
              <p class="small">
                The president will decide. There is no notification channel on the platform yet,
                so check back here.
              </p>
            } @else if (found.group.myApplicationStatus === 'Rejected') {
              <p class="small">
                You can apply again. The president may have wanted something the application did
                not say.
              </p>
            }
          </div>
        </div>

        <!-- Members ----------------------------------------------------- -->
        <h2 class="section-heading">Members</h2>

        @if (found.members.length === 0) {
          <p class="small">Nobody has joined yet.</p>
        } @else {
          <div class="table-scroll">
            <table>
              <caption class="sr-only">Group members</caption>
              <tr>
                <th scope="col">Member</th>
                <th scope="col">Position</th>
                <th scope="col">Joined</th>
                @if (found.group.iAmThePresident) {
                  <th scope="col"><span class="sr-only">Actions</span></th>
                }
              </tr>
              @for (person of found.members; track person.memberId) {
                <tr>
                  <td>
                    @if (person.memberId === me()) {
                      {{ nameFor(person.memberId, found) }}
                    } @else {
                      <a [routerLink]="['/members', person.memberId]">{{ nameFor(person.memberId, found) }}</a>
                    }
                  </td>
                  <td>{{ person.rolePosition ?? '—' }}</td>
                  <td>{{ date(person.joinedAt) }}</td>

                  @if (found.group.iAmThePresident) {
                    <td>
                      @if (editingPosition() === person.memberId) {
                        <form class="inline-form" (ngSubmit)="savePosition(person.memberId)">
                          <label class="sr-only" [for]="'position-' + person.memberId">
                            Position
                          </label>
                          <input
                            class="input"
                            [id]="'position-' + person.memberId"
                            name="position"
                            [(ngModel)]="positionDraft"
                            maxlength="100"
                            placeholder="Secretary"
                          />
                          <button class="btn small" type="submit" [disabled]="busy()">Save</button>
                          <button
                            class="btn small secondary"
                            type="button"
                            (click)="editingPosition.set(null)"
                          >
                            Cancel
                          </button>
                        </form>
                      } @else {
                        <button
                          class="btn link"
                          type="button"
                          (click)="editPosition(person.memberId, person.rolePosition)"
                        >
                          {{ person.rolePosition ? 'Change position' : 'Give a position' }}
                        </button>
                        <!--
                          Never offered for the president's own row: the
                          service refuses that with a 409, and a button that
                          always fails is worse than no button.
                        -->
                        @if (person.memberId !== found.group.presidentMemberId) {
                          <button
                            class="btn link"
                            type="button"
                            [disabled]="busy()"
                            (click)="removeMember(person.memberId)"
                          >
                            Remove
                          </button>
                        }
                      }
                    </td>
                  }
                </tr>
              }
            </table>
          </div>
        }

        <!-- The queue: the president's, or an admin's while there is none --- -->
        @if (found.group.iCanLead) {
          <h2 class="section-heading">Applications</h2>

          @if (loadingApplications()) {
            <p role="status">Loading applications…</p>
          } @else if (pending().length === 0) {
            <p class="small">Nobody is waiting.</p>
          } @else {
            <div class="queue">
              @for (application of pending(); track application.id) {
                <div class="card">
                  <p>
                    <a [routerLink]="['/members', application.memberId]">
                      <b>{{ nameFor(application.memberId, found) }}</b>
                    </a>
                    asked to join {{ date(application.createdAt) }}.
                  </p>

                  @if (application.note; as note) {
                    <p class="group-body">{{ note }}</p>
                  } @else {
                    <p class="small">They did not add a note.</p>
                  }

                  @if (decisionError()[application.id]; as message) {
                    <p class="notice error" role="alert">{{ message }}</p>
                  }

                  <div class="actions">
                    <button
                      class="btn"
                      type="button"
                      [disabled]="busy()"
                      (click)="decide(application, true)"
                    >
                      Accept
                    </button>
                    <button
                      class="btn secondary"
                      type="button"
                      [disabled]="busy()"
                      (click)="decide(application, false)"
                    >
                      Turn down
                    </button>
                  </div>
                </div>
              }
            </div>
          }
        }

        @if (found.group.presidentMemberId === null) {
          <p class="notice info" role="status">
            This group has no president yet. A Samaaj admin accepts its joiners and can set a
            president, or run a "President" election that members nominate and vote in.
          </p>
        }

        <!-- Positions, filled by election ---------------------------- -->
        <h2 class="section-heading">Positions</h2>
        <p class="small">
          A position comes from an election now, not from the president's own say-so.
        </p>

        @if (found.group.iCanLead) {
          <div class="actions">
            <button
              class="btn secondary"
              type="button"
              (click)="openElectionForm(found.group)"
            >
              {{ showOpenElection() ? 'Cancel' : 'Open an election' }}
            </button>
          </div>

          @if (showOpenElection()) {
            <form class="card" (ngSubmit)="openElection(found.group.id)">
              <div class="form-grid">
                <div class="field span-all">
                  <label for="election-position">Position</label>
                  <input
                    class="input"
                    id="election-position"
                    name="positionTitle"
                    [(ngModel)]="positionTitle"
                    [readonly]="!found.group.iAmThePresident"
                    maxlength="100"
                    placeholder="Secretary"
                  />
                </div>

                <div class="field">
                  <label for="nomination-start">Nominations open</label>
                  <input
                    class="input"
                    id="nomination-start"
                    type="datetime-local"
                    name="nominationStartAt"
                    [(ngModel)]="nominationStartAt"
                  />
                </div>

                <div class="field">
                  <label for="nomination-end">Nominations close</label>
                  <input
                    class="input"
                    id="nomination-end"
                    type="datetime-local"
                    name="nominationEndAt"
                    [(ngModel)]="nominationEndAt"
                  />
                </div>

                <div class="field">
                  <label for="voting-start">Voting opens</label>
                  <input
                    class="input"
                    id="voting-start"
                    type="datetime-local"
                    name="votingStartAt"
                    [(ngModel)]="votingStartAt"
                  />
                </div>

                <div class="field">
                  <label for="voting-end">Voting closes</label>
                  <input
                    class="input"
                    id="voting-end"
                    type="datetime-local"
                    name="votingEndAt"
                    [(ngModel)]="votingEndAt"
                  />
                </div>
              </div>

              @if (openElectionError(); as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              <div class="actions">
                <button class="btn" type="submit" [disabled]="electionBusy() || !positionTitle.trim()">
                  Open
                </button>
              </div>
            </form>
          }
        }

        @if (loadingElections()) {
          <p role="status">Loading elections…</p>
        } @else if (elections().length === 0) {
          <p class="small">No elections have been run for this group yet.</p>
        } @else {
          <div class="queue">
            @for (election of elections(); track election.id) {
              <div class="card">
                <h3>{{ election.positionTitle }}</h3>
                <p>
                  <span class="pill" [class]="electionPillClass(election)">
                    {{ electionStage(election) }}
                  </span>
                </p>

                @if (election.winnerMemberId; as winnerId) {
                  <p>
                    Elected:
                    <a [routerLink]="['/members', winnerId]">{{ nameFor(winnerId, found) }}</a>
                  </p>
                }

                @if (electionActionError()[election.id]; as message) {
                  <p class="notice error" role="alert">{{ message }}</p>
                }

                <div class="actions">
                  <button class="btn link" type="button" (click)="toggleElection(election.id)">
                    {{ expandedElectionId() === election.id ? 'Hide ballot' : 'View ballot' }}
                  </button>

                  @if (found.group.iCanLead) {
                    @if (canEditElection(election)) {
                      <button class="btn link" type="button" [disabled]="electionBusy()"
                        (click)="toggleEditElection(election)">
                        {{ editingElectionId() === election.id ? 'Cancel edit' : 'Edit' }}
                      </button>
                    }

                    @if (election.status === 'Draft') {
                      <button class="btn small" type="button" [disabled]="electionBusy()"
                        (click)="moveElection(election.id, 'NominationsOpen')">
                        Open nominations
                      </button>
                    } @else if (election.status === 'NominationsOpen') {
                      <button class="btn small" type="button" [disabled]="electionBusy()"
                        (click)="moveElection(election.id, 'VotingOpen')">
                        Open voting
                      </button>
                    } @else if (election.status === 'VotingOpen') {
                      <button class="btn small" type="button" [disabled]="electionBusy()"
                        (click)="moveElection(election.id, 'Closed')">
                        Close
                      </button>
                    } @else if (election.status === 'Closed') {
                      <button class="btn small" type="button" [disabled]="electionBusy()"
                        (click)="publishElection(election.id)">
                        Publish result
                      </button>
                    }
                  }
                </div>

                @if (editingElectionId() === election.id) {
                  <!-- Reopens nominations that closed too early: pushing the
                       end date out, and the voting window after it if it needs
                       to move too. Nothing here moves the election's status. -->
                  <form class="card" (ngSubmit)="saveElectionEdit(election.id)">
                    <div class="form-grid">
                      <div class="field span-all">
                        <label [for]="'edit-position-' + election.id">Position</label>
                        <input
                          class="input"
                          [id]="'edit-position-' + election.id"
                          name="editPositionTitle"
                          [(ngModel)]="editPositionTitle"
                          maxlength="100"
                        />
                      </div>

                      <div class="field">
                        <label [for]="'edit-nom-start-' + election.id">Nominations open</label>
                        <input class="input" type="datetime-local"
                          [id]="'edit-nom-start-' + election.id"
                          name="editNominationStartAt" [(ngModel)]="editNominationStartAt" />
                      </div>

                      <div class="field">
                        <label [for]="'edit-nom-end-' + election.id">Nominations close</label>
                        <input class="input" type="datetime-local"
                          [id]="'edit-nom-end-' + election.id"
                          name="editNominationEndAt" [(ngModel)]="editNominationEndAt" />
                      </div>

                      <div class="field">
                        <label [for]="'edit-vote-start-' + election.id">Voting opens</label>
                        <input class="input" type="datetime-local"
                          [id]="'edit-vote-start-' + election.id"
                          name="editVotingStartAt" [(ngModel)]="editVotingStartAt" />
                      </div>

                      <div class="field">
                        <label [for]="'edit-vote-end-' + election.id">Voting closes</label>
                        <input class="input" type="datetime-local"
                          [id]="'edit-vote-end-' + election.id"
                          name="editVotingEndAt" [(ngModel)]="editVotingEndAt" />
                      </div>
                    </div>

                    @if (editElectionError(); as message) {
                      <p class="notice error" role="alert">{{ message }}</p>
                    }

                    <div class="actions">
                      <button class="btn small" type="submit"
                        [disabled]="electionBusy() || !editPositionTitle.trim()">
                        Save
                      </button>
                      <button class="btn small secondary" type="button"
                        (click)="editingElectionId.set(null)">
                        Cancel
                      </button>
                    </div>
                  </form>
                }

                @if (expandedElectionId() === election.id && expandedDetail(); as detail) {
                  @if (election.acceptsNominations && !isInGroup(found.group)) {
                    <!-- The electorate is the group's own members: the service refuses a
                         nomination from anybody outside it, and only a member can be
                         nominated. A picker here would be blank for exactly the person
                         who cannot use it. -->
                    <h3 class="section-heading">Nominate somebody</h3>
                    <p class="notice info" role="status">
                      Only members of this group can nominate, be nominated, or vote.
                      @if (found.members.length === 0) {
                        Nobody has joined yet: members ask to join, and
                        {{ found.group.iCanLead ? 'you accept them in Applications above' : 'a Samaaj admin accepts them' }}
                        - then they can be nominated here.
                      } @else {
                        Ask to join this group to take part.
                      }
                    </p>
                  }

                  @if (election.acceptsNominations && isInGroup(found.group)) {
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
                        <option value="">Choose a member…</option>
                        @for (person of found.members; track person.memberId) {
                          <option [value]="person.memberId">{{ nameFor(person.memberId, found) }}</option>
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
                  }

                  @if (found.group.iCanLead) {
                    @for (candidate of waitingCandidates(detail); track candidate.id) {
                      <div class="candidate-row">
                        <span>{{ nameFor(candidate.memberId, found) }}</span>
                        <div class="actions">
                          <button class="btn small" type="button" [disabled]="electionBusy()"
                            (click)="decideCandidate(election.id, candidate.id, true)">
                            Approve
                          </button>
                          <button class="btn small secondary" type="button" [disabled]="electionBusy()"
                            (click)="decideCandidate(election.id, candidate.id, false)">
                            Remove
                          </button>
                        </div>
                      </div>
                    }
                  }

                  <h3 class="section-heading">{{ election.status === 'Published' ? 'Result' : 'Ballot' }}</h3>

                  @if (voteNote(election, found.group, approvedCandidates(detail).length); as note) {
                    <p class="notice info" role="status">{{ note }}</p>
                  }

                  @if (approvedCandidates(detail).length === 0) {
                    <p class="small">Nobody is on the ballot yet.</p>
                  } @else {
                    @for (candidate of approvedCandidates(detail); track candidate.id) {
                      <div class="candidate-row">
                        <span>{{ nameFor(candidate.memberId, found) }}</span>
                        <span class="small">{{ candidate.votes }} {{ candidate.votes === 1 ? 'vote' : 'votes' }}</span>

                        @if (election.myVoteCandidateId === candidate.id) {
                          <span class="pill ok">Your vote</span>
                        } @else if (election.acceptsVotes && election.myVoteCandidateId === null) {
                          @if (candidate.memberId === me()) {
                            <span class="small">You cannot vote for yourself.</span>
                          } @else {
                            <button class="btn small" type="button" [disabled]="electionBusy()"
                              (click)="castVote(election.id, candidate.id)">
                              Vote
                            </button>
                          }
                        }
                      </div>
                    }
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
export class GroupDetailComponent implements OnInit {
  private readonly api = inject(GroupsApi);
  private readonly members = inject(MembersApi);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly detail = signal<GroupDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly applications = signal<readonly GroupApplication[]>([]);
  readonly loadingApplications = signal(false);

  readonly busy = signal(false);
  readonly showApply = signal(false);
  readonly applyError = signal<string | null>(null);

  /** Per-application failures, so one refusal does not blank the queue. */
  readonly decisionError = signal<Record<string, string>>({});

  readonly editingPosition = signal<string | null>(null);

  /** Resolved names, by member id - see this component's own doc comment on why. */
  readonly names = signal<Record<string, string>>({});

  note = '';
  positionDraft = '';

  // ---- Elections ----------------------------------------------------------

  readonly elections = signal<readonly Election[]>([]);
  readonly loadingElections = signal(false);

  readonly showOpenElection = signal(false);
  readonly openElectionError = signal<string | null>(null);
  readonly electionBusy = signal(false);

  positionTitle = '';

  /**
   * Opens the election form. An admin running a group that has no president may
   * run only the "President" election, so the field is filled in and locked for
   * them; the president may name any position.
   */
  openElectionForm(group: VolunteerGroup): void {
    if (this.showOpenElection()) {
      this.showOpenElection.set(false);
      return;
    }

    if (!group.iAmThePresident) {
      this.positionTitle = 'President';
    }

    this.showOpenElection.set(true);
  }
  nominationStartAt = '';
  nominationEndAt = '';
  votingStartAt = '';
  votingEndAt = '';

  // ---- Correcting an election: reopening nominations that closed too early --

  readonly editingElectionId = signal<string | null>(null);
  readonly editElectionError = signal<string | null>(null);

  editPositionTitle = '';
  editNominationStartAt = '';
  editNominationEndAt = '';
  editVotingStartAt = '';
  editVotingEndAt = '';

  /** Which election's ballot is expanded, and its full detail once fetched. */
  readonly expandedElectionId = signal<string | null>(null);
  readonly expandedDetail = signal<ElectionDetail | null>(null);

  /** Per-election nominee-picker draft, so opening one ballot does not clear another's. */
  readonly nomineeDrafts = signal<Record<string, string>>({});

  /** Per-election action failures, so one refusal does not blank the list. */
  readonly electionActionError = signal<Record<string, string>>({});

  readonly me = computed(() => this.auth.user()?.userId ?? null);

  /**
   * Only what is still waiting.
   *
   * The endpoint returns the queue, and a decided application is not a queue
   * item - showing it with Accept and Turn down buttons would invite deciding
   * something already decided.
   */
  readonly pending = computed(() =>
    this.applications().filter((application) => application.status === 'Pending'),
  );

  ngOnInit(): void {
    // Roles come from /me and the screen labels the reader as "You"; loading
    // the group first would render a members table before that is known.
    this.auth.ensureCurrentUser().subscribe({
      next: () => this.load(),
      error: () => this.load(),
    });
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name a group.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.get(id).subscribe({
      next: (found) => {
        this.detail.set(found);
        this.loading.set(false);
        this.resolveNames(found.members.map((person) => person.memberId));
        this.loadElections(found.group.id);

        // Only the president may read these, and the endpoint answers 404 to
        // anyone else - asking speculatively would mean a 404 on every
        // ordinary member's visit.
        if (found.group.iCanLead) {
          this.loadApplications(found.group.id);
        }
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private loadApplications(id: string): void {
    this.loadingApplications.set(true);

    this.api.applications(id).subscribe({
      next: (found) => {
        this.applications.set(found);
        this.loadingApplications.set(false);
        this.resolveNames(found.map((application) => application.memberId));
      },
      error: () => {
        // The group is still readable without its queue, so this does not
        // become a page-level error.
        this.applications.set([]);
        this.loadingApplications.set(false);
      },
    });
  }

  private loadElections(groupId: string): void {
    this.loadingElections.set(true);

    this.api.listElections(groupId).subscribe({
      next: (found) => {
        this.elections.set(found);
        this.loadingElections.set(false);
      },
      error: () => {
        // The group is still readable without its election history.
        this.elections.set([]);
        this.loadingElections.set(false);
      },
    });
  }

  /**
   * Resolves a member's own full name, one call per not-yet-resolved id.
   * Bounded by this one group's own roster and applications - not the whole
   * Samaaj directory - so it stays cheap and correct regardless of how large
   * the Samaaj is. A failed lookup is left unresolved rather than raised as
   * a page-level error: `nameFor` already has a fallback for that.
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

  // ---- Applying ---------------------------------------------------------

  /**
   * Whether the Apply button belongs on screen.
   *
   * Not a member, not the president, no application outstanding, and the group
   * still active. A rejected application does not block a second try - people
   * ask again, and the president can decide again.
   */
  canApply(detail: GroupDetail): boolean {
    return (
      detail.group.status === 'Active' &&
      !detail.group.iAmAMember &&
      !detail.group.iAmThePresident &&
      detail.group.myApplicationStatus !== 'Pending'
    );
  }

  apply(): void {
    const found = this.detail();

    if (found === null) {
      return;
    }

    this.busy.set(true);
    this.applyError.set(null);

    const note = this.note.trim();

    this.api.apply(found.group.id, note.length > 0 ? note : null).subscribe({
      next: () => {
        this.note = '';
        this.showApply.set(false);
        this.busy.set(false);

        // Re-read: the group carries the member's own standing, and it has
        // just changed.
        this.load();
      },
      error: (failure: unknown) => {
        this.applyError.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  cancelApply(): void {
    this.note = '';
    this.showApply.set(false);
    this.applyError.set(null);
  }

  // ---- The president's decisions -----------------------------------------

  decide(application: GroupApplication, accept: boolean): void {
    const found = this.detail();

    if (found === null) {
      return;
    }

    this.busy.set(true);
    this.clearDecisionError(application.id);

    this.api.decide(found.group.id, application.id, accept).subscribe({
      next: () => {
        this.busy.set(false);

        // Accepting adds a member, so both the group and the queue have
        // moved; `load` re-reads the queue too when the reader is president.
        this.load();
      },
      error: (failure: unknown) => {
        this.setDecisionError(application.id, describeError(failure));
        this.busy.set(false);
      },
    });
  }

  editPosition(memberId: string, current: string | null): void {
    this.positionDraft = current ?? '';
    this.editingPosition.set(memberId);
  }

  savePosition(memberId: string): void {
    const found = this.detail();

    if (found === null) {
      return;
    }

    this.busy.set(true);

    const position = this.positionDraft.trim();

    this.api
      .setPosition(found.group.id, memberId, position.length > 0 ? position : null)
      .subscribe({
        next: (updated) => {
          this.detail.set(updated);
          this.editingPosition.set(null);
          this.busy.set(false);
        },
        error: (failure: unknown) => {
          this.error.set(describeError(failure));
          this.editingPosition.set(null);
          this.busy.set(false);
        },
      });
  }

  /**
   * Removes a member. No confirmation, matching the weight this screen already
   * gives assigning a position or deciding an application - all three are a
   * president acting on somebody else's standing in their own group, and none
   * of the others ask "are you sure?" either. It is also reversible: a removed
   * member can apply again, the same as anyone rejected.
   */
  removeMember(memberId: string): void {
    const found = this.detail();

    if (found === null) {
      return;
    }

    this.busy.set(true);

    this.api.removeMember(found.group.id, memberId).subscribe({
      next: (updated) => {
        this.detail.set(updated);
        this.busy.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  // ---- Elections ----------------------------------------------------------

  openElection(groupId: string): void {
    const title = this.positionTitle.trim();

    if (title.length === 0) {
      return;
    }

    this.electionBusy.set(true);
    this.openElectionError.set(null);

    this.api
      .openElection(
        groupId,
        title,
        new Date(this.nominationStartAt).toISOString(),
        new Date(this.nominationEndAt).toISOString(),
        new Date(this.votingStartAt).toISOString(),
        new Date(this.votingEndAt).toISOString(),
      )
      .subscribe({
        next: () => {
          this.electionBusy.set(false);
          this.showOpenElection.set(false);
          this.positionTitle = '';
          this.nominationStartAt = '';
          this.nominationEndAt = '';
          this.votingStartAt = '';
          this.votingEndAt = '';
          this.loadElections(groupId);
        },
        error: (failure: unknown) => {
          this.openElectionError.set(describeError(failure));
          this.electionBusy.set(false);
        },
      });
  }

  /** Draft or NominationsOpen only - the service refuses the correction from VotingOpen on. */
  canEditElection(election: Election): boolean {
    return election.status === 'Draft' || election.status === 'NominationsOpen';
  }

  toggleEditElection(election: Election): void {
    if (this.editingElectionId() === election.id) {
      this.editingElectionId.set(null);
      return;
    }

    this.editPositionTitle = election.positionTitle;
    this.editNominationStartAt = toLocalInputValue(election.nominationStartAt);
    this.editNominationEndAt = toLocalInputValue(election.nominationEndAt);
    this.editVotingStartAt = toLocalInputValue(election.votingStartAt);
    this.editVotingEndAt = toLocalInputValue(election.votingEndAt);
    this.editElectionError.set(null);
    this.editingElectionId.set(election.id);
  }

  saveElectionEdit(electionId: string): void {
    const title = this.editPositionTitle.trim();

    if (title.length === 0) {
      return;
    }

    this.electionBusy.set(true);
    this.editElectionError.set(null);

    this.api
      .updateElection(
        electionId,
        title,
        new Date(this.editNominationStartAt).toISOString(),
        new Date(this.editNominationEndAt).toISOString(),
        new Date(this.editVotingStartAt).toISOString(),
        new Date(this.editVotingEndAt).toISOString(),
      )
      .subscribe({
        next: () => {
          this.electionBusy.set(false);
          this.editingElectionId.set(null);

          const found = this.detail();

          if (found) {
            this.loadElections(found.group.id);
          }
        },
        error: (failure: unknown) => {
          this.editElectionError.set(describeError(failure));
          this.electionBusy.set(false);
        },
      });
  }

  toggleElection(electionId: string): void {
    if (this.expandedElectionId() === electionId) {
      this.expandedElectionId.set(null);
      this.expandedDetail.set(null);
      return;
    }

    this.expandedElectionId.set(electionId);
    this.expandedDetail.set(null);

    this.api.getElection(electionId).subscribe({
      next: (found) => this.expandedDetail.set(found),
      error: (failure: unknown) => {
        this.setElectionActionError(electionId, describeError(failure));
        this.expandedElectionId.set(null);
      },
    });
  }

  private refreshExpanded(electionId: string): void {
    this.api.getElection(electionId).subscribe({
      next: (found) => this.expandedDetail.set(found),
      error: () => {
        /* The election list itself still reflects the latest status. */
      },
    });
  }

  moveElection(electionId: string, status: string): void {
    const found = this.detail();

    if (found === null) {
      return;
    }

    this.electionBusy.set(true);
    this.clearElectionActionError(electionId);

    this.api.moveElection(electionId, status).subscribe({
      next: () => {
        this.electionBusy.set(false);
        this.loadElections(found.group.id);

        if (this.expandedElectionId() === electionId) {
          this.refreshExpanded(electionId);
        }
      },
      error: (failure: unknown) => {
        this.setElectionActionError(electionId, describeError(failure));
        this.electionBusy.set(false);
      },
    });
  }

  publishElection(electionId: string): void {
    const found = this.detail();

    if (found === null) {
      return;
    }

    this.electionBusy.set(true);
    this.clearElectionActionError(electionId);

    this.api.publishElectionResults(electionId).subscribe({
      next: () => {
        this.electionBusy.set(false);
        this.loadElections(found.group.id);

        // Publishing installs the winner on the group's own roster.
        this.load();
      },
      error: (failure: unknown) => {
        this.setElectionActionError(electionId, describeError(failure));
        this.electionBusy.set(false);
      },
    });
  }

  /** Whether the reader belongs to this group - the electorate for its elections. */
  isInGroup(group: VolunteerGroup): boolean {
    return group.iAmAMember || group.iAmThePresident;
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

  decideCandidate(electionId: string, candidateId: string, approve: boolean): void {
    this.electionBusy.set(true);
    this.clearElectionActionError(electionId);

    this.api.decideCandidate(electionId, candidateId, approve).subscribe({
      next: (updated) => {
        this.electionBusy.set(false);
        this.expandedDetail.set(updated);
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
        this.loadElections(this.detail()!.group.id);
        this.refreshExpanded(electionId);
      },
      error: (failure: unknown) => {
        this.setElectionActionError(electionId, describeError(failure));
        this.electionBusy.set(false);
      },
    });
  }

  waitingCandidates(detail: ElectionDetail): readonly { id: string; memberId: string }[] {
    return detail.candidates.filter((c) => c.status === 'Nominated');
  }

  approvedCandidates(detail: ElectionDetail) {
    return detail.candidates.filter((c) => c.status === 'Approved');
  }

  /** Why the vote button is not there, if it is not - see `votingNote`. */
  voteNote(election: Election, group: VolunteerGroup, onBallot: number): string | null {
    return votingNote(
      election,
      { inGroup: this.isInGroup(group), canLead: group.iCanLead },
      onBallot,
    );
  }

  electionStage(election: Election): string {
    return electionStageLabel(election);
  }

  electionPillClass(election: Election): string {
    if (election.status === 'Published') {
      return 'ok';
    }

    return election.acceptsVotes || election.acceptsNominations ? 'warn' : '';
  }

  private setElectionActionError(id: string, message: string): void {
    this.electionActionError.set({ ...this.electionActionError(), [id]: message });
  }

  private clearElectionActionError(id: string): void {
    const { [id]: _removed, ...rest } = this.electionActionError();

    this.electionActionError.set(rest);
  }

  // ---- Rendering --------------------------------------------------------

  /**
   * What to call somebody. The reader is named "You". Everyone else's real
   * name comes from `resolveNames()` once it has loaded; until then, or if
   * that one lookup failed, this falls back to a generic label rather than
   * showing nothing.
   */
  nameFor(memberId: string, detail: GroupDetail): string {
    if (memberId === this.me()) {
      return 'You';
    }

    const resolved = this.names()[memberId];

    if (resolved) {
      return resolved;
    }

    return memberId === detail.group.presidentMemberId ? 'The president' : 'A member';
  }

  standing(detail: GroupDetail): string {
    if (detail.group.iAmThePresident) {
      return 'You lead this group';
    }

    if (detail.group.iAmAMember) {
      return 'You are a member';
    }

    switch (detail.group.myApplicationStatus) {
      case 'Pending':
        return 'Waiting on the president';
      case 'Rejected':
        return 'Not accepted';
      default:
        return 'No application submitted yet';
    }
  }

  standingClass(detail: GroupDetail): string {
    if (detail.group.iAmThePresident || detail.group.iAmAMember) {
      return 'ok';
    }

    return detail.group.myApplicationStatus === 'Pending' ? 'warn' : '';
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }

  private setDecisionError(id: string, message: string): void {
    this.decisionError.set({ ...this.decisionError(), [id]: message });
  }

  private clearDecisionError(id: string): void {
    const { [id]: _removed, ...rest } = this.decisionError();

    this.decisionError.set(rest);
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
