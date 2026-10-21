import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SignInResult } from './auth.models';
import { AuthService } from './auth.service';

/**
 * The `DevCode` echo. identity-tenant-service sends a code on the response only
 * when `RevealSecretsInLogs` is on, which it refuses outside Development, and
 * that is the whole gate: the client prints whatever it was sent. The compose
 * stack's portals are production builds, so a client-side "is this a dev
 * build" check is exactly what kept the console silent where it was wanted.
 */
describe('AuthService dev code echo', () => {
  let auth: AuthService;
  let http: HttpTestingController;
  let info: ReturnType<typeof vi.spyOn>;

  const codeSent: SignInResult = {
    codeRequired: true,
    session: null,
    challengeToken: 'challenge-1',
    codeSentTo: 'ra***@example.com',
    challengeExpiresAt: '2026-09-27T12:10:00Z',
    deviceToken: null,
    deviceTrustedUntil: null,
    devCode: '482913',
  };

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    info = vi.spyOn(console, 'info').mockImplementation(() => undefined);

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: PLATFORM_ID, useValue: 'browser' }],
    });

    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => {
    http.verify();
    info.mockRestore();
  });

  it('prints the sign-in code the backend sent', () => {
    auth.login('ravi@example.com', 'a-password', 'captcha-1', 'ABCDE').subscribe();
    http.expectOne('/v1/identity/login').flush(codeSent);

    expect(info).toHaveBeenCalledWith('[dev] Sign-in code: 482913');
  });

  it('prints a resent code and a password-reset code, each named for what it is', () => {
    auth.resendSignInCode('challenge-1').subscribe();
    http.expectOne('/v1/identity/login/resend').flush({ devCode: '111222' });

    auth.requestPasswordReset('ravi@example.com').subscribe();
    http.expectOne('/v1/identity/password-reset/request').flush({ devCode: '333444' });

    expect(info).toHaveBeenCalledWith('[dev] Sign-in code: 111222');
    expect(info).toHaveBeenCalledWith('[dev] Password reset code: 333444');
  });

  it('prints nothing when the backend sent no code', () => {
    // Every real environment: RevealSecretsInLogs is off, so devCode is null.
    auth.login('ravi@example.com', 'a-password', 'captcha-1', 'ABCDE').subscribe();
    http.expectOne('/v1/identity/login').flush({ ...codeSent, devCode: null });

    auth.requestPasswordReset('nobody@example.com').subscribe();
    http.expectOne('/v1/identity/password-reset/request').flush({ devCode: null });

    expect(info).not.toHaveBeenCalled();
  });

  it('prints nothing for a response that predates the field', () => {
    const withoutField: Record<string, unknown> = { ...codeSent };
    delete withoutField['devCode'];

    auth.login('ravi@example.com', 'a-password', 'captcha-1', 'ABCDE').subscribe();
    http.expectOne('/v1/identity/login').flush(withoutField);

    expect(info).not.toHaveBeenCalled();
  });
});
