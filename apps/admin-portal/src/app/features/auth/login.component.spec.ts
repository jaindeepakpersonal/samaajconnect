import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { API_CONFIG, LoginResult, SignInResult, TokenStore } from '@samaajconnect/shared';
import { AdminLoginComponent } from './login.component';

function signedIn(roles: string[]): SignInResult {
  const session: LoginResult = {
    accessToken: 'signed-token',
    expiresAt: new Date().toISOString(),
    refreshToken: 'refresh-token',
    refreshTokenExpiresAt: new Date().toISOString(),
    userId: 'u1',
    tenantId: 't1',
    tenantSlug: 'mahavir',
    fullName: 'Asha Jain',
    roles,
  };

  return {
    codeRequired: false,
    session,
    challengeToken: null,
    codeSentTo: null,
    challengeExpiresAt: null,
    deviceToken: null,
    deviceTrustedUntil: null,
  };
}

describe('AdminLoginComponent', () => {
  let fixture: ComponentFixture<AdminLoginComponent>;
  let component: AdminLoginComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();

    TestBed.configureTestingModule({
      imports: [AdminLoginComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: new Map() } } },
      ],
    });

    fixture = TestBed.createComponent(AdminLoginComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    answerCaptcha();
  });

  afterEach(() => http.verify());

  function answerCaptcha(): void {
    http.expectOne('/v1/identity/captcha').flush({ captchaId: 'c1', imagePng: 'iVBORw0KGgo=', expiresAt: '' });
    fixture.detectChanges();
  }

  function fill(): void {
    component.form.setValue({ mobileOrEmail: 'admin@example.com', password: 'a-long-password', captchaAnswer: 'ab3cd' });
  }

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  it('asks for the characters in the picture, and will not submit before one has loaded', () => {
    expect(text()).toContain('Type the characters in the picture');
    expect((fixture.nativeElement as HTMLElement).querySelector('img.captcha-img')).not.toBeNull();
  });

  it('moves to the code, then verifies it and lands an administrator on the dashboard', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    fill();
    component.submit();
    http.expectOne('/v1/identity/login').flush({
      codeRequired: true,
      session: null,
      challengeToken: 'challenge-1',
      codeSentTo: 'ad***@example.com',
      challengeExpiresAt: new Date().toISOString(),
      deviceToken: null,
      deviceTrustedUntil: null,
    } satisfies SignInResult);
    fixture.detectChanges();

    expect(text()).toContain('We sent a 6-digit code to ad***@example.com');

    component.code.set('123456');
    component.verify();
    http.expectOne('/v1/identity/login/verify').flush(signedIn(['Member', 'SamaajAdmin']));

    expect(TestBed.inject(TokenStore).token()).toBe('signed-token');
    expect(navigate).toHaveBeenCalledWith('/dashboard');
  });

  it('signs a plain member out again, back at the password with a new picture', () => {
    fill();
    component.submit();
    http.expectOne('/v1/identity/login').flush(signedIn(['Member']));
    http.expectOne('/v1/identity/logout').flush(null);
    answerCaptcha();

    expect(text()).toContain('does not administer anything');
    expect(component.flow.step()).toBe('password');
  });
});
