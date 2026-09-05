import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { VolunteerGroup } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * Volunteer groups — creating them, and standing them down.
 *
 * Wireframe `#groups` covers the member-facing directory; no wireframe covers
 * administering it.
 *
 * **A group could only ever be created with curl, and a president is part of
 * creating one.** `CreateGroupCommand` takes the president's member id and the
 * service installs them, because a group with nobody able to decide its
 * applications is a group whose join requests go nowhere — the same shape of
 * dead end as a Pathshala enrolment nobody could place.
 *
 * **The president is the reason `VolunteerGroups.Lead` exists.** A president is
 * an ordinary member, so gating their own group's decisions on an admin
 * permission would have made those endpoints unreachable by the only person
 * who should reach them. Choosing one here is therefore choosing who holds that
 * power, which is why the form names a member rather than accepting a typed id.
 *
 * **Inactive is not deletion.** The group keeps its members and its history and
 * simply takes no new applications — a Samaaj that ran a seva group for one
 * monsoon should still be able to see who was in it — so the action is
 * reversible and the screen says so.
 */
@Component({
  selector: 'app-groups-list',
  imports: [FormsModule],
  template: `
    <h1 class="title">Volunteer groups</h1>
    <p class="sub">Set a group up, name its president, and stand one down.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the community module, which volunteer groups sit
        behind. Switch it on under the Samaaj's settings.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else {
      <div class="card">
        <h2>Groups</h2>

        @if (groups().length === 0) {
          <p class="empty">None yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Volunteer groups</caption>
              <thead>
                <tr>
                  <th>Group</th><th>Focus</th><th>President</th>
                  <th>Members</th><th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (group of groups(); track group.id) {
                  <tr>
                    <td>
                      <b>{{ group.name }}</b>
                      @if (group.description) {
                        <div class="muted">{{ group.description }}</div>
                      }
                    </td>
                    <td>{{ group.focusArea ?? '—' }}</td>
                    <td>
                      {{ memberName(group.presidentMemberId) }}
                      <div>
                        <button
                          class="btn link small"
                          type="button"
                          [attr.aria-expanded]="presidentFor() === group.id"
                          (click)="openChangePresident(group)"
                        >
                          Change
                        </button>
                      </div>
                    </td>
                    <td>{{ group.memberCount }}</td>
                    <td>
                      <span class="pill" [class.warn]="group.status === 'Inactive'">
                        {{ group.status }}
                      </span>
                    </td>
                    <td>
                      @if (group.status === 'Active') {
                        <button class="btn small alt" type="button" [disabled]="busy()"
                          (click)="setStatus(group, 'Inactive')">
                          Stand down
                        </button>
                      } @else {
                        <button class="btn small" type="button" [disabled]="busy()"
                          (click)="setStatus(group, 'Active')">
                          Bring back
                        </button>
                      }
                    </td>
                  </tr>

                  @if (presidentFor() === group.id) {
                    <tr>
                      <td colspan="6">
                        <div class="notice" role="status">
                          <label [for]="group.id + '-newpres'">New president</label>
                          <select
                            class="input"
                            [id]="group.id + '-newpres'"
                            [(ngModel)]="newPresidentMemberId"
                          >
                            <option value="">Choose a member…</option>
                            @for (member of members(); track member.id) {
                              <option [value]="member.id">{{ member.fullName }}</option>
                            }
                          </select>

                          <p class="small">
                            The outgoing president, {{ memberName(group.presidentMemberId) }},
                            stays in the group as an ordinary member rather than being removed
                            as a side effect of this change.
                          </p>

                          <div class="actions">
                            <button
                              class="btn small"
                              type="button"
                              [disabled]="busy() || newPresidentMemberId.length === 0"
                              (click)="changePresident(group)"
                            >
                              Hand over
                            </button>
                            <button
                              class="btn small alt"
                              type="button"
                              (click)="presidentFor.set(null)"
                            >
                              Cancel
                            </button>
                          </div>
                        </div>
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>

          <p class="small">
            Standing a group down keeps its members and its history; it simply stops taking new
            applications. It can be brought back.
          </p>
        }
      </div>

      <!-- Create --------------------------------------------------------- -->
      <div class="card spaced">
        <h2>Set up a group</h2>

        <form (ngSubmit)="create()">
          <label for="group-name">Name</label>
          <input id="group-name" class="input" name="name" [(ngModel)]="name"
            maxlength="200" placeholder="Seva Group" />

          <label for="group-description">Description</label>
          <input id="group-description" class="input" name="description"
            [(ngModel)]="description" maxlength="1000" />

          <label for="group-focus">Focus area</label>
          <input id="group-focus" class="input" name="focusArea" [(ngModel)]="focusArea"
            maxlength="120" placeholder="Social Service" />

          <label for="group-president">President</label>
          <select id="group-president" class="input" name="president"
            [(ngModel)]="presidentMemberId">
            <option value="">Choose a member…</option>
            @for (member of members(); track member.id) {
              <option [value]="member.id">{{ member.fullName }}</option>
            }
          </select>

          <p class="small">
            A group needs one from the start: the president is who decides its applications, and
            a group without one is a group whose join requests go nowhere.
          </p>

          <button class="btn" type="submit" [disabled]="busy() || !canCreate()">
            Create group
          </button>
        </form>
      </div>
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }
  `,
})
export class GroupsListComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);

  readonly groups = signal<readonly VolunteerGroup[]>([]);
  readonly members = signal<readonly { id: string; fullName: string }[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  name = '';
  description = '';
  focusArea = '';
  presidentMemberId = '';

  private readonly names = computed(
    () => new Map(this.members().map((m) => [m.id, m.fullName])),
  );

  ngOnInit(): void {
    this.load();
  }

  canCreate(): boolean {
    return this.name.trim().length > 0 && this.presidentMemberId.length > 0;
  }

  memberName(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.listGroups().subscribe({
      next: (found) => {
        this.groups.set(found);
        this.loading.set(false);
        this.loadMembers();
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.moduleOff.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  /**
   * The directory, for the president column and the create form.
   *
   * Unlike the other screens' name lookups this one is not purely cosmetic —
   * without it the form has nobody to choose — but a failure still leaves the
   * table readable, so it does not raise an error of its own.
   */
  private loadMembers(): void {
    this.api.listMembers().subscribe({
      next: (found) => this.members.set(found),
      error: () => this.members.set([]),
    });
  }

  create(): void {
    if (!this.canCreate()) {
      return;
    }

    const name = this.name.trim();

    this.act(
      this.api.createGroup(
        name,
        blankToNull(this.description),
        blankToNull(this.focusArea),
        this.presidentMemberId,
      ),
      `${name} created, with ${this.memberName(this.presidentMemberId)} as president.`,
      () => {
        this.name = '';
        this.description = '';
        this.focusArea = '';
        this.presidentMemberId = '';
      },
    );
  }

  setStatus(group: VolunteerGroup, status: 'Active' | 'Inactive'): void {
    this.act(
      this.api.setGroupStatus(group.id, status),
      status === 'Inactive'
        ? `${group.name} is stood down. Its members and history are kept.`
        : `${group.name} is active again.`,
    );
  }

  // ---- Changing a president ----------------------------------------------

  readonly presidentFor = signal<string | null>(null);

  newPresidentMemberId = '';

  openChangePresident(group: VolunteerGroup): void {
    this.newPresidentMemberId = '';
    this.presidentFor.set(this.presidentFor() === group.id ? null : group.id);
  }

  changePresident(group: VolunteerGroup): void {
    if (this.newPresidentMemberId.length === 0) {
      return;
    }

    const newPresident = this.newPresidentMemberId;

    this.act(
      this.api.changeGroupPresident(group.id, newPresident),
      `${group.name} is now led by ${this.memberName(newPresident)}.`,
      () => this.presidentFor.set(null),
    );
  }

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
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed.length === 0 ? null : trimmed;
}
