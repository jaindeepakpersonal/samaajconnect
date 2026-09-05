import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '@samaajconnect/shared';

/**
 * Reset password, step one - the wireframe's `#forgot` screen.
 *
 * Answers the same way whether or not the identifier belongs to a real,
 * active account: telling the two apart would hand an attacker a free
 * account-enumeration oracle, the same reasoning every other anonymous
 * credential endpoint on this platform already follows. There is nothing to
 * branch on in the response, so the confirmation is unconditional and the
 * screen simply offers to continue to the next step.
 */
@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink],
  styleUrl: './auth.css',
  template: `
    <div class="auth-wrap">
      <div class="logo">samaajconnect</div>

      <div class="auth-card">
        <h1 class="auth-heading">Reset password</h1>
        <p class="subtitle">We'll send a one-time code to verify it's you.</p>

        @if (!sent()) {
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

            <div class="actions">
              <button class="btn" type="submit" [disabled]="busy()">
                {{ busy() ? 'Sending…' : 'Send Code' }}
              </button>
              <a class="btn secondary" routerLink="/login">Back to sign in</a>
            </div>
          </form>
        } @else {
          <p class="notice info" role="status">
            If that identifier has an account, a code has been sent. It expires in 10 minutes.
          </p>

          <div class="actions">
            <a class="btn" [routerLink]="['/reset']" [queryParams]="{ identifier: identifier() }">
              Enter code
            </a>
            <a class="btn secondary" routerLink="/login">Back to sign in</a>
          </div>
        }
      </div>
    </div>
  `,
})
export class ForgotPasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly formBuilder = inject(FormBuilder);

  readonly busy = signal(false);
  readonly sent = signal(false);
  readonly identifier = signal('');

  readonly form = this.formBuilder.nonNullable.group({
    mobileOrEmail: ['', Validators.required],
  });

  showError(control: 'mobileOrEmail'): boolean {
    const field = this.form.controls[control];

    return field.invalid && (field.dirty || field.touched);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { mobileOrEmail } = this.form.getRawValue();

    this.busy.set(true);

    this.auth.requestPasswordReset(mobileOrEmail.trim()).subscribe({
      // The request never fails in a way this screen should show - a wrong
      // identifier is exactly the case it must answer identically to a right
      // one. A genuine network/server failure would surface from /reset's own
      // redeem call instead, where a failure is real and worth showing.
      next: () => {
        this.busy.set(false);
        this.identifier.set(mobileOrEmail.trim());
        this.sent.set(true);
      },
      error: () => {
        this.busy.set(false);
        this.identifier.set(mobileOrEmail.trim());
        this.sent.set(true);
      },
    });
  }
}
