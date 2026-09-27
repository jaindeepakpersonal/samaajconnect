import {
  Component,
  ElementRef,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { forkJoin } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AreaScope, labelLocalities } from '../../core/area-scope';
import { VolunteerGroup } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';
import { ALL_LOCALITIES, LocalityFilterComponent, matchesLocality } from '../../core/locality-filter.component';

/**
 * Volunteer groups — creating them, correcting them, and standing them down.
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
 *
 * **Create and Edit are one modal, the same shape Events' own screen already
 * uses.** The create form used to sit permanently below the table, pushed
 * further down the page every time the Samaaj had one more group, and there was
 * no way to correct a typo in a name or description at all until
 * `UpdateGroupCommand` existed. Setting or changing the president stays its own
 * inline row-expansion rather than joining the modal — it is a different kind
 * of decision (who runs the group, not what the group is called) with its own
 * member-picker, the same way Events keeps its cancellation reason inline
 * beside the row rather than folding it into the modal too.
 */
@Component({
  selector: 'app-groups-list',
  imports: [LocalityFilterComponent, FormsModule, RouterLink],
  template: `
    <div class="section-head">
      <div>
        <h1 class="title">Volunteer groups</h1>
        <p class="sub">Set a group up, correct it, name its president, and stand one down.</p>
      </div>
      <button class="btn small" type="button" (click)="openCreateModal($event)">
        Set up a group
      </button>
    </div>

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

        <app-locality-filter [(value)]="localityFilter" inputId="groups-locality" />

        @if (groups().length > 0 && shownGroups().length === 0) {
          <p class="empty">Nothing here for that locality.</p>
        }

        @if (groups().length === 0) {
          <p class="empty">
            None yet. A group starts with a name and, usually, a president to run it.
          </p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Volunteer groups</caption>
              <thead>
                <tr>
                  <th>Group</th><th>Focus</th><th>Locality</th><th>President</th>
                  <th>Members</th><th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (group of shownGroups(); track group.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/groups', group.id]"><b>{{ group.name }}</b></a>
                      @if (group.description) {
                        <div class="muted">{{ group.description }}</div>
                      }
                    </td>
                    <td>{{ group.focusArea ?? '—' }}</td>
                    <td>{{ localityName(group.localityId) }}</td>
                    <td>
                      @if (group.presidentMemberId) {
                        {{ memberName(group.presidentMemberId) }}
                      } @else {
                        <span class="muted">Not set</span>
                      }
                      @if (area.administers(group.localityId)) {
                        <div>
                          <button
                            class="btn link small"
                            type="button"
                            [attr.aria-expanded]="presidentFor() === group.id"
                            (click)="openChangePresident(group)"
                          >
                            {{ group.presidentMemberId ? 'Change' : 'Set president' }}
                          </button>
                        </div>
                      }
                    </td>
                    <td>{{ group.memberCount }}</td>
                    <td>
                      <span class="pill" [class.warn]="group.status === 'Inactive'">
                        {{ group.status }}
                      </span>
                    </td>
                    <td>
                      @if (area.administers(group.localityId)) {
                        <div class="row-actions">
                          <button class="btn small alt" type="button" [disabled]="busy()"
                            (click)="openEditModal($event, group)">
                            Edit
                          </button>

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
                        </div>
                      } @else {
                        <span class="muted small">Another locality</span>
                      }
                    </td>
                  </tr>

                  @if (presidentFor() === group.id) {
                    <tr>
                      <td colspan="7">
                        <div class="notice" role="status">
                          <label [for]="group.id + '-newpres'">{{ group.presidentMemberId ? 'New president' : 'President' }}</label>
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

                          @if (group.presidentMemberId) {
                            <p class="small">
                              The outgoing president, {{ memberName(group.presidentMemberId) }},
                              stays in the group as an ordinary member rather than being removed
                              as a side effect of this change.
                            </p>
                          } @else {
                            <p class="small">
                              They join the group if they are not already in it. A "President"
                              election, run by the group itself, can also fill this.
                            </p>
                          }

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
    }

    <!-- Create / Edit modal ---------------------------------------------- -->
    @if (modalOpen()) {
      <div class="modal-backdrop" (click)="closeModal()">
        <div
          class="card modal-card modal-dialog"
          role="dialog"
          aria-modal="true"
          aria-labelledby="group-modal-title"
          (click)="$event.stopPropagation()"
          (keydown)="onModalKeydown($event)"
        >
          <h2 id="group-modal-title">
            {{ modalMode() === 'create' ? 'Set up a group' : 'Edit ' + editingGroup()?.name }}
          </h2>

          <form (ngSubmit)="submitModal()">
            <div class="form-grid">
              <div class="field">
                <label for="group-name">Name</label>
                <input id="group-name" class="input" name="name" [(ngModel)]="name"
                  maxlength="200" placeholder="Seva Group" #firstField />
              </div>

              <div class="field">
                <label for="group-description">Description</label>
                <input id="group-description" class="input" name="description"
                  [(ngModel)]="description" maxlength="1000" />
              </div>

              <div class="field">
                <label for="group-focus">Focus area</label>
                <input id="group-focus" class="input" name="focusArea" [(ngModel)]="focusArea"
                  maxlength="120" placeholder="Social Service" />
              </div>

              <!--
                An Area Admin's groups are always in their own locality, so the
                choice is shown to them but not offered.
              -->
              <div class="field">
                <label for="group-locality">Locality</label>
                <select id="group-locality" class="input" name="locality" [(ngModel)]="localityId"
                  [disabled]="area.localityId() !== null">
                  @if (area.localityId() === null) {
                    <option value="">The whole Samaaj</option>
                  }
                  @for (option of localityOptions(); track option.id) {
                    <option [value]="option.id">{{ option.label }}</option>
                  }
                </select>
              </div>

              @if (modalMode() === 'create') {
                <div class="field">
                  <label for="group-president">President</label>
                  <select id="group-president" class="input" name="president"
                    [(ngModel)]="presidentMemberId">
                    <option value="">No president yet</option>
                    @for (member of members(); track member.id) {
                      <option [value]="member.id">{{ member.fullName }}</option>
                    }
                  </select>
                </div>

                <p class="small span-all">
                  Optional. Until a group has a president, a Samaaj admin accepts its joiners and
                  can run a "President" election; choose one later with "Set president".
                </p>
              } @else {
                <p class="small span-all">
                  President:
                  {{ editingGroup()!.presidentMemberId ? memberName(editingGroup()!.presidentMemberId) : 'Not set' }}
                  — changed with "Set president" on the row, not here.
                </p>
              }
            </div>

            <div class="actions">
              <button class="btn" type="submit" [disabled]="busy() || !canSubmit()">
                {{ modalMode() === 'create' ? 'Create group' : 'Save' }}
              </button>
              <button class="btn alt" type="button" (click)="closeModal()">Cancel</button>
            </div>
          </form>
        </div>
      </div>
    }
  `,
  styles: `
    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }

    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .section-head h1 {
      margin-bottom: 0;
    }
  `,
})
export class GroupsListComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);
  readonly area = inject(AreaScope);

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLInputElement>;

  readonly groups = signal<readonly VolunteerGroup[]>([]);

  /** The Locality filter - see LocalityFilterComponent. */
  readonly localityFilter = signal(ALL_LOCALITIES);

  readonly shownGroups = computed(() =>
    this.groups().filter((group) => matchesLocality(this.localityFilter(), group.localityId)),
  );
  readonly localityOptions = signal<readonly { id: string; label: string }[]>([]);
  readonly members = signal<readonly { id: string; fullName: string }[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly modalOpen = signal(false);
  readonly modalMode = signal<'create' | 'edit'>('create');
  readonly editingGroup = signal<VolunteerGroup | null>(null);
  private modalTrigger: HTMLElement | null = null;

  name = '';
  description = '';
  focusArea = '';
  presidentMemberId = '';
  localityId = '';

  private readonly names = computed(
    () => new Map(this.members().map((m) => [m.id, m.fullName])),
  );

  private readonly localityLabels = computed(
    () => new Map(this.localityOptions().map((o) => [o.id, o.label])),
  );

  ngOnInit(): void {
    this.load();

    // Names for the Locality column and the picker. A failure leaves both
    // showing a dash, not an error: the groups themselves still load.
    forkJoin({ localities: this.api.listLocalities(), cities: this.api.listCities() }).subscribe({
      next: ({ localities, cities }) => this.localityOptions.set(labelLocalities(localities, cities)),
      error: () => this.localityOptions.set([]),
    });
  }

  localityName(localityId: string | null): string {
    return localityId === null ? 'Whole Samaaj' : (this.localityLabels().get(localityId) ?? '—');
  }

  canSubmit(): boolean {
    return this.name.trim().length > 0;
  }

  memberName(memberId: string | null): string {
    return (memberId && this.names().get(memberId)) || 'A member';
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

  // ---- Modal --------------------------------------------------------------

  openCreateModal(event: MouseEvent): void {
    this.modalMode.set('create');
    this.editingGroup.set(null);
    this.name = '';
    this.description = '';
    this.focusArea = '';
    this.presidentMemberId = '';
    this.localityId = this.area.localityId() ?? '';
    this.openModal(event);
  }

  openEditModal(event: MouseEvent, group: VolunteerGroup): void {
    this.modalMode.set('edit');
    this.editingGroup.set(group);
    this.name = group.name;
    this.description = group.description ?? '';
    this.focusArea = group.focusArea ?? '';
    this.localityId = group.localityId ?? '';
    this.openModal(event);
  }

  private openModal(event: MouseEvent): void {
    this.modalTrigger = event.currentTarget as HTMLElement;
    this.error.set(null);
    this.done.set(null);
    this.modalOpen.set(true);
    setTimeout(() => this.firstFieldRef?.nativeElement.focus());
  }

  closeModal(): void {
    if (!this.modalOpen()) {
      return;
    }

    this.modalOpen.set(false);

    const trigger = this.modalTrigger;
    this.modalTrigger = null;

    if (trigger) {
      setTimeout(() => trigger.focus());
    }
  }

  onModalKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.closeModal();

      return;
    }

    if (event.key !== 'Tab') {
      return;
    }

    const dialog = event.currentTarget as HTMLElement;
    const focusable = dialog.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
    );

    if (focusable.length === 0) {
      return;
    }

    const first = focusable[0]!;
    const last = focusable[focusable.length - 1]!;

    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  submitModal(): void {
    if (this.modalMode() === 'create') {
      this.create();
    } else {
      this.update();
    }
  }

  create(): void {
    if (!this.canSubmit()) {
      return;
    }

    const name = this.name.trim();

    this.act(
      this.api.createGroup(
        name,
        blankToNull(this.description),
        blankToNull(this.focusArea),
        this.presidentMemberId || null,
        this.localityId || null,
      ),
      this.presidentMemberId
        ? `${name} created, with ${this.memberName(this.presidentMemberId)} as president.`
        : `${name} created, with no president yet.`,
      () => this.closeModal(),
    );
  }

  update(): void {
    const editing = this.editingGroup();

    if (!editing || !this.canSubmit()) {
      return;
    }

    const name = this.name.trim();

    this.act(
      this.api.updateGroup(
        editing.id,
        name,
        blankToNull(this.description),
        blankToNull(this.focusArea),
        this.localityId || null,
      ),
      `${name} updated.`,
      () => this.closeModal(),
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
