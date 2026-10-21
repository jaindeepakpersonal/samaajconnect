import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService, LoginResult, SignInFlow, describeError } from '@samaajconnect/shared';

/**
 * Login, from the member-portal wireframe's `#login` screen.
 *
 * **Two steps since 2026-09-27, and no code-only tab.** The password comes
 * with a CAPTCHA drawn by identity-tenant-service itself (no third party learns
 * who signs in), and on a browser the account does not trust, a 6-digit code
 * follows - sent to the member's own mobile or email - with a box to trust
 * this browser for thirty days. The wireframe's OTP tab signed in with the code
 * alone; with a second factor required of everybody that was the one way in
 * that skipped the password, so it went (confirmed with the user). The code the
 * wireframe drew is still here: it is the second step now, not a substitute
 * for the first. `SignInFlow` in libs/shared holds the steps; this draws them.
 *
 * "Forgot password?" is its own separate `#forgot`/`#otp` flow (`/forgot`,
 * `/reset`) - redeems into a new password rather than a session, so it hands
 * back here with a notice rather than signing anybody in directly.
 */
@Component({
  selector: 'app-login',
  // FormsModule for the code step's plain <form>: without NgForm on it,
  // (ngSubmit) never fires and the browser submits natively, reloading the page.
  imports: [FormsModule, ReactiveFormsModule, RouterLink],
  providers: [SignInFlow],
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
            Your account is ready. Sign in to continue - the first time, we send a code to your
            mobile or email to confirm it is you.
          </p>
        }

        @if (flow.step() === 'password') {
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

            <label for="captcha-answer">Type the characters in the picture</label>
            <div class="captcha-row">
              @if (flow.captcha(); as captcha) {
                <img class="captcha-img" [src]="'data:image/png;base64,' + captcha.imagePng"
                  alt="A picture of five letters and digits to type" width="200" height="70" />
              } @else if (flow.captchaUnavailable()) {
                <span class="small" role="alert">The picture did not load.</span>
              } @else {
                <span class="small" role="status">Loading the picture…</span>
              }
              <button class="btn link" type="button" (click)="flow.loadCaptcha()">A different picture</button>
            </div>
            <input
              id="captcha-answer"
              class="input"
              formControlName="captchaAnswer"
              autocomplete="off"
              autocapitalize="characters"
              maxlength="10"
              [attr.aria-invalid]="showError('captchaAnswer')"
            />
            @if (showError('captchaAnswer')) {
              <p class="field-error">Type the characters you see.</p>
            }

            @if (error(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            <div class="actions">
              <button class="btn" type="submit" [disabled]="flow.busy() || !flow.captcha()">
                {{ flow.busy() ? 'Signing in…' : 'Login' }}
              </button>
              <a class="btn secondary" routerLink="/register">Register</a>
            </div>
          </form>
        } @else {
          <form (ngSubmit)="verify()" novalidate>
            <p>
              We sent a 6-digit code to <b>{{ flow.codeSentTo() }}</b>. Enter it to finish signing in.
            </p>

            <label for="sign-in-code">OTP</label>
            <input
              id="sign-in-code"
              class="input"
              inputmode="numeric"
              autocomplete="one-time-code"
              placeholder="6-digit code"
              maxlength="6"
              [value]="code()"
              (input)="code.set($any($event.target).value)"
            />

            <label class="trust-row">
              <input type="checkbox" [checked]="trust()" (change)="trust.set($any($event.target).checked)" />
              Trust this browser for 30 days - it will not ask for a code here until then
            </label>

            @if (error(); as message) {
              <p class="notice error" role="alert">{{ message }}</p>
            }

            @if (flow.resent()) {
              <p class="notice info" role="status">
                A new code is on its way if the last one was sent over 30 seconds ago.
              </p>
            }

            <div class="actions">
              <button class="btn" type="submit" [disabled]="flow.busy() || code().trim().length !== 6">
                {{ flow.busy() ? 'Checking…' : 'Verify and sign in' }}
              </button>
            </div>

            <p class="small auth-footer">
              <button class="btn link" type="button" (click)="flow.resend()">Resend OTP</button>
              ·
              <button class="btn link" type="button" (click)="startAgain()">Start again</button>
            </p>
          </form>
        }

        <p class="sr-only" role="status">{{ flow.busy() ? 'Signing in' : '' }}</p>

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
export class LoginComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);

  readonly flow = inject(SignInFlow);

  readonly error = signal<string | null>(null);
  readonly sessionExpired = signal(this.route.snapshot.queryParamMap.get('expired') === 'true');
  readonly justRegistered = signal(this.route.snapshot.queryParamMap.get('registered') === 'true');
  readonly justActivated = signal(this.route.snapshot.queryParamMap.get('activated') === 'true');
  readonly justReset = signal(this.route.snapshot.queryParamMap.get('reset') === 'true');
  readonly otherSamaaj = signal(this.route.snapshot.queryParamMap.get('otherSamaaj') === 'true');

  readonly code = signal('');
  readonly trust = signal(false);

  readonly form = this.formBuilder.nonNullable.group({
    mobileOrEmail: ['', Validators.required],
    password: ['', Validators.required],
    captchaAnswer: ['', Validators.required],
  });

  ngOnInit(): void {
    this.flow.loadCaptcha();
  }

  showError(control: 'mobileOrEmail' | 'password' | 'captchaAnswer'): boolean {
    const field = this.form.controls[control];

    return field.invalid && (field.dirty || field.touched);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);

    const { mobileOrEmail, password, captchaAnswer } = this.form.getRawValue();

    this.flow.signIn(mobileOrEmail, password, captchaAnswer).subscribe({
      next: (session) => {
        // Each picture is good once, so the answer box starts empty for the next.
        this.form.controls.captchaAnswer.reset('');

        if (session) {
          this.landOrRefuse(session);
        }
      },
      error: (failure: unknown) => {
        this.form.controls.captchaAnswer.reset('');
        this.error.set(describeError(failure));
      },
    });
  }

  verify(): void {
    this.error.set(null);

    this.flow.verify(this.code(), this.trust()).subscribe({
      next: (session) => this.landOrRefuse(session),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  startAgain(): void {
    this.error.set(null);
    this.code.set('');
    this.flow.startAgain();
  }

  /**
   * A Pathshala teacher from outside the Samaaj has an account only to teach:
   * the teacher role and not the Member role. There is nothing for them here,
   * and every member-facing read refuses them server-side (their account does
   * not hold Members.Read), so they are signed out again and pointed at the
   * admin panel rather than shown a portal of errors.
   *
   * Everybody else lands on Timeline - the wireframe sent a member to their
   * Samaaj's subdomain, and on one domain the token already names the Samaaj.
   */
  private landOrRefuse(result: LoginResult): void {
    if (result.roles.includes('PathshalaTeacher') && !result.roles.includes('Member')) {
      this.auth.signOut().subscribe();
      this.startAgain();
      this.error.set(
        'This account is for teaching at a Pathshala, and teachers sign in to the admin panel. ' +
          'The member portal is for members of the Samaaj.',
      );
      return;
    }

    void this.router.navigateByUrl(this.route.snapshot.queryParamMap.get('returnUrl') ?? '/timeline');
  }
}
