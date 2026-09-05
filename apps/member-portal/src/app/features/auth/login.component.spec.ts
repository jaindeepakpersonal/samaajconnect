import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { API_CONFIG, TokenStore } from '@samaajconnect/shared';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let http: HttpTestingController;

  function setup(queryParams: Record<string, string> = {}): void {
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([{ path: 'home', children: [] }]),
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
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  afterEach(() => http?.verify());

  it('does not call the API when the form is empty', () => {
    setup();

    component.submit();

    http.expectNone('/v1/identity/login');
  });

  it('signs in and stores the token', () => {
    setup();

    component.form.setValue({ mobileOrEmail: 'ravi@example.com', password: 'a-long-password' });
    component.submit();

    const request = http.expectOne('/v1/identity/login');
    expect(request.request.body).toEqual({
      mobileOrEmail: 'ravi@example.com',
      password: 'a-long-password',
    });

    request.flush({
      accessToken: 'signed-token',
      expiresAt: new Date().toISOString(),
      userId: 'u1',
      tenantId: 't1',
      tenantSlug: '',
      fullName: 'Ravi Shah',
      roles: ['Member'],
    });

    expect(TestBed.inject(TokenStore).token()).toBe('signed-token');
  });

  it('shows the API message when the credentials are wrong', () => {
    setup();

    component.form.setValue({ mobileOrEmail: 'ravi@example.com', password: 'wrong' });
    component.submit();

    http.expectOne('/v1/identity/login').flush(
      { title: 'Auth.InvalidCredentials', detail: 'Incorrect mobile/email or password.' },
      { status: 401, statusText: 'Unauthorized' },
    );
    fixture.detectChanges();

    expect(component.error()).toBe('Incorrect mobile/email or password.');
    expect(text()).toContain('Incorrect mobile/email or password.');
  });

  it('stops showing the busy state after a failure so the member can retry', () => {
    setup();

    component.form.setValue({ mobileOrEmail: 'ravi@example.com', password: 'wrong' });
    component.submit();

    http.expectOne('/v1/identity/login').flush({}, { status: 500, statusText: 'Server Error' });

    expect(component.busy()).toBe(false);
  });

  it("goes to the return url after signing in - there is no subdomain to move to", async () => {
    setup({ returnUrl: '/home' });

    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    component.form.setValue({ mobileOrEmail: 'ravi@example.com', password: 'a-long-password' });
    component.submit();

    http.expectOne('/v1/identity/login').flush({
      accessToken: 't',
      expiresAt: new Date().toISOString(),
      userId: 'u1',
      tenantId: "t1",
      tenantSlug: "mahavir-samaj",
      fullName: 'Ravi Shah',
      roles: ['SuperAdmin'],
    });

    expect(navigate).toHaveBeenCalledWith('/home');
  });

  it('tells the member their session ended when redirected by the interceptor', () => {
    setup({ expired: 'true' });

    expect(text()).toContain('Your session ended');
  });

  it('confirms registration succeeded when arriving from the register screen', () => {
    setup({ registered: 'true' });

    expect(text()).toContain('Your account is ready');
  });

  // ---- OTP sign-in --------------------------------------------------------

  function switchToOtp(): void {
    const tabs = (fixture.nativeElement as HTMLElement).querySelectorAll('[role="tab"]');
    const otp = Array.from(tabs).find((tab) => tab.textContent?.includes('OTP')) as HTMLButtonElement;

    otp.click();
    fixture.detectChanges();
  }

  it('offers OTP as a real choice, not a disabled one', () => {
    setup();
    switchToOtp();

    expect(component.method()).toBe('otp');
  });

  it('sends a code for whatever identifier is in the field', () => {
    setup();
    switchToOtp();

    component.form.controls.mobileOrEmail.setValue('ravi@example.com');
    component.sendOtp();

    const request = http.expectOne('/v1/identity/otp/request');
    expect(request.request.body).toEqual({ mobileOrEmail: 'ravi@example.com' });

    request.flush(null);
    fixture.detectChanges();

    expect(component.otpSent()).toBe(true);
    expect(text()).toContain('a code has been sent');
  });

  it('does not request a code with no identifier', () => {
    setup();
    switchToOtp();

    component.sendOtp();

    http.expectNone('/v1/identity/otp/request');
  });

  it('signs in with the code and stores the token, the same as a password', () => {
    setup();
    switchToOtp();

    component.form.controls.mobileOrEmail.setValue('ravi@example.com');
    component.sendOtp();
    http.expectOne('/v1/identity/otp/request').flush(null);

    component.otpCode.set('482913');
    component.submit();

    const request = http.expectOne('/v1/identity/otp/login');
    expect(request.request.body).toEqual({ mobileOrEmail: 'ravi@example.com', code: '482913' });

    request.flush({
      accessToken: 'signed-token',
      expiresAt: new Date().toISOString(),
      userId: 'u1',
      tenantId: 't1',
      tenantSlug: '',
      fullName: 'Ravi Shah',
      roles: ['Member'],
    });

    expect(TestBed.inject(TokenStore).token()).toBe('signed-token');
  });

  it('does not submit an OTP sign-in with no code entered', () => {
    setup();
    switchToOtp();

    component.form.controls.mobileOrEmail.setValue('ravi@example.com');
    component.submit();

    http.expectNone('/v1/identity/otp/login');
  });

  it('shows a wrong-code failure the same way a wrong password shows one', () => {
    setup();
    switchToOtp();

    component.form.controls.mobileOrEmail.setValue('ravi@example.com');
    component.otpCode.set('000000');
    component.submit();

    http.expectOne('/v1/identity/otp/login').flush(
      { title: 'Auth.InvalidCredentials', detail: 'Incorrect mobile/email or password.' },
      { status: 401, statusText: 'Unauthorized' },
    );
    fixture.detectChanges();

    expect(text()).toContain('Incorrect mobile/email or password.');
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
