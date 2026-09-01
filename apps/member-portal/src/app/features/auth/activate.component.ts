import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';

/**
 * Redeeming a one-time activation code, and setting a first password.
 *
 * **Three screens in the admin panel told people to do this here, and there was
 * nowhere to do it.** The invite screen says "They redeem it in the member
 * portal to set a password"; the admin sign-in screen says the same; the
 * conversion queue says an account appears "once the code has been redeemed".
 * `POST /v1/identity/activations/redeem` existed and was covered by the smoke
 * script, and no screen in either app called it — so no invited administrator
 * could ever sign in, and no adult child converted from a family record could
 * ever get an account.
 *
 * No wireframe covers this. The prototype has `#forgot` and `#otp` and neither
 * is this, so the screen is designed against the flow rather than translated —
 * the same position `/privacy` was in.
 *
 * **Redeeming does not sign anybody in.** The service answers with who the
 * account belongs to and no token, deliberately: the first ordinary login is
 * also the first proof the new password works. So this hands over to `/login`
 * with a notice, the same way registration does.
 *
 * **Every way this can fail says the same thing**, because the service answers
 * the same way for all of them. Distinguishing "no such account" from "already
 * activated" from "wrong code" would let somebody holding a list of identifiers
 * work out which ones are mid-conversion.
 */
@Component({
  selector: 'app-activate',
  imports: [ReactiveFormsModule, RouterLink],
  styleUrl: './auth.css',
  template: `
    <div class="auth-wrap">
      <div class="logo">samaajconnect</div>

      <div class="auth-card">
        <h1 class="auth-heading">Set your password</h1>
        <p class="subtitle">
          Your Samaaj administrator gave you a one-time code. Use it here to choose a password,
          then sign in.
        </p>

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
            <p class="field-error">Enter the mobile number or email your Samaaj has for you.</p>
          }

          <label for="code">Activation code</label>
          <input
            id="code"
            class="input"
            formControlName="code"
            autocomplete="one-time-code"
            [attr.aria-invalid]="showError('code')"
          />
          @if (showError('code')) {
            <p class="field-error">Enter the code your Samaaj administrator gave you.</p>
          }

          <label for="password">Choose a password</label>
          <input
            id="password"
            class="input"
            type="password"
            formControlName="password"
            autocomplete="new-password"
            placeholder="••••••••"
            [attr.aria-invalid]="showError('password')"
          />
          @if (showError('password')) {
            <p class="field-error">Choose a password of at least 10 characters.</p>
          }

          @if (error(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          <div class="actions">
            <button class="btn" type="submit" [disabled]="busy()">
              {{ busy() ? 'Setting your password…' : 'Set password' }}
            </button>
            <a class="btn secondary" routerLink="/login">Back to sign in</a>
          </div>

          <p class="sr-only" role="status">{{ busy() ? 'Setting your password' : '' }}</p>
        </form>

        <p class="small auth-footer">
          A code can only be used once and expires. If yours no longer works, ask your Samaaj
          administrator for another.
        </p>
      </div>
    </div>
  `,
})
export class ActivateComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.formBuilder.nonNullable.group({
    mobileOrEmail: ['', Validators.required],
    code: ['', Validators.required],
    // The same floor the service applies, and the same one registration shows.
    // A converted child's first password is a real password, not a weaker one
    // because an administrator vouched for them.
    password: ['', [Validators.required, Validators.minLength(10)]],
  });

  showError(control: 'mobileOrEmail' | 'code' | 'password'): boolean {
    const field = this.form.controls[control];

    return field.invalid && (field.dirty || field.touched);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { mobileOrEmail, code, password } = this.form.getRawValue();

    this.busy.set(true);
    this.error.set(null);

    this.auth.activate(mobileOrEmail.trim(), code.trim(), password).subscribe({
      next: () => {
        this.busy.set(false);

        // To sign in, not to Home: there is no token yet. The query parameter
        // is how the login screen already carries a notice, the same as
        // `registered`.
        void this.router.navigate(['/login'], { queryParams: { activated: 'true' } });
      },
      error: (failure) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
