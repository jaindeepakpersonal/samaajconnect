import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService, describeError } from '@samaajconnect/shared';

type SignInMethod = 'password' | 'otp';

/**
 * Login, from the member-portal wireframe's `#login` screen.
 *
 * The OTP tab is real now. The wireframe's own OTP field has no explicit
 * "Send" control - only a "Resend OTP" link - which reads as the code going
 * out the moment the tab is opened. Real accounts do not get free credential
 * mailings on a tab click, so this app adds the one control the wireframe
 * left implicit: a "Send code" button, shown until a code has actually been
 * requested.
 *
 * "Forgot password?" is real now too, its own separate `#forgot`/`#otp`
 * flow (`/forgot`, `/reset`) - redeems into a new password rather than a
 * session, so it hands back here with a notice rather than signing anybody
 * in directly.
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

        @if (justReset()) {
          <p class="notice info" role="status">Your password has been reset. Sign in to continue.</p>
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
            (click)="selectMethod('password')"
          >
            Password
          </button>
          <button
            type="button"
            role="tab"
            [class.on]="method() === 'otp'"
            [attr.aria-selected]="method() === 'otp'"
            (click)="selectMethod('otp')"
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

          @if (method() === 'password') {
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
          } @else {
            @if (otpSent()) {
              <label for="otp-code">OTP</label>
              <input
                id="otp-code"
                class="input"
                inputmode="numeric"
                autocomplete="one-time-code"
                placeholder="6-digit code"
                [value]="otpCode()"
                (input)="otpCode.set($any($event.target).value)"
              />
              <p class="small auth-footer">
                <button
                  class="btn link"
                  type="button"
                  [disabled]="otpRequesting()"
                  (click)="sendOtp()"
                >
                  {{ otpRequesting() ? 'Sending…' : 'Resend OTP' }}
                </button>
              </p>
            } @else {
              <p class="small">We will send a 6-digit code to this identifier.</p>
            }

            @if (otpNotice(); as message) {
              <p class="notice info" role="status">{{ message }}</p>
            }
          }

          @if (error(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          <div class="actions">
            @if (method() === 'otp' && !otpSent()) {
              <button
                class="btn"
                type="button"
                [disabled]="otpRequesting()"
                (click)="sendOtp()"
              >
                {{ otpRequesting() ? 'Sending…' : 'Send code' }}
              </button>
            } @else {
              <button class="btn" type="submit" [disabled]="busy()">
                {{ busy() ? 'Signing in…' : 'Login' }}
              </button>
            }
            <a class="btn secondary" routerLink="/register">Register</a>
          </div>

          <p class="sr-only" role="status">{{ busy() ? 'Signing in' : '' }}</p>
        </form>

        <p class="small auth-footer">
          <a routerLink="/forgot">Forgot password?</a>
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
  readonly sessionExpired = signal(this.route.snapshot.queryParamMap.get('expired') === 'true');
  readonly justRegistered = signal(this.route.snapshot.queryParamMap.get('registered') === 'true');
  readonly justActivated = signal(this.route.snapshot.queryParamMap.get('activated') === 'true');
  readonly justReset = signal(this.route.snapshot.queryParamMap.get('reset') === 'true');
  readonly otherSamaaj = signal(this.route.snapshot.queryParamMap.get('otherSamaaj') === 'true');

  readonly otpSent = signal(false);
  readonly otpRequesting = signal(false);
  readonly otpCode = signal('');
  readonly otpNotice = signal<string | null>(null);

  readonly form = this.formBuilder.nonNullable.group({
    mobileOrEmail: ['', Validators.required],
    password: ['', Validators.required],
  });

  showError(control: 'mobileOrEmail' | 'password'): boolean {
    const field = this.form.controls[control];

    return field.invalid && (field.dirty || field.touched);
  }

  selectMethod(method: SignInMethod): void {
    this.method.set(method);
    this.error.set(null);
  }

  /**
   * Requests a code for whatever identifier is in the shared field. Doubles
   * as "Resend" once one has already gone out - re-requesting is normal, the
   * same reasoning `RequestLoginOtpCommand` mints a fresh code rather than
   * refusing a second request.
   */
  sendOtp(): void {
    const identifier = this.form.controls.mobileOrEmail.value;

    if (!identifier) {
      this.form.controls.mobileOrEmail.markAsTouched();
      return;
    }

    this.otpRequesting.set(true);
    this.error.set(null);

    this.auth.requestLoginOtp(identifier).subscribe({
      next: () => {
        this.otpRequesting.set(false);
        this.otpSent.set(true);
        // The same message regardless of whether the identifier turned out to
        // belong to a real account - the request endpoint answers the same
        // way either way, and this screen must not say anything it does not.
        this.otpNotice.set('If that identifier has an account, a code has been sent.');
      },
      error: (failure: unknown) => {
        this.otpRequesting.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  submit(): void {
    if (this.method() === 'otp') {
      this.submitOtp();
      return;
    }

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

  private submitOtp(): void {
    const identifier = this.form.controls.mobileOrEmail.value;

    if (!identifier || !this.otpCode()) {
      this.form.controls.mobileOrEmail.markAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    this.auth.loginWithOtp(identifier, this.otpCode()).subscribe({
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
