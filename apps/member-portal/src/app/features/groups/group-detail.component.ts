import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { GroupsApi } from './groups.api';
import { GroupApplication, GroupDetail } from './groups.models';

/**
 * Group detail, from the member-portal wireframe's `#groupdetail` screen.
 *
 * The wireframe's two cards are both here: About, with the Apply button, and
 * "My Application Status". Its subtitle - "President: Rajesh Jain • 82 members
 * • Social Service" - drops the president's name for the same reason the list
 * does, and keeps the count and the focus area, which the group does carry.
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
            <h3>About</h3>

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
            <h3>My application status</h3>

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
                  <td>{{ nameFor(person.memberId, found) }}</td>
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
                      }
                    </td>
                  }
                </tr>
              }
            </table>
          </div>
        }

        <!-- The president's queue --------------------------------------- -->
        @if (found.group.iAmThePresident) {
          <h2 class="section-heading">Applications</h2>

          @if (loadingApplications()) {
            <p role="status">Loading applications…</p>
          } @else if (pending().length === 0) {
            <p class="small">Nobody is waiting.</p>
          } @else {
            <div class="queue">
              @for (application of pending(); track application.id) {
                <div class="card">
                  <p><b>{{ nameFor(application.memberId, found) }}</b> asked to join
                    {{ date(application.createdAt) }}.</p>

                  @if (application.note; as note) {
                    <p class="group-body">{{ note }}</p>
                  } @else {
                    <p class="small">They did not add a note.</p>
                  }

                  @if (decisionError()[application.id]; as message) {
                    <p class="notice error" role="alert">{{ message }}</p>
                  }

                  <label [for]="'role-' + application.id">
                    Position to give them (optional)
                  </label>
                  <input
                    class="input"
                    [id]="'role-' + application.id"
                    [name]="'role-' + application.id"
                    [(ngModel)]="roleDrafts[application.id]"
                    maxlength="100"
                    placeholder="Volunteer"
                  />

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
    </div>
  `,
})
export class GroupDetailComponent implements OnInit {
  private readonly api = inject(GroupsApi);
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

  note = '';
  positionDraft = '';
  roleDrafts: Record<string, string> = {};

  private readonly me = computed(() => this.auth.user()?.userId ?? null);

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

        // Only the president may read these, and the endpoint answers 404 to
        // anyone else - asking speculatively would mean a 404 on every
        // ordinary member's visit.
        if (found.group.iAmThePresident) {
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
      },
      error: () => {
        // The group is still readable without its queue, so this does not
        // become a page-level error.
        this.applications.set([]);
        this.loadingApplications.set(false);
      },
    });
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

    const role = (this.roleDrafts[application.id] ?? '').trim();

    this.api
      .decide(found.group.id, application.id, accept, role.length > 0 ? role : null)
      .subscribe({
        next: () => {
          delete this.roleDrafts[application.id];
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

  // ---- Rendering --------------------------------------------------------

  /**
   * What to call somebody.
   *
   * Ids, not names - names live in member-family-service. The reader is named
   * "You", which is the one identity this screen can resolve and the one that
   * matters most on it.
   */
  nameFor(memberId: string, detail: GroupDetail): string {
    if (memberId === this.me()) {
      return 'You';
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
