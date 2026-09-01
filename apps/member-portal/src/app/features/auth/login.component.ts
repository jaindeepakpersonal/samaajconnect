import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';

type SignInMethod = 'password' | 'otp';

/**
 * Login, from the member-portal wireframe's `#login` screen.
 *
 * The wireframe offers a Password/OTP switch. There is no OTP endpoint in
 * API-CONTRACTS.md yet, so the tab is present but disabled with a plain
 * explanation rather than wired to something that does not exist - the
 * wireframe-to-angular skill is explicit that a missing endpoint means build
 * the backend, never fake the call.
 */
@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  styleUrl: './auth.css',
  template: `
    <div class="auth-wrap">
      <div class="logo">samaajconnect</div>

      <div class="auth-card">
        <h1 class="auth-heading">Login</h1>
        <p class="subtitle">Common login automatically routes you to your registered Samaaj.</p>

        @if (sessionExpired()) {
          <p class="notice info" role="status">Your session ended. Please sign in again.</p>
        }

        @if (otherSamaaj()) {
          <p class="notice info" role="status">
            That link belongs to a different Samaaj from the one you were signed in
            to. Please sign in again.
          </p>
        }

        @if (justActivated()) {
          <p class="notice info" role="status">Your password is set. Sign in to continue.</p>
        }

        @if (justRegistered()) {
          <p class="notice info" role="status">
            Your account is ready. Sign in to continue. We will ask you to verify your mobile
            number once verification messages are switched on.
          </p>
        }

        <div class="switch" role="tablist" aria-label="Sign-in method">
          <button
            type="button"
            role="tab"
            [class.on]="method() === 'password'"
            [attr.aria-selected]="method() === 'password'"
            (click)="method.set('password')"
          >
            Password
          </button>
          <button
            type="button"
            role="tab"
            [class.on]="method() === 'otp'"
            [attr.aria-selected]="method() === 'otp'"
            [disabled]="true"
            title="One-time-code sign-in is not available yet"
          >
            OTP
          </button>
        </div>

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

          @if (resetNotice()) {
            <p class="notice info" role="status">
              Password reset is not available yet. Please contact your Samaaj administrator.
            </p>
          }

          <div class="actions">
            <button class="btn" type="submit" [disabled]="busy()">
              {{ busy() ? 'Signing in…' : 'Login' }}
            </button>
            <a class="btn secondary" routerLink="/register">Register</a>
          </div>

          <p class="sr-only" role="status">{{ busy() ? 'Signing in' : '' }}</p>
        </form>

        <p class="small auth-footer">
          <button class="btn link" type="button" (click)="resetNotice.set(true)">
            Forgot password?
          </button>
        </p>

        <!--
          The way in for anyone an administrator invited, and for an adult child
          whose conversion was approved. Three screens in the admin panel tell
          people to redeem their code "in the member portal", so it has to be
          findable from the screen they land on.
        -->
        <p class="small auth-footer">
          Given a one-time code?
          <a routerLink="/activate">Set your password</a>
        </p>
      </div>
    </div>
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);

  readonly method = signal<SignInMethod>('password');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly resetNotice = signal(false);
  readonly sessionExpired = signal(this.route.snapshot.queryParamMap.get('expired') === 'true');
  readonly justRegistered = signal(this.route.snapshot.queryParamMap.get('registered') === 'true');
  readonly justActivated = signal(this.route.snapshot.queryParamMap.get('activated') === 'true');
  readonly otherSamaaj = signal(this.route.snapshot.queryParamMap.get('otherSamaaj') === 'true');

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
      next: () => {
        this.busy.set(false);
        this.goHome();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  /**
   * The wireframe sent a member to their Samaaj's subdomain after signing in.
   * The platform is single-domain now, so there is nowhere else to go: the
   * token already names the Samaaj and the gateway reads it from there.
   */
  private goHome(): void {
    void this.router.navigateByUrl(
      this.route.snapshot.queryParamMap.get('returnUrl') ?? '/home');
  }
}
