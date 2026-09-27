import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { Observable } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import { TrustRole } from '../../core/admin.models';

/**
 * A Trust's roles - President, Secretary, Treasurer, or whatever it calls
 * them - each with the seats an election for it fills. Copied from Volunteer
 * Groups' own "Volunteer roles" card rather than shared.
 *
 * **Trustee is built in and offered nothing.** Every trustee holds it until
 * they hold another role; it has no seat limit and nobody is elected to it.
 *
 * **What the service refuses is not drawn as a choice.** Remove is disabled
 * while anybody holds the role; resizing below the holders is refused with a
 * sentence rather than a silent 409.
 */
@Component({
  selector: 'app-trust-roles',
  imports: [FormsModule],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    <div class="card">
      <h2>Roles</h2>
      <p class="small">
        Every trustee holds one role. Everybody starts as a Trustee; each other role is filled by an
        election for as many seats as it has, or by moving somebody into it on the Members tab.
      </p>

      @if (loading()) {
        <p class="empty" role="status">Loading…</p>
      } @else {
        <div class="table-wrap">
          <table>
            <caption class="sr-only">This Trust's roles</caption>
            <thead>
              <tr><th>Role</th><th>Seats</th><th>Held by</th><th></th></tr>
            </thead>
            <tbody>
              @for (role of roles(); track role.id) {
                <tr>
                  @if (editingId() === role.id) {
                    <td>
                      <label class="sr-only" [for]="'trust-role-title-' + role.id">Title</label>
                      <input [id]="'trust-role-title-' + role.id" class="input" name="editTitle"
                        [(ngModel)]="editTitle" maxlength="100" />
                    </td>
                    <td>
                      <label class="sr-only" [for]="'trust-role-seats-' + role.id">Seats</label>
                      <input [id]="'trust-role-seats-' + role.id" class="input seats" type="number" min="1"
                        max="50" name="editSeats" [(ngModel)]="editSeats"
                        [attr.aria-invalid]="editSeats < role.holders ? 'true' : null" />
                    </td>
                    <td>{{ role.holders }}</td>
                    <td>
                      <div class="row-actions">
                        <button class="btn small" type="button" (click)="save(role)"
                          [disabled]="busy() || !valid(editTitle, editSeats) || editSeats < role.holders">
                          Save
                        </button>
                        <button class="btn small alt" type="button" (click)="editingId.set(null)">Cancel</button>
                      </div>
                      @if (editSeats < role.holders) {
                        <p class="small" role="alert">
                          {{ role.holders }} hold it now - move some to another role first.
                        </p>
                      }
                    </td>
                  } @else {
                    <td>
                      <b>{{ role.title }}</b>
                      @if (!role.isElected) {
                        <span class="small"> · everybody's until they hold another</span>
                      }
                    </td>
                    <td>{{ role.seats ?? 'No limit' }}</td>
                    <td>{{ role.holders }}</td>
                    <td>
                      @if (role.isElected) {
                        <div class="row-actions">
                          <button class="btn small alt" type="button" [disabled]="busy()" (click)="startEdit(role)">
                            Edit<span class="sr-only"> {{ role.title }}</span>
                          </button>
                          <button class="btn small alt" type="button" [disabled]="busy() || role.holders > 0"
                            [title]="role.holders > 0 ? 'Move its holders to another role first' : ''"
                            (click)="remove(role)">
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

        <form class="add-role" (ngSubmit)="define()">
          <div>
            <label for="new-trust-role-title">New role</label>
            <input id="new-trust-role-title" class="input" name="newTitle" [(ngModel)]="newTitle"
              maxlength="100" placeholder="Secretary" />
          </div>
          <div>
            <label for="new-trust-role-seats">Seats</label>
            <input id="new-trust-role-seats" class="input seats" type="number" min="1" max="50"
              name="newSeats" [(ngModel)]="newSeats" />
          </div>
          <button class="btn" type="submit" [disabled]="busy() || !valid(newTitle, newSeats)">Add role</button>
        </form>
      }
    </div>
  `,
  styles: `
    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }

    .seats {
      max-width: 90px;
    }

    .add-role {
      display: flex;
      gap: var(--space-3);
      align-items: flex-end;
      flex-wrap: wrap;
      margin-top: var(--space-4);
    }
  `,
})
export class TrustRolesComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly trustId = input.required<string>();

  readonly roles = signal<readonly TrustRole[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly editingId = signal<string | null>(null);

  newTitle = '';
  newSeats = 1;
  editTitle = '';
  editSeats = 1;

  ngOnInit(): void {
    this.load();
  }

  /** The service's rule, character for character: a title, and 1 to 50 seats. */
  valid(title: string, seats: number): boolean {
    return title.trim().length > 0 && Number.isInteger(seats) && seats >= 1 && seats <= 50;
  }

  private load(): void {
    this.api.listTrustRoles(this.trustId()).subscribe({
      next: (found) => {
        this.roles.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  startEdit(role: TrustRole): void {
    this.editTitle = role.title;
    this.editSeats = role.seats ?? 1;
    this.editingId.set(role.id);
  }

  define(): void {
    if (!this.valid(this.newTitle, this.newSeats)) {
      return;
    }

    const title = this.newTitle.trim();

    this.act(this.api.defineTrustRole(this.trustId(), title, this.newSeats), `${title} is a role on this Trust.`, () => {
      this.newTitle = '';
      this.newSeats = 1;
    });
  }

  save(role: TrustRole): void {
    const title = this.editTitle.trim();

    this.act(this.api.updateTrustRole(this.trustId(), role.id, title, this.editSeats), `${title} is saved.`, () =>
      this.editingId.set(null),
    );
  }

  remove(role: TrustRole): void {
    this.act(this.api.removeTrustRole(this.trustId(), role.id), `${role.title} is no longer a role.`);
  }

  private act(work: Observable<unknown>, message: string, after?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        after?.();
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
