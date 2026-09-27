import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { API_CONFIG, LoginResult, SignInResult, TokenStore } from '@samaajconnect/shared';
import { LoginComponent } from './login.component';

function session(overrides: Partial<LoginResult> = {}): LoginResult {
  return {
    accessToken: 'signed-token',
    expiresAt: new Date().toISOString(),
    refreshToken: 'refresh-token',
    refreshTokenExpiresAt: new Date().toISOString(),
    userId: 'u1',
    tenantId: 't1',
    tenantSlug: '',
    fullName: 'Ravi Shah',
    roles: ['Member'],
    ...overrides,
  };
}

function signedIn(overrides: Partial<LoginResult> = {}): SignInResult {
  return {
    codeRequired: false,
    session: session(overrides),
    challengeToken: null,
    codeSentTo: null,
    challengeExpiresAt: null,
    deviceToken: null,
    deviceTrustedUntil: null,
  };
}

const CODE_SENT: SignInResult = {
  codeRequired: true,
  session: null,
  challengeToken: 'challenge-1',
  codeSentTo: 'ra***@example.com',
  challengeExpiresAt: new Date().toISOString(),
  deviceToken: null,
  deviceTrustedUntil: null,
};

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let http: HttpTestingController;
  let captchas = 0;

  function setup(queryParams: Record<string, string> = {}): void {
    localStorage.clear();
    sessionStorage.clear();

    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([{ path: 'timeline', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: new Map(Object.entries(queryParams)) } },
        },
      ],
    });

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    answerCaptcha();
  }

  /** Every CAPTCHA the page asks for - on arrival, and after every attempt. */
  function answerCaptcha(): void {
    captchas++;
    http.expectOne('/v1/identity/captcha').flush({ captchaId: `c${captchas}`, imagePng: 'iVBORw0KGgo=', expiresAt: '' });
    fixture.detectChanges();
  }

  function fill(): void {
    component.form.setValue({ mobileOrEmail: 'ravi@example.com', password: 'a-long-password', captchaAnswer: 'ab3cd' });
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  afterEach(() => http?.verify());

  it('shows the CAPTCHA picture it was given, with no code-only tab', () => {
    setup();

    const image = (fixture.nativeElement as HTMLElement).querySelector<HTMLImageElement>('img.captcha-img');
    expect(image?.getAttribute('src')).toBe('data:image/png;base64,iVBORw0KGgo=');
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="tab"]')).toBeNull();
  });

  it('does not call the API when the form is empty', () => {
    setup();

    component.submit();

    http.expectNone('/v1/identity/login');
  });

  it('sends the CAPTCHA with the password and asks for the code, storing nothing yet', () => {
    setup();
    fill();
    component.submit();

    const request = http.expectOne('/v1/identity/login');
    expect(request.request.body).toEqual({
      mobileOrEmail: 'ravi@example.com',
      password: 'a-long-password',
      captchaId: `c${captchas}`,
      captchaAnswer: 'ab3cd',
      deviceToken: null,
    });
    request.flush(CODE_SENT);
    fixture.detectChanges();

    expect(text()).toContain('We sent a 6-digit code to ra***@example.com');
    expect(TestBed.inject(TokenStore).token()).toBeNull();
  });

  it('verifies the code, trusts the browser when asked, and lands on Timeline', async () => {
    setup();
    fill();
    component.submit();
    http.expectOne('/v1/identity/login').flush(CODE_SENT);
    fixture.detectChanges();

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    component.code.set('123456');
    component.trust.set(true);
    component.verify();

    const request = http.expectOne('/v1/identity/login/verify');
    expect(request.request.body).toEqual({ challengeToken: 'challenge-1', code: '123456', trustDevice: true });
    request.flush({ ...signedIn(), deviceToken: 'device-1' });

    expect(TestBed.inject(TokenStore).token()).toBe('signed-token');
    expect(localStorage.getItem('samaajconnect.device')).toBe('device-1');
    expect(navigate).toHaveBeenCalledWith('/timeline');
  });

  it('shows the API message when the credentials are wrong, with a fresh CAPTCHA to try again', () => {
    setup();
    fill();
    component.submit();

    http.expectOne('/v1/identity/login').flush(
      { title: 'Auth.InvalidCredentials', detail: 'Incorrect mobile/email or password.' },
      { status: 401, statusText: 'Unauthorized' },
    );
    answerCaptcha();

    expect(text()).toContain('Incorrect mobile/email or password.');
    expect(component.form.controls.captchaAnswer.value).toBe('');
    expect(component.flow.busy()).toBe(false);
  });

  it('signs straight in on a trusted browser, and goes to the return url', async () => {
    setup({ returnUrl: '/family' });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    fill();
    component.submit();
    http.expectOne('/v1/identity/login').flush(signedIn());
    answerCaptcha();

    expect(navigate).toHaveBeenCalledWith('/family');
  });

  it('turns away a Pathshala teacher who is not a member, and signs them out again', () => {
    setup();
    fill();
    component.submit();
    http.expectOne('/v1/identity/login').flush(CODE_SENT);

    component.code.set('123456');
    component.verify();
    http.expectOne('/v1/identity/login/verify').flush(signedIn({ roles: ['PathshalaTeacher'] }));
    http.expectOne('/v1/identity/logout').flush(null);
    answerCaptcha();

    expect(text()).toContain('teachers sign in to the admin panel');
    expect(component.flow.step()).toBe('password');
  });

  it('lets a member who also teaches in', () => {
    setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    fill();
    component.submit();
    http.expectOne('/v1/identity/login').flush(signedIn({ roles: ['Member', 'PathshalaTeacher'] }));
    answerCaptcha();

    expect(navigate).toHaveBeenCalledWith('/timeline');
  });

  it('resends a code, and starting again goes back to the password with a new picture', () => {
    setup();
    fill();
    component.submit();
    http.expectOne('/v1/identity/login').flush(CODE_SENT);
    fixture.detectChanges();

    component.flow.resend();
    http.expectOne('/v1/identity/login/resend').flush(null);
    fixture.detectChanges();
    expect(text()).toContain('A new code is on its way');

    component.startAgain();
    answerCaptcha();

    expect(component.flow.step()).toBe('password');
    expect(text()).toContain('Type the characters in the picture');
  });

  it('tells the member their session ended when redirected by the interceptor', () => {
    setup({ expired: 'true' });

    expect(text()).toContain('Your session ended');
  });

  it('confirms registration succeeded when arriving from the register screen', () => {
    setup({ registered: 'true' });

    expect(text()).toContain('Your account is ready');
  });

  it('links forgot password to the real reset flow', () => {
    setup();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href="/forgot"]');

    expect(link?.textContent).toContain('Forgot password?');
  });

  it('confirms a password reset when arriving from the reset screen', () => {
    setup({ reset: 'true' });

    expect(text()).toContain('Your password has been reset');
  });
});
