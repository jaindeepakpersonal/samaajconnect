import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { ActivationCode, AdminUser, Role } from '../../core/admin.models';

/**
 * Admin Users & Roles, from the admin wireframe's `#admins` screen.
 *
 * The wireframe's **Tenant Scope** column is gone, because on this screen it
 * would say the same thing on every row: the list is the current Samaaj's
 * administrators and nobody else's. Which Samaaj that is, is in the top bar.
 *
 * Roles are checkboxes rather than an Edit button opening a form. Each one is a
 * single call that the backend either accepts or refuses, so a checkbox is an
 * honest picture of what is happening; a form implies a batch that can be
 * cancelled, and there is no batch.
 */
@Component({
  selector: 'app-admin-list',
  imports: [RouterLink, DatePipe],
  template: `
    <h1 class="title">Admin Users &amp; Roles</h1>
    <p class="sub">Role and tenant-scoped access management for {{ samaajLabel() }}.</p>

    <div class="actions">
      <a class="btn" routerLink="/admins/invite">+ Invite Admin</a>
      <a class="btn alt" routerLink="/roles">Role &amp; Permission Matrix</a>
    </div>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (needsSamaaj()) {
      <p class="notice">
        Administrators belong to a Samaaj. Choose one in the top bar to see and manage its
        administrators.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading administrators…</p>
    } @else if (admins().length === 0) {
      <p class="empty">
        This Samaaj has no administrators yet.
        <a routerLink="/admins/invite">Invite one</a>.
      </p>
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">Administrators, their roles and when they last signed in</caption>
          <thead>
            <tr>
              <th>Admin</th>
              <th>Status</th>
              <th>Last signed in</th>
              @for (role of assignableRoles(); track role.id) {
                <th class="role-col">{{ spaced(role.name) }}</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (admin of admins(); track admin.userId) {
              <tr>
                <td>
                  <b>{{ admin.fullName }}</b>
                  <div class="muted">{{ admin.mobileOrEmail }}</div>
                </td>
                <td>
                  <span
                    class="pill"
                    [class.ok]="admin.status === 'Active'"
                    [class.warn]="admin.status === 'PendingActivation'"
                    [class.off]="admin.status !== 'Active' && admin.status !== 'PendingActivation'"
                    >{{ spaced(admin.status) }}</span
                  >

                  <!--
                    The Invite screen has always told administrators that "a
                    lost code is re-issued from the Admin Users screen, which
                    cancels this one". It was not: the endpoint and the client
                    method both existed and no screen called either, so an
                    account stuck at Pending Activation stayed stuck and the
                    dashboard counted it every day.
                  -->
                  @if (admin.status === 'PendingActivation') {
                    <div class="actions">
                      <button
                        class="btn alt small"
                        type="button"
                        [disabled]="reissuing() === admin.userId"
                        (click)="reissue(admin)"
                      >
                        {{ reissuing() === admin.userId ? 'Issuing…' : 'Re-issue code' }}
                        <span class="sr-only">for {{ admin.fullName }}</span>
                      </button>
                    </div>
                  }

                  @if (issued(); as code) {
                    @if (code.userId === admin.userId) {
                      <div class="notice" role="status">
                        <p class="code">{{ code.code }}</p>
                        <p class="small">
                          <b>Shown once.</b> Only its hash is stored, so it cannot be looked
                          up again — hand it to {{ code.fullName }} in person. It expires
                          {{ code.expiresAt | date: 'd MMM y, HH:mm' }}, and issuing this one
                          cancelled any earlier code.
                        </p>
                        <button class="btn alt small" type="button" (click)="issued.set(null)">
                          Done
                        </button>
                      </div>
                    }
                  }
                </td>
                <td>
                  @if (admin.lastLoginAt) {
                    {{ admin.lastLoginAt | date: 'd MMM y, HH:mm' }}
                  } @else {
                    <span class="muted">Never</span>
                  }
                </td>
                @for (role of assignableRoles(); track role.id) {
                  <td class="role-col">
                    <label class="sr-only" [for]="admin.userId + role.name">
                      {{ role.name }} for {{ admin.fullName }}
                    </label>
                    <input
                      type="checkbox"
                      [id]="admin.userId + role.name"
                      [checked]="admin.roles.includes(role.name)"
                      [disabled]="busy() !== null"
                      (change)="setRole(admin, role.name, $event)"
                    />
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>

      <p class="small">
        Every member also holds the Member role; it is not shown because it is not something an
        administrator grants. Super Admin is not here either — it is a platform account, not a
        Samaaj one, and nothing on this screen can hand it out.
      </p>

      <p class="sr-only" role="status">{{ busy() ? 'Saving the role change' : '' }}</p>
    }
  `,
  styles: `
    .role-col {
      text-align: center;
      white-space: nowrap;
    }
  `,
})
export class AdminListComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly scope = inject(AdminScope);

  readonly admins = signal<readonly AdminUser[]>([]);
  readonly roles = signal<readonly Role[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal<string | null>(null);

  /** Which account a code is being minted for, and the code once it arrives. */
  readonly reissuing = signal<string | null>(null);
  readonly issued = signal<ActivationCode | null>(null);

  /**
   * Mints a fresh one-time code for an account still waiting to be activated.
   *
   * Issuing cancels any earlier code, which is why the panel says so: an
   * administrator who hands out a second code without knowing that would leave
   * somebody holding one that has silently stopped working.
   *
   * The previous code is cleared before the request rather than after it, so a
   * failure cannot leave the last person's code sitting on screen next to a new
   * name — the same rule the invite screen follows.
   */
  reissue(admin: AdminUser): void {
    this.reissuing.set(admin.userId);
    this.issued.set(null);
    this.error.set(null);

    this.api.issueActivationCode(admin.userId).subscribe({
      next: (code) => {
        this.issued.set(code);
        this.reissuing.set(null);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.reissuing.set(null);
      },
    });
  }

  readonly assignableRoles = computed(() => this.roles().filter((r) => r.assignableToAdmins));

  /**
   * A Super Admin's token names no Samaaj, so without a selection this screen
   * would ask for "the current Samaaj's administrators" and get nothing back.
   * Saying so beats an empty table.
   */
  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  readonly samaajLabel = computed(() => this.scope.label());

  ngOnInit(): void {
    this.api.roleMatrix().subscribe({
      next: (matrix) => this.roles.set(matrix.roles),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });

    if (this.needsSamaaj()) {
      this.loading.set(false);
      return;
    }

    this.load();
  }

  load(): void {
    this.loading.set(true);

    this.api.listAdmins().subscribe({
      next: (admins) => {
        this.admins.set(admins);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  setRole(admin: AdminUser, role: string, event: Event): void {
    const input = event.target as HTMLInputElement;
    const granted = input.checked;

    this.busy.set(admin.userId + role);
    this.error.set(null);

    this.api.setRole(admin.userId, role, granted).subscribe({
      next: (result) => {
        this.admins.set(
          this.admins().map((a) =>
            a.userId === admin.userId ? { ...a, roles: result.roles } : a,
          ),
        );
        this.busy.set(null);
      },
      error: (failure: unknown) => {
        // The server refused, so the checkbox has to go back to what is true.
        // Leaving it where the click put it would show an authority the
        // account does not have.
        input.checked = !granted;
        this.error.set(describeError(failure));
        this.busy.set(null);
      },
    });
  }

  /** `SamaajAdmin` reads as "Samaaj Admin" to a person. */
  spaced(name: string): string {
    return name.replace(/([a-z])([A-Z])/g, '$1 $2');
  }
}
