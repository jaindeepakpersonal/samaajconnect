import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LoginResult, SignInResult } from './auth.models';
import { SignInFlow } from './sign-in-flow';
import { TokenStore } from './token.store';

/**
 * Signing in, as both portals run it: CAPTCHA and password, then a code on a
 * device the account does not trust. What these hold is what is easy to get
 * wrong: a CAPTCHA is single use, so every attempt fetches a new one; the
 * device token is kept, and sent next time; nothing is stored until there is a
 * session.
 */
describe('SignInFlow', () => {
  let flow: SignInFlow;
  let http: HttpTestingController;

  const session: LoginResult = {
    accessToken: 'access',
    expiresAt: '2026-09-27T12:15:00Z',
    refreshToken: 'refresh',
    refreshTokenExpiresAt: '2026-10-11T12:00:00Z',
    userId: 'u1',
    tenantId: 't1',
    tenantSlug: 'mahavir',
    fullName: 'Ravi Shah',
    roles: ['Member'],
  };

  const codeSent: SignInResult = {
    codeRequired: true,
    session: null,
    challengeToken: 'challenge-1',
    codeSentTo: 'ra***@example.com',
    challengeExpiresAt: '2026-09-27T12:10:00Z',
    deviceToken: null,
    deviceTrustedUntil: null,
  };

  const signedIn = (deviceToken: string | null = null): SignInResult => ({
    ...codeSent,
    codeRequired: false,
    session,
    challengeToken: null,
    codeSentTo: null,
    deviceToken,
  });

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [SignInFlow, provideHttpClient(), provideHttpClientTesting(), { provide: PLATFORM_ID, useValue: 'browser' }],
    });

    flow = TestBed.inject(SignInFlow);
    http = TestBed.inject(HttpTestingController);

    flow.loadCaptcha();
    http.expectOne('/v1/identity/captcha').flush({ captchaId: 'c1', imagePng: 'iVBOR', expiresAt: '' });
  });

  afterEach(() => http.verify());

  it('sends the CAPTCHA with the password, then moves to the code step with nothing stored', () => {
    let emitted: LoginResult | null | undefined;
    flow.signIn('ravi@example.com', 'password', 'ab3cd').subscribe((value) => (emitted = value));

    const request = http.expectOne('/v1/identity/login');
    expect(request.request.body).toEqual({
      mobileOrEmail: 'ravi@example.com',
      password: 'password',
      captchaId: 'c1',
      captchaAnswer: 'ab3cd',
      deviceToken: null,
    });
    request.flush(codeSent);

    expect(emitted).toBeNull();
    expect(flow.step()).toBe('code');
    expect(flow.codeSentTo()).toBe('ra***@example.com');
    expect(TestBed.inject(TokenStore).token()).toBeNull();
  });

  it('fetches a fresh CAPTCHA after a refused attempt, because the old one is spent', () => {
    flow.signIn('ravi@example.com', 'wrong', 'ab3cd').subscribe({ error: () => undefined });
    http.expectOne('/v1/identity/login').flush({ title: 'Auth.InvalidCredentials' }, { status: 401, statusText: 'Unauthorized' });

    http.expectOne('/v1/identity/captcha').flush({ captchaId: 'c2', imagePng: 'iVBOR', expiresAt: '' });
    expect(flow.captcha()?.captchaId).toBe('c2');
    expect(flow.step()).toBe('password');
  });

  it('verifies the code with the challenge, keeps the session and remembers a trusted device', () => {
    flow.signIn('ravi@example.com', 'password', 'ab3cd').subscribe();
    http.expectOne('/v1/identity/login').flush(codeSent);

    let result: LoginResult | undefined;
    flow.verify(' 123456 ', true).subscribe((value) => (result = value));

    const request = http.expectOne('/v1/identity/login/verify');
    expect(request.request.body).toEqual({ challengeToken: 'challenge-1', code: '123456', trustDevice: true });
    request.flush(signedIn('device-1'));

    expect(result?.accessToken).toBe('access');
    expect(TestBed.inject(TokenStore).token()).toBe('access');
    expect(localStorage.getItem('samaajconnect.device')).toBe('device-1');
  });

  it('sends the remembered device token, and a trusted device signs in at once', () => {
    localStorage.setItem('samaajconnect.device', 'device-1');

    let emitted: LoginResult | null | undefined;
    flow.signIn('ravi@example.com', 'password', 'ab3cd').subscribe((value) => (emitted = value));

    const request = http.expectOne('/v1/identity/login');
    expect(request.request.body.deviceToken).toBe('device-1');
    request.flush(signedIn());
    http.expectOne('/v1/identity/captcha').flush({ captchaId: 'c3', imagePng: 'iVBOR', expiresAt: '' });

    expect(emitted?.accessToken).toBe('access');
    expect(flow.step()).toBe('password');
  });

  it('resends for the open challenge, and starting again drops it', () => {
    flow.signIn('ravi@example.com', 'password', 'ab3cd').subscribe();
    http.expectOne('/v1/identity/login').flush(codeSent);

    flow.resend();
    expect(http.expectOne('/v1/identity/login/resend').request.body).toEqual({ challengeToken: 'challenge-1' });
    expect(flow.resent()).toBe(false);

    flow.startAgain();
    http.expectOne('/v1/identity/captcha').flush({ captchaId: 'c4', imagePng: 'iVBOR', expiresAt: '' });

    expect(flow.step()).toBe('password');
    expect(flow.codeSentTo()).toBeNull();

    flow.resend();
    http.expectNone('/v1/identity/login/resend');
  });

  it('says so when no CAPTCHA can be fetched', () => {
    flow.loadCaptcha();
    http.expectOne('/v1/identity/captcha').flush({}, { status: 503, statusText: 'Unavailable' });

    expect(flow.captchaUnavailable()).toBe(true);
    expect(flow.captcha()).toBeNull();
  });
});
