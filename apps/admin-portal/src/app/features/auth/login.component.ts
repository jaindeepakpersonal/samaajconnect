import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService, LoginResult, SignInFlow, describeError } from '@samaajconnect/shared';

const ADMIN_ROLES = [
  'SuperAdmin',
  'SamaajAdmin',
  'ContentModerator',
  'VolunteerGroupPresident',
  'PathshalaTeacher',
  'BoliManager',
  'AreaAdmin',
] as const;

/**
 * Sign in to the admin panel.
 *
 * The admin wireframe has no login screen - it opens straight onto the
 * dashboard - so this follows member-portal's `#login` for layout and copy
 * instead of inventing a new one. There is one login endpoint on the platform;
 * this is the same call the member portal makes, against the same accounts.
 *
 * **Two steps since 2026-09-27.** The password comes with a CAPTCHA (drawn by
 * identity-tenant-service, no third party), and on a device this account does
 * not trust a 6-digit code follows, sent to the account's contact address -
 * with a box to trust this browser for thirty days. `SignInFlow` in
 * libs/shared holds the steps; this draws them.
 *
 * A member with no administrative role is signed out again rather than shown an
 * empty panel. That is a courtesy, not a control: every screen behind this is
 * gated server-side, and a member who got in would simply be refused by each
 * endpoint in turn.
 */
@Component({
  selector: 'app-admin-login',
  imports: [ReactiveFormsModule],
  providers: [SignInFlow],
  styleUrl: './login.css',
  template: `
    <!-- A landmark, but no skip link: this page has nothing before its content
         to skip past, and a skip link that jumps nowhere is noise in the tab
         order rather than a service. -->
    <main class="login-wrap">
      <div class="brand">samaajconnect<br /><span>Unified Admin</span></div>

      <div class="card">
        <h1 class="login-heading">Sign in</h1>
        <p class="sub">Administrator access to the platform.</p>

        @if (sessionExpired()) {
          <p class="notice" role="status">Your session ended. Please sign in again.</p>
        }

        @if (flow.step() === 'password') {
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
                {{ flow.busy() ? 'Signing in…' : 'Sign in' }}
              </button>
            </div>
          </form>
        } @else {
          <form (ngSubmit)="verify()" novalidate>
            <p>
              We sent a 6-digit code to <b>{{ flow.codeSentTo() }}</b>. Enter it to finish signing in.
            </p>

            <label for="sign-in-code">Code</label>
            <input
              id="sign-in-code"
              class="input"
              inputmode="numeric"
              autocomplete="one-time-code"
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
              <p class="notice" role="status">A new code is on its way if the last one was sent over 30 seconds ago.</p>
            }

            <div class="actions">
              <button class="btn" type="submit" [disabled]="flow.busy() || code().trim().length !== 6">
                {{ flow.busy() ? 'Checking…' : 'Verify and sign in' }}
              </button>
              <button class="btn link" type="button" (click)="flow.resend()">Send a new code</button>
              <button class="btn link" type="button" (click)="startAgain()">Start again</button>
            </div>
          </form>
        }

        <p class="sr-only" role="status">{{ flow.busy() ? 'Signing in' : '' }}</p>

        <p class="small login-footer">
          Invited administrators set their first password by redeeming the one-time code their
          Samaaj administrator gave them, in the member portal.
        </p>
      </div>
    </main>
  `,
})
export class AdminLoginComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);

  readonly flow = inject(SignInFlow);

  readonly error = signal<string | null>(null);
  readonly sessionExpired = signal(this.route.snapshot.queryParamMap.get('expired') === 'true');
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
          this.land(session);
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
      next: (session) => this.land(session),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  startAgain(): void {
    this.error.set(null);
    this.code.set('');
    this.flow.startAgain();
  }

  private land(result: LoginResult): void {
    if (!result.roles.some((role) => ADMIN_ROLES.includes(role as never))) {
      this.auth.signOut().subscribe();
      this.startAgain();
      this.error.set(
        'That account does not administer anything on this platform. ' +
          'Members sign in through the member portal.',
      );
      return;
    }

    // An Area Admin manages one locality's members and nothing else, so
    // the dashboard's Samaaj-wide tiles are not theirs to read. A teacher
    // - a member who teaches, or somebody from outside the Samaaj - has
    // only the Pathshalas they teach.
    const only = (...allowed: string[]) => result.roles.every((role) => allowed.includes(role));
    const home = only('AreaAdmin', 'Member')
      ? '/members'
      : only('PathshalaTeacher', 'Member')
        ? '/pathshala'
        : '/dashboard';

    void this.router.navigateByUrl(this.route.snapshot.queryParamMap.get('returnUrl') ?? home);
  }
}
