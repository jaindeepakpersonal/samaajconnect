import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { InviteAdminResult, Role } from '../../core/admin.models';

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
 */
@Component({
  selector: 'app-invite-admin',
  imports: [ReactiveFormsModule, RouterLink, DatePipe],
  template: `
    <a class="back" routerLink="/admins">‹ Back to Admin Users &amp; Roles</a>

    <h1 class="title">Invite Admin</h1>
    <p class="sub">
      Creates an account for {{ scope.label() }} and issues a one-time code to set the first
      password.
    </p>

    @if (invited(); as result) {
      <div class="card modal-card">
        <p class="notice ok" role="status">
          <b>{{ result.fullName }}</b> has been invited to {{ scope.label() }}.
        </p>

        <h3>Their one-time code</h3>
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
    } @else if (needsSamaaj()) {
      <p class="notice">
        An administrator belongs to a Samaaj. Choose one in the top bar before inviting anybody.
      </p>
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
              This becomes their login, and it must be one nobody on the platform already uses. To
              give an existing account a role, use the Admin Users screen instead.
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

    .code {
      font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
      font-size: 28px;
      letter-spacing: 0.12em;
      background: var(--soft);
      color: var(--accent-ink);
      border-radius: var(--radius-sm);
      padding: var(--space-3) var(--space-4);
      margin: var(--space-2) 0;
      user-select: all;
      overflow-wrap: anywhere;
    }
  `,
})
export class InviteAdminComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);

  readonly scope = inject(AdminScope);

  readonly roles = signal<readonly Role[]>([]);
  readonly selected = signal<readonly string[]>([]);
  readonly invited = signal<InviteAdminResult | null>(null);
  readonly busy = signal(false);
  readonly submitted = signal(false);
  readonly error = signal<string | null>(null);

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

  spaced(name: string): string {
    return name.replace(/([a-z])([A-Z])/g, '$1 $2');
  }
}
