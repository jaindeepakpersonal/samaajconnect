import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { Role, RoleMatrix } from '../../core/admin.models';

/**
 * Role & Permission Matrix, from the admin wireframe's `#rolematrix` screen.
 *
 * The wireframe's subtitle says "Backend authorization enforces this matrix —
 * this screen edits it, not just displays it", and it has a Save Matrix button
 * and checkboxes in some cells.
 *
 * It does now. It displayed only, for reasons `ListRolesQuery` set out at
 * length: an editable matrix needed per-tenant definitions, an audit trail, and
 * a floor of permissions no edit may remove. All three exist, so the checkboxes
 * are real.
 *
 * What a tick means has not changed - it is still a fact about the running
 * system rather than a wish. The screen never decides what may be edited: the
 * response says whether this caller may edit at all, and each role says whether
 * it may be. SuperAdmin comes back not editable, and the one grant a Samaaj
 * administrator cannot lose is drawn as a tick rather than a checkbox, because
 * offering a click that always answers 409 is offering a choice that was never
 * there.
 *
 * Changes apply to the Samaaj in scope and to no other. A Super Admin who has
 * chosen no Samaaj sees the platform defaults, read-only, because an override
 * would have nowhere to go.
 */
@Component({
  selector: 'app-role-matrix',
  imports: [RouterLink],
  template: `
    <a class="back" routerLink="/admins">‹ Back to Admin Users &amp; Roles</a>

    <h1 class="title">Role &amp; Permission Matrix</h1>
    <p class="sub">What the backend actually enforces, for every role on the platform.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (matrix(); as data) {
      <p class="notice">{{ data.editableNote }}</p>

      @if (saveError(); as message) {
        <p class="notice error" role="alert">{{ message }}</p>
      }

      <div class="table-wrap">
        <table class="matrix">
          <caption class="sr-only">Which roles hold which permissions</caption>
          <thead>
            <tr>
              <th scope="col">Permission</th>
              @for (role of data.roles; track role.id) {
                <th scope="col">
                  {{ spaced(role.name) }}
                  @if (role.assignableToAdmins) {
                    <div class="small">assignable</div>
                  }
                  @if (data.editable && !role.editable) {
                    <div class="small">fixed</div>
                  }
                </th>
              }
            </tr>
          </thead>
          <tbody>
            @for (permission of data.permissions; track permission) {
              <tr>
                <th scope="row">{{ permission }}</th>
                @for (role of data.roles; track role.id) {
                  <td [class.held]="role.permissions.includes(permission)">
                    @if (canEdit(data, role, permission)) {
                      <!-- A real checkbox, with a visually-hidden label naming
                           both the role and the permission. The visible cell is
                           a tick in a grid; "checkbox" on its own tells a
                           screen-reader user nothing about which one. -->
                      <label class="sr-only" [attr.for]="cellId(role.id, permission)">
                        {{ spaced(role.name) }} — {{ permission }}
                      </label>
                      <input
                        type="checkbox"
                        [id]="cellId(role.id, permission)"
                        [checked]="role.permissions.includes(permission)"
                        [disabled]="busy() !== null"
                        (change)="toggle(role, permission)"
                      />
                    } @else if (role.permissions.includes(permission)) {
                      <span [attr.aria-label]="held(role, permission)">✔</span>
                    } @else {
                      <span class="muted" [attr.aria-label]="notHeld(role, permission)">—</span>
                    }
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>

      <p class="small">
        “Assignable” marks the roles an administrator can hand out. Super Admin is not one of
        them — its only route is the platform bootstrap, so granting it can never be one
        compromised administrator account away. Member, Family Head and Pathshala Student are not
        either: they are earned by registering, by creating a household, and by enrolling.
      </p>
    } @else if (!error()) {
      <p class="empty" role="status">Loading the matrix…</p>
    }
  `,
  styles: `
    .matrix .held {
      background: var(--ok-soft);
      color: var(--ok-ink);
      font-weight: 700;
    }

    .matrix thead th {
      vertical-align: bottom;
    }

    .matrix tbody th {
      font-weight: 600;
      font-size: 12px;
    }
  `,
})
export class RoleMatrixComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly matrix = signal<RoleMatrix | null>(null);
  readonly error = signal<string | null>(null);
  readonly saveError = signal<string | null>(null);

  /** Which cell is in flight, so the whole grid is not disabled by one save. */
  readonly busy = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.roleMatrix().subscribe({
      next: (matrix) => this.matrix.set(matrix),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  /**
   * Whether this cell is a checkbox rather than a tick.
   *
   * Three things have to hold, and the screen asks the server for all three
   * rather than deciding any of them: the caller may edit at all, this role may
   * be edited, and this is not the one grant a Samaaj administrator cannot lose.
   * The last is duplicated from the backend deliberately — it is a floor rather
   * than a rule, so a screen that let somebody click it and then reported a 409
   * would be offering a choice that was never there.
   */
  canEdit(matrix: RoleMatrix, role: Role, permission: string): boolean {
    return matrix.editable && role.editable && !this.isProtected(role, permission);
  }

  isProtected(role: Role, permission: string): boolean {
    return role.name === 'SamaajAdmin' && permission === 'Roles.Manage';
  }

  toggle(role: Role, permission: string): void {
    const granted = !role.permissions.includes(permission);
    const cell = this.cellId(role.id, permission);

    this.busy.set(cell);
    this.saveError.set(null);

    this.api.setRolePermission(role.id, permission, granted).subscribe({
      next: (matrix) => {
        // The server answers with the whole matrix, so the screen shows what is
        // actually true rather than the tick it drew optimistically.
        this.matrix.set(matrix);
        this.busy.set(null);
      },
      error: (failure: unknown) => {
        this.saveError.set(describeError(failure));
        this.busy.set(null);

        // Re-read on failure: the checkbox has already flipped itself in the
        // DOM, and leaving it showing a change the server refused is the one
        // thing this screen must never do.
        this.load();
      },
    });
  }

  cellId(roleId: string, permission: string): string {
    return `cell-${roleId}-${permission.replace(/\./g, '-')}`;
  }

  held(role: Role, permission: string): string {
    return `${this.spaced(role.name)} holds ${permission}`;
  }

  notHeld(role: Role, permission: string): string {
    return `${this.spaced(role.name)} does not hold ${permission}`;
  }

  spaced(name: string): string {
    return name.replace(/([a-z])([A-Z])/g, '$1 $2');
  }
}
