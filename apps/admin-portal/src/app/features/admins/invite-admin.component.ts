import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { forkJoin } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AdminMember, InviteAdminResult, Role } from '../../core/admin.models';

/**
 * Invite Admin, from the admin wireframe's `#inviteadmin` screen.
 *
 * The wireframe says invited admins "receive a set-password link". There is no
 * delivery channel on the platform yet, so what actually happens is what
 * already happens for a converted child: a one-time code comes back here, once,
 * and is handed over in person. For a community organisation whose
 * administrators know each other that is realistic, and it involves no channel
 * that can be intercepted.
 *
 * Two wireframe controls are absent. **Tenant Scope** is not a field: the
 * invitation lands in the Samaaj the panel is currently acting on, which is in
 * the top bar and cannot be quietly different from what the request will do.
 * **Require OTP step-up for sensitive actions** is not a field either - there
 * is no OTP on the platform, so the toggle would promise a control that does
 * not exist.
 *
 * **A second mode, added 2026-09-13.** The invite form has always refused an
 * identifier already on the platform (`User.IdentifierTaken`) with a hint
 * pointing at "the Admin Users screen instead" - but that screen only ever
 * lists accounts that *already* hold an admin-assignable role
 * (`ListAdminUsersQuery`), so a member who registered themselves and holds
 * none was invisible to the one screen this form's own error message sent
 * an administrator to. This mode is the actual door: search the member
 * directory (the same `searchMembers` call the Members screen uses) and
 * grant a role directly with `AssignRoleCommand` - no invitation, no code,
 * because the account and its password already exist.
 */
