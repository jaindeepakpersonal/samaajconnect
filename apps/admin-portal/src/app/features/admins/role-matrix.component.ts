import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { RoleMatrix } from '../../core/admin.models';

/**
 * Role & Permission Matrix, from the admin wireframe's `#rolematrix` screen.
 *
 * The wireframe's subtitle says "Backend authorization enforces this matrix —
 * this screen edits it, not just displays it", and it has a Save Matrix button
 * and checkboxes in some cells.
 *
 * This screen displays it. That is not a shortcut: `GET /v1/identity/roles`
 * reports `editable: false` and explains why in `editableNote`, and the screen
 * renders what the backend says rather than deciding for itself. Every command
 * on the platform declares its required roles as a compiled-in attribute, so an
 * editable matrix would split the answer to "who may do this?" between source
 * control and a table - and the matrix is platform-wide, so a Samaaj Admin
 * editing it would be editing what a Samaaj Admin means everywhere. Making it
 * editable is a real piece of work, tracked in `DEVELOPMENT_PLAN.md`.
 *
 * A tick here is therefore a fact about the running system, checked against the
 * same catalogue the pipeline reads - which is worth more than a checkbox that
 * accepts an edit the backend ignores.
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
      @if (!data.editable) {
        <p class="notice">{{ data.editableNote }}</p>
      }

      <div class="table-wrap">
        <table class="matrix">
          <thead>
            <tr>
              <th scope="col">Permission</th>
              @for (role of data.roles; track role.id) {
                <th scope="col">
                  {{ spaced(role.name) }}
                  @if (role.assignableToAdmins) {
                    <div class="small">assignable</div>
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
                    @if (role.permissions.includes(permission)) {
                      <span aria-label="held">✔</span>
                    } @else {
                      <span class="muted" aria-label="not held">—</span>
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

  ngOnInit(): void {
    this.api.roleMatrix().subscribe({
      next: (matrix) => this.matrix.set(matrix),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  spaced(name: string): string {
    return name.replace(/([a-z])([A-Z])/g, '$1 $2');
  }
}
