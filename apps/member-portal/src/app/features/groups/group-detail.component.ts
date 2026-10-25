import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { Observable } from 'rxjs';
import { MembersApi } from '../members/members.api';
import { GroupsApi } from './groups.api';
import {
  Election,
  ElectionDetail,
  GroupApplication,
  GroupDetail,
  GroupRole,
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
 * Assigning a role is here for the same reason - the wireframe's own subtitle
 * promises "Group President assigns roles/positions" and nothing else in the
 * portal does it.
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
 * **Every member holds one of the group's volunteer roles (2026-09-26).**
 * Volunteer and President are built in; the group defines the rest, each with
 * the seats an election for it fills, in the "Volunteer roles" section. The
 * president - or an administrator of the group - defines them, moves a member
 * into one from the Members table (never into or out of President, which moves
 * only with the presidency), and opens an election for one. Any current member
 * nominates another, the leader approves nominations onto the ballot, voting
 * opens, and publishing installs the winners. A role with several seats takes a
 * ballot of several picks - ticked, then cast once, because a ballot is final.
 * `group.iCanRunElections` is the service's own answer to who may do all this.
 *
 * **Laid out like a page since 2026-10-24.** The group's photo is a cover
 * across the top (its initials when there is none), the name, count, focus and
 * president sit under it with the one action the reader has - Join group, or
 * where their application stands - and the sections are tabs: About (with the
 * application form), Members, Applications for whoever decides them, Roles and
 * Elections. The photo is set from the admin panel and fetched here through
 * `AuthedImageDirective`, because it is only served to a signed-in member.
 *
 * The nominee picker is a `<select>` built from this group's own roster
 * (`found.members`), not a name search: unlike Celebrities of Samaaj's
 * Samaaj-wide ballot, a group's roster is already loaded and small, so there
 * is no case here for the same autosuggest-over-typed-id fix that screen
 * needed.
 */
@Component({
  selector: 'app-group-detail',
  imports: [FormsModule, RouterLink, AuthedImageDirective],
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
        <!-- The cover, like a page: the photo across the top, then the name
             and the one thing the reader can do about this group. -->
        <div class="group-cover">
          @if (found.group.photoUrl; as path) {
            <img class="group-cover-img" [scAuthedSrc]="path" alt="" />
          } @else {
            <div class="group-cover-img placeholder" aria-hidden="true">{{ initials(found.group.name) }}</div>
          }
        </div>

        <div class="group-head">
          <div class="group-head-text">
            <h1 class="page-title">{{ found.group.name }}</h1>
            <p class="subtitle">
              {{ found.group.memberCount }}
              {{ found.group.memberCount === 1 ? 'member' : 'members' }}
              @if (found.group.focusArea; as area) {
                • {{ area }}
              }
              @if (found.group.presidentMemberId; as president) {
                • President: <a [routerLink]="['/members', president]">{{ nameFor(president, found) }}</a>
              }
            </p>
          </div>

          <div class="group-head-actions">
            @if (canApply(found)) {
              <button class="btn" type="button" [disabled]="busy()" (click)="startApply()">
                Join group
              </button>
            } @else {
              <span class="pill" [class]="standingClass(found)">{{ standing(found) }}</span>
            }
          </div>
        </div>

        @if (found.group.status === 'Inactive') {
          <p class="notice info" role="status">
            This group is not active. It keeps its members and its history, and takes no new
            applications.
          </p>
        }

        <div class="tabs" role="tablist" aria-label="This group">
          @for (item of tabsFor(found); track item.value) {
            <button
              type="button"
              role="tab"
              [id]="'group-tab-' + item.value"
              [class.on]="tab() === item.value"
              [attr.aria-selected]="tab() === item.value"
              aria-controls="group-tab-panel"
              (click)="tab.set(item.value)"
            >
              {{ item.label }}
            </button>
          }
        </div>

        <div id="group-tab-panel" role="tabpanel" [attr.aria-labelledby]="'group-tab-' + tab()">
        @switch (tab()) {
        @case ('about') {
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

            @if (canApply(found) && !showApply()) {
              <p class="small">Use <b>Join group</b> above to ask the president to let you in.</p>
            }

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

        }
        @case ('members') {
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
                <th scope="col">Role</th>
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
                  <td>
                    <!--
                      The president's own row is never a picker: President moves
                      only with the presidency, and the service refuses anything
                      else for them.
                    -->
                    @if (found.group.iCanRunElections && person.memberId !== found.group.presidentMemberId) {
                      <label class="sr-only" [for]="'role-' + person.memberId">
                        Role for {{ nameFor(person.memberId, found) }}
                      </label>
                      <select
                        class="input"
                        [id]="'role-' + person.memberId"
                        [ngModel]="person.roleId"
                        (ngModelChange)="assignRole(person.memberId, $event)"
                        [disabled]="busy()"
                      >
                        @for (role of assignableRoles(found); track role.id) {
                          <option [value]="role.id">{{ role.title }}</option>
                        }
                      </select>
                    } @else {
                      {{ person.rolePosition ?? 'Volunteer' }}
                    }
                  </td>
                  <td>{{ date(person.joinedAt) }}</td>

                  @if (found.group.iAmThePresident) {
                    <td>
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
                    </td>
                  }
                </tr>
              }
            </table>
          </div>
        }

        }
        @case ('applications') {
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

        }
        @case ('roles') {
        @if (found.group.presidentMemberId === null) {
          <p class="notice info" role="status">
            This group has no president yet. A Samaaj admin accepts its joiners and can set a
            president, or run a "President" election that members nominate and vote in.
          </p>
        }

        <!-- Volunteer roles -------------------------------------------- -->
        <h2 class="section-heading">Volunteer roles</h2>
        <p class="small">
          Everybody in the group holds one role, starting as a Volunteer. Each other role is filled
          by an election for as many seats as it has.
        </p>

        <div class="table-scroll">
          <table>
            <caption class="sr-only">Volunteer roles in this group</caption>
            <tr>
              <th scope="col">Role</th>
              <th scope="col">Seats</th>
              <th scope="col">Held by</th>
              @if (found.group.iCanRunElections) {
                <th scope="col"><span class="sr-only">Actions</span></th>
              }
            </tr>
            @for (role of found.roles; track role.id) {
              <tr>
                @if (editingRoleId() === role.id) {
                  <td>
                    <label class="sr-only" [for]="'role-title-' + role.id">Title</label>
                    <input class="input" [id]="'role-title-' + role.id" name="editRoleTitle"
                      [(ngModel)]="editRoleTitle" maxlength="100" />
                  </td>
                  <td>
                    <label class="sr-only" [for]="'role-seats-' + role.id">Seats</label>
                    <input class="input" type="number" min="1" max="100" [id]="'role-seats-' + role.id"
                      name="editRoleSeats" [(ngModel)]="editRoleSeats" />
                  </td>
                  <td>{{ role.holderCount }}</td>
                  <td>
                    <button class="btn small" type="button" (click)="saveRole(role.id)"
                      [disabled]="busy() || !validRole(editRoleTitle, editRoleSeats)">
                      Save
                    </button>
                    <button class="btn small secondary" type="button" (click)="editingRoleId.set(null)">
                      Cancel
                    </button>
                  </td>
                } @else {
                  <td>{{ role.title }}</td>
                  <td>{{ role.seats ?? 'No limit' }}</td>
                  <td>{{ role.holderCount }}</td>
                  @if (found.group.iCanRunElections) {
                    <td>
                      @if (role.isEditable) {
                        <button class="btn link" type="button" [disabled]="busy()" (click)="startEditRole(role)">
                          Edit<span class="sr-only"> {{ role.title }}</span>
                        </button>
                        <!-- A held role cannot be removed: the service answers 409. -->
                        @if (role.holderCount === 0) {
                          <button class="btn link" type="button" [disabled]="busy()" (click)="removeRole(role.id)">
                            Remove<span class="sr-only"> {{ role.title }}</span>
                          </button>
                        }
                      }
                    </td>
                  }
                }
              </tr>
            }
          </table>
        </div>

        @if (roleError(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        @if (found.group.iCanRunElections) {
          <form class="inline-form" (ngSubmit)="defineRole()">
            <label for="new-role-title">New role</label>
            <input class="input" id="new-role-title" name="newRoleTitle" [(ngModel)]="newRoleTitle"
              maxlength="100" placeholder="Secretary" />
            <label for="new-role-seats">Seats</label>
            <input class="input" id="new-role-seats" type="number" min="1" max="100"
              name="newRoleSeats" [(ngModel)]="newRoleSeats" />
            <button class="btn small" type="submit" [disabled]="busy() || !validRole(newRoleTitle, newRoleSeats)">
              Add role
            </button>
          </form>
        }

        }
        @case ('elections') {
        <!-- Elections, one per role ------------------------------------ -->
        <h2 class="section-heading">Elections</h2>

        @if (found.group.iCanRunElections) {
          <div class="actions">
            <button
              class="btn secondary"
              type="button"
              (click)="openElectionForm(found)"
            >
              {{ showOpenElection() ? 'Cancel' : 'Open an election' }}
            </button>
          </div>

          @if (showOpenElection()) {
            <form class="card" (ngSubmit)="openElection(found.group.id)">
              <div class="form-grid">
                <div class="field span-all">
                  <label for="election-role">Role</label>
                  <select class="input" id="election-role" name="roleId" [(ngModel)]="roleId">
                    <option value="">Choose a role…</option>
                    @for (role of electedRoles(found); track role.id) {
                      <option [value]="role.id">{{ role.title }} ({{ seatsLabel(role.seats ?? 1) }})</option>
                    }
                  </select>
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
                <button class="btn" type="submit" [disabled]="electionBusy() || !roleId">
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
                  <span class="small"> {{ seatsLabel(election.seats) }}</span>
                </p>

                @if (election.winnerMemberIds.length > 0) {
                  <p>
                    Elected:
                    @for (winnerId of election.winnerMemberIds; track winnerId; let last = $last) {
                      <a [routerLink]="['/members', winnerId]">{{ nameFor(winnerId, found) }}</a>{{ last ? '' : ', ' }}
                    }
                  </p>
                }

                @if (electionActionError()[election.id]; as message) {
                  <p class="notice error" role="alert">{{ message }}</p>
                }

                <div class="actions">
                  <button class="btn link" type="button" (click)="toggleElection(election.id)">
                    {{ expandedElectionId() === election.id ? 'Hide ballot' : 'View ballot' }}
                  </button>

                  @if (found.group.iCanRunElections) {
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
                        <label [for]="'edit-role-' + election.id">Role</label>
                        <select class="input" [id]="'edit-role-' + election.id" name="editRoleId"
                          [(ngModel)]="editRoleId">
                          @for (role of electedRoles(found); track role.id) {
                            <option [value]="role.id">{{ role.title }} ({{ seatsLabel(role.seats ?? 1) }})</option>
                          }
                        </select>
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
                        [disabled]="electionBusy() || !editRoleId">
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

                  @if (found.group.iCanRunElections) {
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
                    <p class="notice ok" role="status">{{ note }}</p>
                  }

                  @if (approvedCandidates(detail).length === 0) {
                    <p class="small">Nobody is on the ballot yet.</p>
                  } @else {
                    @if (canBallot(election) && election.seats > 1) {
                      <p class="small">
                        Pick up to {{ election.seats }}, then cast your ballot once. It is final: you
                        cannot come back to add another.
                      </p>
                    }

                    @for (candidate of approvedCandidates(detail); track candidate.id) {
                      <div class="candidate-row">
                        <span>{{ nameFor(candidate.memberId, found) }}</span>
                        <span class="small">{{ candidate.votes }} {{ candidate.votes === 1 ? 'vote' : 'votes' }}</span>

                        @if (election.myVoteCandidateIds.includes(candidate.id)) {
                          <span class="pill ok">Your vote</span>
                        } @else if (canBallot(election)) {
                          @if (candidate.memberId === me()) {
                            <span class="small">You cannot vote for yourself.</span>
                          } @else if (election.seats === 1) {
                            <button class="btn small" type="button" [disabled]="electionBusy()"
                              (click)="castVote(election.id, [candidate.id])">
                              Vote
                            </button>
                          } @else {
                            <label>
                              <input
                                type="checkbox"
                                [checked]="picked(election.id, candidate.id)"
                                [disabled]="!picked(election.id, candidate.id) && picks(election.id).length >= election.seats"
                                (change)="togglePick(election.id, candidate.id)"
                              />
                              Pick<span class="sr-only"> {{ nameFor(candidate.memberId, found) }}</span>
                            </label>
                          }
                        }
                      </div>
                    }

                    @if (canBallot(election) && election.seats > 1) {
                      <div class="actions">
                        <button class="btn" type="button"
                          [disabled]="electionBusy() || picks(election.id).length === 0"
                          (click)="castVote(election.id, picks(election.id))">
                          Cast my ballot ({{ picks(election.id).length }} of {{ election.seats }})
                        </button>
                      </div>
                    }
                  }
                }
              </div>
            }
          </div>
        }
        }
        }
        </div>
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

  /** Which section of the page is showing - About first, like a page's own landing tab. */
  readonly tab = signal<GroupTab>('about');
  readonly applyError = signal<string | null>(null);

  /** Per-application failures, so one refusal does not blank the queue. */
  readonly decisionError = signal<Record<string, string>>({});

  /** Resolved names, by member id - see this component's own doc comment on why. */
  readonly names = signal<Record<string, string>>({});

  note = '';

  // ---- Volunteer roles ----------------------------------------------------

  readonly roleError = signal<string | null>(null);
  readonly editingRoleId = signal<string | null>(null);

  newRoleTitle = '';
  newRoleSeats = 1;
  editRoleTitle = '';
  editRoleSeats = 1;

  // ---- Elections ----------------------------------------------------------

  readonly elections = signal<readonly Election[]>([]);
  readonly loadingElections = signal(false);

  readonly showOpenElection = signal(false);
  readonly openElectionError = signal<string | null>(null);
  readonly electionBusy = signal(false);

  roleId = '';

  /**
   * Opens the election form. A group with no president most likely wants its
   * President election first, so that role is picked to start with.
   */
  openElectionForm(found: GroupDetail): void {
    if (this.showOpenElection()) {
      this.showOpenElection.set(false);
      return;
    }

    this.roleId =
      found.group.presidentMemberId === null
        ? (found.roles.find((role) => role.kind === 'President')?.id ?? '')
        : '';

    this.showOpenElection.set(true);
  }
  nominationStartAt = '';
  nominationEndAt = '';
  votingStartAt = '';
  votingEndAt = '';

  // ---- Correcting an election: reopening nominations that closed too early --

  readonly editingElectionId = signal<string | null>(null);
  readonly editElectionError = signal<string | null>(null);

  editRoleId = '';
  editNominationStartAt = '';
  editNominationEndAt = '';
  editVotingStartAt = '';
  editVotingEndAt = '';

  /** Which election's ballot is expanded, and its full detail once fetched. */
  readonly expandedElectionId = signal<string | null>(null);
  readonly expandedDetail = signal<ElectionDetail | null>(null);

  /** Per-election nominee-picker draft, so opening one ballot does not clear another's. */
  readonly nomineeDrafts = signal<Record<string, string>>({});

  /** Per-election ticked candidates, for a role with more than one seat. */
  readonly ballotDrafts = signal<Record<string, readonly string[]>>({});

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

  // ---- Volunteer roles ---------------------------------------------------

  /** The roles an election can be run for: every one but Volunteer. */
  electedRoles(found: GroupDetail): readonly GroupRole[] {
    return found.roles.filter((role) => role.isElected);
  }

  /** The roles a member can be moved into by hand: every one but President. */
  assignableRoles(found: GroupDetail): readonly GroupRole[] {
    return found.roles.filter((role) => role.kind !== 'President');
  }

  seatsLabel(seats: number): string {
    return seats === 1 ? '1 seat' : `${seats} seats`;
  }

  validRole(title: string, seats: number | null): boolean {
    return title.trim().length > 0 && Number.isInteger(seats) && seats! >= 1 && seats! <= 100;
  }

  defineRole(): void {
    const found = this.detail();
    const title = this.newRoleTitle.trim();

    if (found === null || !this.validRole(title, this.newRoleSeats)) {
      return;
    }

    this.roleAction(this.api.defineRole(found.group.id, title, this.newRoleSeats), () => {
      this.newRoleTitle = '';
      this.newRoleSeats = 1;
    });
  }

  startEditRole(role: GroupRole): void {
    this.editRoleTitle = role.title;
    this.editRoleSeats = role.seats ?? 1;
    this.editingRoleId.set(role.id);
  }

  saveRole(roleId: string): void {
    const found = this.detail();
    const title = this.editRoleTitle.trim();

    if (found === null || !this.validRole(title, this.editRoleSeats)) {
      return;
    }

    this.roleAction(this.api.updateRole(found.group.id, roleId, title, this.editRoleSeats), () =>
      this.editingRoleId.set(null),
    );
  }

  removeRole(roleId: string): void {
    const found = this.detail();

    if (found !== null) {
      this.roleAction(this.api.removeRole(found.group.id, roleId));
    }
  }

  /**
   * Moves a member into a role. A refusal - a full role, say - re-reads the
   * group, because the select has already shown the choice the service refused.
   */
  assignRole(memberId: string, roleId: string): void {
    const found = this.detail();

    if (found === null || found.members.find((m) => m.memberId === memberId)?.roleId === roleId) {
      return;
    }

    this.roleAction(this.api.assignRole(found.group.id, memberId, roleId), undefined, true);
  }

  /** Every role call answers with the whole group, so the screen takes it as it stands. */
  private roleAction(
    work: Observable<GroupDetail>,
    done?: () => void,
    reloadOnFailure = false,
  ): void {
    this.busy.set(true);
    this.roleError.set(null);

    work.subscribe({
      next: (updated) => {
        this.detail.set(updated);
        this.busy.set(false);
        done?.();
      },
      error: (failure: unknown) => {
        this.roleError.set(describeError(failure));
        this.busy.set(false);

        if (reloadOnFailure) {
          this.load();
        }
      },
    });
  }

  /**
   * Removes a member. No confirmation, matching the weight this screen already
   * gives assigning a role or deciding an application - all three are a
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
    if (this.roleId.length === 0) {
      return;
    }

    this.electionBusy.set(true);
    this.openElectionError.set(null);

    this.api
      .openElection(
        groupId,
        this.roleId,
        new Date(this.nominationStartAt).toISOString(),
        new Date(this.nominationEndAt).toISOString(),
        new Date(this.votingStartAt).toISOString(),
        new Date(this.votingEndAt).toISOString(),
      )
      .subscribe({
        next: () => {
          this.electionBusy.set(false);
          this.showOpenElection.set(false);
          this.roleId = '';
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

    this.editRoleId = election.roleId;
    this.editNominationStartAt = toLocalInputValue(election.nominationStartAt);
    this.editNominationEndAt = toLocalInputValue(election.nominationEndAt);
    this.editVotingStartAt = toLocalInputValue(election.votingStartAt);
    this.editVotingEndAt = toLocalInputValue(election.votingEndAt);
    this.editElectionError.set(null);
    this.editingElectionId.set(election.id);
  }

  saveElectionEdit(electionId: string): void {
    if (this.editRoleId.length === 0) {
      return;
    }

    this.electionBusy.set(true);
    this.editElectionError.set(null);

    this.api
      .updateElection(
        electionId,
        this.editRoleId,
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

  /** Whether this reader may still cast a ballot in it: voting is open and they have not voted. */
  canBallot(election: Election): boolean {
    return election.acceptsVotes && election.myVoteCandidateIds.length === 0;
  }

  picks(electionId: string): readonly string[] {
    return this.ballotDrafts()[electionId] ?? [];
  }

  picked(electionId: string, candidateId: string): boolean {
    return this.picks(electionId).includes(candidateId);
  }

  togglePick(electionId: string, candidateId: string): void {
    const current = this.picks(electionId);

    this.ballotDrafts.update((drafts) => ({
      ...drafts,
      [electionId]: current.includes(candidateId)
        ? current.filter((id) => id !== candidateId)
        : [...current, candidateId],
    }));
  }

  castVote(electionId: string, candidateIds: readonly string[]): void {
    this.electionBusy.set(true);
    this.clearElectionActionError(electionId);

    this.api.castVote(electionId, candidateIds).subscribe({
      next: () => {
        this.electionBusy.set(false);
        this.ballotDrafts.update((drafts) => ({ ...drafts, [electionId]: [] }));
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
      { inGroup: this.isInGroup(group), canLead: group.iCanRunElections },
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

  /** The tabs this reader has something on. Applications only for whoever decides them. */
  tabsFor(detail: GroupDetail): readonly { value: GroupTab; label: string }[] {
    const waiting = this.applications().filter((application) => application.status === 'Pending').length;

    return [
      { value: 'about', label: 'About' },
      { value: 'members', label: `Members (${detail.members.length})` },
      ...(detail.group.iCanLead
        ? [{ value: 'applications' as const, label: waiting > 0 ? `Applications (${waiting})` : 'Applications' }]
        : []),
      { value: 'roles', label: 'Roles' },
      { value: 'elections', label: 'Elections' },
    ];
  }

  /** The header's Join group: the application form lives on the About tab. */
  startApply(): void {
    this.tab.set('about');
    this.showApply.set(true);
  }

  /** Up to two initials, for a cover with no photo. */
  initials(name: string): string {
    return name
      .split(/\s+/)
      .filter((part) => part.length > 0)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('');
  }

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

/** The page's sections. */
type GroupTab = 'about' | 'members' | 'applications' | 'roles' | 'elections';
