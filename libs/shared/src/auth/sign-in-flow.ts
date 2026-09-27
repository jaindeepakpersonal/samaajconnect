import { Injectable, inject, signal } from '@angular/core';
import { Observable, finalize, map, tap } from 'rxjs';
import { AuthService } from './auth.service';
import { Captcha, LoginResult } from './auth.models';

/**
 * The two-step sign-in both portals run, without its markup: a CAPTCHA and a
 * password, then - on a device the account does not trust - the code sent to
 * the member.
 *
 * **Why here, and why no template.** Both apps sign in the same way, and the
 * rules are easy to get subtly wrong twice: a CAPTCHA is single use, so every
 * attempt must fetch a fresh one whether it succeeded or not; the challenge
 * belongs to one password step and is dropped on "start again". This library's
 * tests cannot compile a template (see its CLAUDE.md), so the logic lives here
 * where it is tested and each app draws its own few lines of form.
 *
 * Provide it per component (\`providers: [SignInFlow]\`): it holds one sign-in's
 * state, and a second visit to the page should start clean.
 */
@Injectable()
export class SignInFlow {
  private readonly auth = inject(AuthService);

  /** Which form to show. */
  readonly step = signal<'password' | 'code'>('password');

  /** The picture to show; null while one loads, or when none could be. */
  readonly captcha = signal<Captcha | null>(null);

  /** True when no CAPTCHA could be fetched - the form cannot be submitted then. */
  readonly captchaUnavailable = signal(false);

  /** Where the code went, masked by the server. */
  readonly codeSentTo = signal<string | null>(null);

  /** A resend was asked for; the page says a new code is on its way. */
  readonly resent = signal(false);

  readonly busy = signal(false);

  private challengeToken: string | null = null;

  /** A fresh CAPTCHA. Called on arrival, after every attempt, and by "a different picture". */
  loadCaptcha(): void {
    this.captcha.set(null);
    this.captchaUnavailable.set(false);

    this.auth.captcha().subscribe({
      next: (found) => this.captcha.set(found),
      error: () => this.captchaUnavailable.set(true),
    });
  }

  /**
   * Step one. Emits the session when this device is trusted, and null when a
   * code has been sent - the form then moves to the code step by itself. A
   * CAPTCHA is spent by any attempt, so a new one is fetched either way.
   */
  signIn(mobileOrEmail: string, password: string, captchaAnswer: string): Observable<LoginResult | null> {
    const captcha = this.captcha();

    this.busy.set(true);

    return this.auth.login(mobileOrEmail, password, captcha?.captchaId ?? '', captchaAnswer).pipe(
      tap({
        next: (result) => {
          if (result.codeRequired) {
            this.challengeToken = result.challengeToken;
            this.codeSentTo.set(result.codeSentTo);
            this.resent.set(false);
            this.step.set('code');
          } else {
            this.loadCaptcha();
          }
        },
        error: () => this.loadCaptcha(),
      }),
      map((result) => result.session),
      finalize(() => this.busy.set(false)),
    );
  }

  /** Step two: the code, and whether to trust this device for thirty days. */
  verify(code: string, trustDevice: boolean): Observable<LoginResult> {
    this.busy.set(true);

    return this.auth.verifySignInCode(this.challengeToken ?? '', code.trim(), trustDevice).pipe(
      map((result) => {
        if (!result.session) {
          throw new Error('Signed in without a session.');
        }

        return result.session;
      }),
      finalize(() => this.busy.set(false)),
    );
  }

  /** Asks for another code. The server sends at most one every thirty seconds, and says the same either way. */
  resend(): void {
    if (!this.challengeToken) {
      return;
    }

    this.auth.resendSignInCode(this.challengeToken).subscribe({
      next: () => this.resent.set(true),
      error: () => this.resent.set(true),
    });
  }

  /**
   * Back to the password - a new challenge on the next attempt. A new CAPTCHA
   * only when leaving the code step: the password step fetched one after its
   * own attempt already, and asking twice would leave two in flight.
   */
  startAgain(): void {
    const fromCode = this.step() === 'code';

    this.challengeToken = null;
    this.codeSentTo.set(null);
    this.resent.set(false);
    this.step.set('password');

    if (fromCode) {
      this.loadCaptcha();
    }
  }
}