@Component({
  selector: 'app-invite-admin',
  imports: [ReactiveFormsModule, FormsModule, RouterLink, DatePipe],
  template: `
    <a class="back" routerLink="/admins">‹ Back to Admin Users &amp; Roles</a>

    <h1 class="title">Invite Admin</h1>
    <p class="sub">Give somebody an admin role - a brand new person, or a member who already has an account.</p>

    <div class="mode-tabs" role="tablist">
      <button
        class="btn small"
        [class.alt]="mode() !== 'invite'"
        type="button"
        role="tab"
        [attr.aria-selected]="mode() === 'invite'"
        (click)="setMode('invite')"
      >
        Invite a new person
      </button>
      <button
        class="btn small"
        [class.alt]="mode() !== 'existing'"
        type="button"
        role="tab"
        [attr.aria-selected]="mode() === 'existing'"
        (click)="setMode('existing')"
      >
        Grant an existing member a role
      </button>
    </div>

    @if (needsSamaaj()) {
      <p class="notice">
        An administrator belongs to a Samaaj. Choose one in the top bar before continuing.
      </p>
    } @else if (mode() === 'invite') {
      @if (invited(); as result) {
        <div class="card modal-card">
          <p class="notice ok" role="status">
            <b>{{ result.fullName }}</b> has been invited to {{ scope.label() }}.
          </p>

          <h2>Their one-time code</h2>
          <p class="code">{{ result.activationCode }}</p>

          <p class="notice">
            <b>This is the only time the code is shown.</b> Only its hash is stored, so it cannot be
            looked up again — give it to {{ result.fullName }} in person or by a channel you trust.
            It expires {{ result.codeExpiresAt | date: 'd MMM y, HH:mm' }}, and five wrong attempts
            kill it. A lost code is re-issued from the Admin Users screen, which cancels this one.
          </p>

          <p class="small">
            They redeem it in the member portal to set a password, then sign in here.
            They already hold {{ result.roles.join(', ') }}.
          </p>

          <div class="actions">
            <a class="btn" routerLink="/admins">Done</a>
            <button class="btn alt" type="button" (click)="inviteAnother()">Invite another</button>
          </div>
        </div>
      } @else {
        <div class="card modal-card">
          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <label for="fullName">Full name</label>
            <input
              id="fullName"
              class="input"
              formControlName="fullName"
              [attr.aria-invalid]="showError('fullName')"
            />
            @if (showError('fullName')) {
              <p class="field-error">Enter their name.</p>
            }

            <label for="identifier">Mobile or email</label>
            <input
              id="identifier"
              class="input"
              formControlName="mobileOrEmail"
              placeholder="admin@example.com or 9876543210"
              [attr.aria-invalid]="showError('mobileOrEmail')"
            />
            @if (showError('mobileOrEmail')) {
              <p class="field-error">Enter a valid email address or Indian mobile number.</p>
            } @else {
              <p class="field-hint">
                This becomes their login, and it must be one nobody on the platform already uses.
                To give an existing member a role instead, switch to the other tab above.
              </p>
            }

            <fieldset class="roles">
              <legend>Roles</legend>
              <p class="small">
                What they will be able to do. They also become a member of this Samaaj, as everyone
                with a login is.
              </p>

              @if (assignableRoles().length === 0) {
                <p class="muted">Loading roles…</p>
              }

              @for (role of assignableRoles(); track role.id) {
                <div class="toggle-row">
                  <label [for]="'role-' + role.name">
                    {{ spaced(role.name) }}
                    <span class="muted">{{ role.permissions.length }} permissions</span>
                  </label>
                  <input
                    type="checkbox"
                    [id]="'role-' + role.name"
                    [checked]="selected().includes(role.name)"
                    (change)="toggle(role.name)"
                  />
                </div>
              }

              @if (submitted() && selected().length === 0) {
                <p class="field-error">Choose at least one role.</p>
              }
            </fieldset>

            @if (error(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <div class="actions">
              <button class="btn" type="submit" [disabled]="busy()">
                {{ busy() ? 'Inviting…' : 'Send invite' }}
              </button>
              <a class="btn alt" routerLink="/admins">Cancel</a>
            </div>

            <p class="sr-only" role="status">{{ busy() ? 'Creating the invitation' : '' }}</p>
          </form>
        </div>
      }
    } @else if (granted(); as result) {
      <div class="card modal-card">
        <p class="notice ok" role="status">
          <b>{{ result.fullName }}</b> now holds {{ result.roles.join(', ') }}.
        </p>

        <p class="small">
          Nothing else changed for their account - they already have a password, and sign in the
          same way they always have.
        </p>

        <div class="actions">
          <a class="btn" routerLink="/admins">Done</a>
          <button class="btn alt" type="button" (click)="grantAnother()">Grant another</button>
        </div>
      </div>
    } @else {
      <div class="card modal-card">
        <h2>Find the member</h2>

        <form (ngSubmit)="search()">
          <label for="member-search-term">Name</label>
          <div class="search-row">
            <input
              class="input"
              id="member-search-term"
              name="memberSearchTerm"
              [(ngModel)]="searchTerm"
              placeholder="Part of a name"
            />
            <button class="btn" type="submit" [disabled]="searching()">Search</button>
          </div>
        </form>

        @if (selectedMember(); as member) {
          <p class="small">
            Granting a role to <strong>{{ member.fullName }}</strong>.
            <button type="button" class="btn link" (click)="clearSelection()">Change</button>
          </p>
        } @else {
          @if (searching()) {
            <p class="small" role="status">Searching…</p>
          } @else if (searched() && searchResults().length === 0) {
            <p class="small">No member found by that name.</p>
          } @else if (searchResults().length > 0) {
            <ul class="suggestions" role="listbox" aria-label="Matching members">
              @for (member of searchResults(); track member.id) {
                <li role="option">
                  <button type="button" class="btn link" (click)="selectMember(member)">
                    {{ member.fullName }}
                  </button>
                </li>
              }
            </ul>
          }
        }

        @if (selectedMember()) {
          <fieldset class="roles">
            <legend>Roles</legend>
            <p class="small">What this member will additionally be able to do.</p>

            @if (assignableRoles().length === 0) {
              <p class="muted">Loading roles…</p>
            }

            @for (role of assignableRoles(); track role.id) {
              <div class="toggle-row">
                <label [for]="'existing-role-' + role.name">
                  {{ spaced(role.name) }}
                  <span class="muted">{{ role.permissions.length }} permissions</span>
                </label>
                <input
                  type="checkbox"
                  [id]="'existing-role-' + role.name"
                  [checked]="selected().includes(role.name)"
                  (change)="toggle(role.name)"
                />
              </div>
            }

            @if (grantSubmitted() && selected().length === 0) {
              <p class="field-error">Choose at least one role.</p>
            }
          </fieldset>

          @if (error(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          <div class="actions">
            <button class="btn" type="button" [disabled]="busy()" (click)="grant()">
              {{ busy() ? 'Granting…' : 'Grant role(s)' }}
            </button>
            <a class="btn alt" routerLink="/admins">Cancel</a>
          </div>
        }
      </div>
    }
  `,
  styles: `
    .roles {
      border: 1px solid var(--line);
      border-radius: var(--radius-sm);
      padding: var(--space-3) var(--space-4);
      margin: var(--space-4) 0 0;
    }

    .roles legend {
      font-size: 14px;
      font-weight: 600;
      padding: 0 var(--space-2);
    }

    .roles .toggle-row label .muted {
      display: block;
      font-weight: 400;
    }

    .mode-tabs {
      display: flex;
      gap: var(--space-2);
      margin: var(--space-3) 0 var(--space-4);
    }

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
      max-height: 220px;
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
export class InviteAdminComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);

  readonly scope = inject(AdminScope);

  readonly mode = signal<'invite' | 'existing'>('invite');

  readonly roles = signal<readonly Role[]>([]);
  readonly selected = signal<readonly string[]>([]);
  readonly invited = signal<InviteAdminResult | null>(null);
  readonly busy = signal(false);
  readonly submitted = signal(false);
  readonly error = signal<string | null>(null);

  // ---- Existing-member mode -----------------------------------------------
  searchTerm = '';
  readonly searching = signal(false);
  readonly searched = signal(false);
  readonly searchResults = signal<readonly AdminMember[]>([]);
  readonly selectedMember = signal<AdminMember | null>(null);
  readonly grantSubmitted = signal(false);
  readonly granted = signal<{ fullName: string; roles: readonly string[] } | null>(null);

  readonly assignableRoles = computed(() => this.roles().filter((r) => r.assignableToAdmins));

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  readonly form = this.formBuilder.nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(200)]],

    // The same rule identity-tenant-service applies: an email address, or an
    // Indian mobile number with an optional +91. Kept in step deliberately -
    // a value this form accepts and the service rejects is a wasted round trip,
    // and one it rejects that the service would accept is a login nobody can
    // create here.
    mobileOrEmail: [
      '',
      [Validators.required, Validators.pattern(/^([^@\s]+@[^@\s]+\.[^@\s]+|(\+91)?[6-9]\d{9})$/)],
    ],
  });

  ngOnInit(): void {
    this.api.roleMatrix().subscribe({
      next: (matrix) => this.roles.set(matrix.roles),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  setMode(mode: 'invite' | 'existing'): void {
    this.mode.set(mode);
    this.error.set(null);
    this.selected.set([]);
  }

  showError(control: 'fullName' | 'mobileOrEmail'): boolean {
    const field = this.form.controls[control];

    return field.invalid && (field.dirty || field.touched || this.submitted());
  }

  toggle(role: string): void {
    const current = this.selected();

    this.selected.set(
      current.includes(role) ? current.filter((r) => r !== role) : [...current, role],
    );
  }

  submit(): void {
    this.submitted.set(true);

    if (this.form.invalid || this.selected().length === 0) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    const raw = this.form.getRawValue();

    this.api
      .inviteAdmin({
        fullName: raw.fullName.trim(),
        mobileOrEmail: raw.mobileOrEmail.trim(),
        roles: this.selected(),
      })
      .subscribe({
        next: (result) => {
          this.busy.set(false);
          this.invited.set(result);
        },
        error: (failure: unknown) => {
          this.busy.set(false);
          this.error.set(describeError(failure));
        },
      });
  }

  inviteAnother(): void {
    // The previous code is gone from the screen for good, which is the point:
    // leaving it visible while a second invitation is typed is how one gets
    // handed to the wrong person.
    this.invited.set(null);
    this.submitted.set(false);
    this.selected.set([]);
    this.form.reset();
  }

  // ---- Existing-member mode -----------------------------------------------

  search(): void {
    const term = this.searchTerm.trim();

    if (term.length === 0) {
      return;
    }

    this.searching.set(true);
    this.searched.set(false);
    this.error.set(null);

    this.api.searchMembers(term, null).subscribe({
      next: (found) => {
        this.searchResults.set(found.items);
        this.searching.set(false);
        this.searched.set(true);
      },
      error: (failure: unknown) => {
        this.searching.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  selectMember(member: AdminMember): void {
    this.selectedMember.set(member);
    this.searchResults.set([]);
  }

  clearSelection(): void {
    this.selectedMember.set(null);
    this.selected.set([]);
    this.grantSubmitted.set(false);
    this.searched.set(false);
  }

  /**
   * Grants every checked role to the selected member, one call per role -
   * the same shape `AdminListComponent`'s own per-row checkboxes use,
   * `AssignRoleCommand` was never built to take more than one at a time.
   */
  grant(): void {
    this.grantSubmitted.set(true);

    const member = this.selectedMember();
    const roles = this.selected();

    if (!member || roles.length === 0) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    forkJoin(roles.map((role) => this.api.setRole(member.id, role, true))).subscribe({
      next: (results) => {
        this.busy.set(false);
        this.granted.set({
          fullName: member.fullName,
          roles: results[results.length - 1]?.roles ?? roles,
        });
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  grantAnother(): void {
    this.granted.set(null);
    this.grantSubmitted.set(false);
    this.selected.set([]);
    this.selectedMember.set(null);
    this.searchTerm = '';
    this.searchResults.set([]);
    this.searched.set(false);
  }

  spaced(name: string): string {
    return name.replace(/([a-z])([A-Z])/g, '$1 $2');
  }
}
