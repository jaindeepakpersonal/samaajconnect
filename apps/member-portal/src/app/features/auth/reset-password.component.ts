import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';

/**
 * Reset password, step two - the wireframe's `#otp` screen, redeeming into a
 * new password rather than a session.
 *
 * The identifier arrives from `/forgot` as a query parameter, pre-filled but
 * editable: someone who mistyped it on the previous screen should not have to
 * go back and resend.
 *
 * **Redeeming does not sign anybody in**, the same choice `/activate` makes
 * for the same reason: proving you hold a contact address a code was sent to
 * is weaker than a real password, so the next step is signing in normally.
 *
 * **Every way this can fail says the same thing.** Distinguishing "no such
 * account" from "wrong code" from "expired" would let somebody holding a list
 * of identifiers work out which ones exist.
 */
@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink],
  styleUrl: './auth.css',
  template: `
    <div class="auth-wrap">
      <div class="logo">samaajconnect</div>

      <div class="auth-card">
        <h1 class="auth-heading">Enter verification code</h1>
        <p class="subtitle">Valid for 10 minutes.</p>

        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <label for="identifier">Mobile / Email</label>
          <input
            id="identifier"
            class="input"
            formControlName="mobileOrEmail"
            autocomplete="username"
            placeholder="member@example.com"
            [attr.aria-invalid]="showError('mobileOrEmail')"
          />
          @if (showError('mobileOrEmail')) {
            <p class="field-error">Enter your mobile number or email.</p>
          }

          <label for="code">OTP</label>
          <input
            id="code"
            class="input"
            formControlName="code"
            inputmode="numeric"
            autocomplete="one-time-code"
            placeholder="6-digit code"
            [attr.aria-invalid]="showError('code')"
          />
          @if (showError('code')) {
            <p class="field-error">Enter the code that was sent.</p>
          }

          <label for="new-password">New Password</label>
          <input
            id="new-password"
            class="input"
            type="password"
            formControlName="newPassword"
            autocomplete="new-password"
            placeholder="••••••••"
            [attr.aria-invalid]="showNewPasswordError()"
          />
          @if (showNewPasswordError()) {
            <p class="field-error">Choose a password of at least 10 characters.</p>
          }

          <label for="confirm-password">Confirm new password</label>
          <input
            id="confirm-password"
            class="input"
            type="password"
            formControlName="confirmPassword"
            autocomplete="new-password"
            [attr.aria-invalid]="showConfirmError()"
          />
          @if (showConfirmError()) {
            <p class="field-error">The passwords do not match.</p>
          }

          @if (error(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          <div class="actions">
            <button class="btn" type="submit" [disabled]="busy()">
              {{ busy() ? 'Resetting…' : 'Reset & Continue' }}
            </button>
            <a class="btn secondary" routerLink="/login">Back to sign in</a>
          </div>

          <p class="sr-only" role="status">{{ busy() ? 'Resetting your password' : '' }}</p>
        </form>

        <p class="small auth-footer">
          <a routerLink="/forgot">Resend code</a>
        </p>
      </div>
    </div>
  `,
})
export class ResetPasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  private readonly attempted = signal(false);

  readonly form = this.formBuilder.nonNullable.group({
    mobileOrEmail: [
      this.route.snapshot.queryParamMap.get('identifier') ?? '',
      Validators.required,
    ],
    code: ['', Validators.required],
    // The same floor every other password field on this platform applies.
    newPassword: ['', [Validators.required, Validators.minLength(10)]],
    confirmPassword: ['', Validators.required],
  });

  /**
   * A plain method, not a `computed` - reactive form control values are not
   * signals, so a `computed` here would cache against `attempted()` alone
   * and never see a keystroke.
   */
  showNewPasswordError(): boolean {
    return this.attempted() && this.form.controls.newPassword.invalid;
  }

  showConfirmError(): boolean {
    return (
      this.attempted()
      && this.form.controls.confirmPassword.value !== this.form.controls.newPassword.value
    );
  }

  showError(control: 'mobileOrEmail' | 'code'): boolean {
    const field = this.form.controls[control];

    return field.invalid && (field.dirty || field.touched);
  }

  submit(): void {
    this.attempted.set(true);

    if (this.form.invalid || this.showConfirmError()) {
      this.form.markAllAsTouched();
      return;
    }

    const { mobileOrEmail, code, newPassword } = this.form.getRawValue();

    this.busy.set(true);
    this.error.set(null);

    this.auth.redeemPasswordReset(mobileOrEmail.trim(), code.trim(), newPassword).subscribe({
      next: () => {
        this.busy.set(false);

        // To sign in, not to Home: there is no token from this call.
        void this.router.navigate(['/login'], { queryParams: { reset: 'true' } });
      },
      error: (failure) => {
        this.busy.set(false);
        this.error.set(describeError(failure));
      },
    });
  }
}
