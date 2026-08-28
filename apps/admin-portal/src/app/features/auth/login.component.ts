import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';

const ADMIN_ROLES = [
  'SuperAdmin',
  'SamaajAdmin',
  'ContentModerator',
  'VolunteerGroupPresident',
  'PathshalaTeacher',
  'BoliManager',
] as const;

/**
 * Sign in to the admin panel.
 *
 * The admin wireframe has no login screen - it opens straight onto the
 * dashboard - so this follows member-portal's `#login` for layout and copy
 * instead of inventing a new one. There is one login endpoint on the platform;
 * this is the same call the member portal makes, against the same accounts.
 *
 * A member with no administrative role is signed out again rather than shown an
 * empty panel. That is a courtesy, not a control: every screen behind this is
 * gated server-side, and a member who got in would simply be refused by each
 * endpoint in turn.
 */
@Component({
  selector: 'app-admin-login',
  imports: [ReactiveFormsModule],
  styleUrl: './login.css',
  template: `
    <div class="login-wrap">
      <div class="brand">samaajconnect<br /><span>Unified Admin</span></div>

      <div class="card">
        <h1 class="login-heading">Sign in</h1>
        <p class="sub">Administrator access to the platform.</p>

        @if (sessionExpired()) {
          <p class="notice" role="status">Your session ended. Please sign in again.</p>
        }

        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <label for="identifier">Mobile / Email</label>
          <input
            id="identifier"
            class="input"
            formControlName="mobileOrEmail"
            autocomplete="username"
            placeholder="admin@example.com"
            [attr.aria-invalid]="showError('mobileOrEmail')"
          />
          @if (showError('mobileOrEmail')) {
            <p class="field-error">Enter your mobile number or email.</p>
          }

          <label for="password">Password</label>
          <input
            id="password"
            class="input"
            type="password"
            formControlName="password"
            autocomplete="current-password"
            placeholder="••••••••"
            [attr.aria-invalid]="showError('password')"
          />
          @if (showError('password')) {
            <p class="field-error">Enter your password.</p>
          }

          @if (error(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          <div class="actions">
            <button class="btn" type="submit" [disabled]="busy()">
              {{ busy() ? 'Signing in…' : 'Sign in' }}
            </button>
          </div>

          <p class="sr-only" role="status">{{ busy() ? 'Signing in' : '' }}</p>
        </form>

        <p class="small login-footer">
          Invited administrators set their first password by redeeming the one-time code their
          Samaaj administrator gave them, in the member portal.
        </p>
      </div>
    </div>
  `,
})
export class AdminLoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly sessionExpired = signal(this.route.snapshot.queryParamMap.get('expired') === 'true');

  readonly form = this.formBuilder.nonNullable.group({
    mobileOrEmail: ['', Validators.required],
    password: ['', Validators.required],
  });

  showError(control: 'mobileOrEmail' | 'password'): boolean {
    const field = this.form.controls[control];

    return field.invalid && (field.dirty || field.touched);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    const { mobileOrEmail, password } = this.form.getRawValue();

    this.auth.login(mobileOrEmail, password).subscribe({
      next: (result) => {
        this.busy.set(false);

        if (!result.roles.some((role) => ADMIN_ROLES.includes(role as never))) {
          this.auth.signOut().subscribe();
          this.error.set(
            'That account does not administer anything on this platform. ' +
              'Members sign in through the member portal.',
          );
          return;
        }

        void this.router.navigateByUrl(
          this.route.snapshot.queryParamMap.get('returnUrl') ?? '/dashboard',
        );
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(describeError(failure));
      },
    });
  }
}
