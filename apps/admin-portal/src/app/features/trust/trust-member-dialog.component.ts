import { AfterViewInit, Component, ElementRef, ViewChild, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminMember, TrustRole } from '../../core/admin.models';
import { modalKeydown } from '../../core/modal-keys';

/**
 * Adding a trustee, as a modal over the Members tab - the dialog shape Events
 * and the Trust's own set-up dialog use.
 *
 * **Somebody is found by searching the directory**, the call Invite Admin's
 * "existing member" mode makes, because the whole Samaaj is not on screen to
 * pick from the way a small group's roster is.
 *
 * **The role picker offers only roles with a free seat.** trust-service refuses
 * a full one with 409; a choice that always fails is not a choice. Trustee has
 * no limit and is the default.
 */
@Component({
  selector: 'app-trust-member-dialog',
  imports: [FormsModule],
  template: `
    <div class="modal-backdrop" (click)="closed.emit()">
      <div
        class="card modal-card modal-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="trust-member-dialog-title"
        (click)="$event.stopPropagation()"
        (keydown)="onKeydown($event)"
      >
        <h2 id="trust-member-dialog-title">Add a trustee</h2>

        @if (error(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        @if (selected(); as picked) {
          <p>
            Adding <b>{{ picked.fullName }}</b>.
            <button type="button" class="btn link" (click)="clearSelection()">Change</button>
          </p>

          <form (ngSubmit)="add(picked)">
            <label for="new-trustee-role">Role</label>
            <select id="new-trustee-role" class="input" name="roleId" [(ngModel)]="roleId">
              @for (role of openRoles(); track role.id) {
                <option [value]="role.id">
                  {{ role.title }}{{ role.seats === null ? '' : ' (' + (role.seats - role.holders) + ' of ' + role.seats + ' seats free)' }}
                </option>
              }
            </select>

            <div class="actions">
              <button class="btn" type="submit" [disabled]="busy()">Add to the roster</button>
              <button class="btn alt" type="button" (click)="closed.emit()">Cancel</button>
            </div>
          </form>
        } @else {
          <form (ngSubmit)="search()">
            <label for="trustee-search">Name or mobile</label>
            <div class="search-row">
              <input id="trustee-search" class="input" name="term" [(ngModel)]="term"
                placeholder="Part of a name" #firstField />
              <button class="btn" type="submit" [disabled]="searching() || !term.trim()">Search</button>
            </div>
          </form>

          @if (searching()) {
            <p class="small" role="status">Searching…</p>
          } @else if (searched() && results().length === 0) {
            <p class="small">No member found by that name.</p>
          } @else if (results().length > 0) {
            <ul class="suggestions" aria-label="Matching members">
              @for (candidate of results(); track candidate.id) {
                <li>
                  <button type="button" class="btn link" (click)="select(candidate)">
                    {{ candidate.fullName }}
                    @if (onRoster().has(candidate.id)) {
                      <span class="small"> · already on the roster</span>
                    }
                  </button>
                </li>
              }
            </ul>
          }

          <div class="actions">
            <button class="btn alt" type="button" (click)="closed.emit()">Cancel</button>
          </div>
        }
      </div>
    </div>
  `,
  styles: `
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
      max-height: 260px;
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
  `,
})
export class TrustMemberDialogComponent implements AfterViewInit {
  private readonly api = inject(AdminApi);

  readonly trustId = input.required<string>();
  readonly roles = input.required<readonly TrustRole[]>();

  /** Member ids already active on the roster - adding one again changes nothing, and the list says so. */
  readonly onRoster = input<ReadonlySet<string>>(new Set());

  /** Emitted with a sentence saying what happened. */
  readonly saved = output<string>();
  readonly closed = output<void>();

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly searching = signal(false);
  readonly searched = signal(false);
  readonly results = signal<readonly AdminMember[]>([]);
  readonly selected = signal<AdminMember | null>(null);

  readonly openRoles = computed(() =>
    this.roles().filter((role) => role.seats === null || role.holders < role.seats),
  );

  term = '';
  roleId = '';

  @ViewChild('firstField') private readonly firstFieldRef?: ElementRef<HTMLInputElement>;

  ngAfterViewInit(): void {
    setTimeout(() => this.firstFieldRef?.nativeElement.focus());
  }

  onKeydown(event: KeyboardEvent): void {
    modalKeydown(event, () => this.closed.emit());
  }

  search(): void {
    const term = this.term.trim();

    if (term.length === 0) {
      return;
    }

    this.searching.set(true);
    this.searched.set(false);
    this.error.set(null);

    this.api.searchMembers(term, null).subscribe({
      next: (found) => {
        this.results.set(found.items);
        this.searching.set(false);
        this.searched.set(true);
      },
      error: (failure: unknown) => {
        this.searching.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  select(member: AdminMember): void {
    this.selected.set(member);
    this.roleId = this.roles().find((role) => !role.isElected)?.id ?? '';
  }

  clearSelection(): void {
    this.selected.set(null);
  }

  add(member: AdminMember): void {
    if (this.busy()) {
      return;
    }

    const role = this.roles().find((r) => r.id === this.roleId);

    this.busy.set(true);
    this.error.set(null);

    this.api.addTrustMember(this.trustId(), member.id, role?.isElected ? role.id : null).subscribe({
      next: () => {
        this.busy.set(false);
        this.saved.emit(`${member.fullName} is on the roster as ${role?.title ?? 'Trustee'}.`);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
