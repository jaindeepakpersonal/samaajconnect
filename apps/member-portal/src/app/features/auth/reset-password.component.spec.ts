import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { ResetPasswordComponent } from './reset-password.component';

describe('ResetPasswordComponent', () => {
  let fixture: ComponentFixture<ResetPasswordComponent>;
  let component: ResetPasswordComponent;
  let http: HttpTestingController;
  let router: Router;

  function setup(queryParams: Record<string, string> = {}): void {
    TestBed.configureTestingModule({
      imports: [ResetPasswordComponent],
      providers: [
        provideRouter([{ path: 'login', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: new Map(Object.entries(queryParams)) } },
        },
      ],
    });

    fixture = TestBed.createComponent(ResetPasswordComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);

    fixture.detectChanges();
  }

  afterEach(() => http?.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function fill(overrides: Partial<Record<'mobileOrEmail' | 'code' | 'newPassword' | 'confirmPassword', string>> = {}) {
    component.form.setValue({
      mobileOrEmail: 'ravi@example.com',
      code: '482913',
      newPassword: 'a-new-long-password',
      confirmPassword: 'a-new-long-password',
      ...overrides,
    });
  }

  it('pre-fills the identifier from the query parameter', () => {
    setup({ identifier: 'ravi@example.com' });

    expect(component.form.controls.mobileOrEmail.value).toBe('ravi@example.com');
  });

  it('sends the identifier, code and new password', () => {
    setup();
    fill();
    component.submit();

    const request = http.expectOne('/v1/identity/password-reset/redeem');

    expect(request.request.body).toEqual({
      mobileOrEmail: 'ravi@example.com',
      code: '482913',
      newPassword: 'a-new-long-password',
    });

    request.flush(null);
  });

  it('will not submit with no code', () => {
    setup();
    fill({ code: '' });
    component.submit();

    http.expectNone('/v1/identity/password-reset/redeem');
  });

  it('will not submit a new password shorter than ten characters', () => {
    setup();
    fill({ newPassword: 'short', confirmPassword: 'short' });
    component.submit();
    fixture.detectChanges();

    http.expectNone('/v1/identity/password-reset/redeem');
    expect(text()).toContain('at least 10 characters');
  });

  it('will not submit when the confirmation does not match', () => {
    setup();
    fill({ confirmPassword: 'something-else-long' });
    component.submit();
    fixture.detectChanges();

    http.expectNone('/v1/identity/password-reset/redeem');
    expect(text()).toContain('do not match');
  });

  it('sends the member to sign in afterwards, not to Home', () => {
    setup();
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fill();
    component.submit();

    http.expectOne('/v1/identity/password-reset/redeem').flush(null);

    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { reset: 'true' } });
  });

  it('shows the service refusal as it is written', () => {
    setup();
    fill();
    component.submit();

    http.expectOne('/v1/identity/password-reset/redeem').flush(
      { title: 'Auth.InvalidCredentials', detail: 'That code is not valid.' },
      { status: 401, statusText: 'Unauthorized' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(text()).toContain('That code is not valid.');
  });

  it('stops being busy after a refusal, so the form can be tried again', () => {
    setup();
    fill();
    component.submit();

    expect(component.busy()).toBe(true);

    http
      .expectOne('/v1/identity/password-reset/redeem')
      .flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(component.busy()).toBe(false);
  });
});
