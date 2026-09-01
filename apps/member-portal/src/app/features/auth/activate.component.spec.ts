import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { ActivateComponent } from './activate.component';

describe('ActivateComponent', () => {
  let fixture: ComponentFixture<ActivateComponent>;
  let component: ActivateComponent;
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ActivateComponent],
      providers: [
        // A stub /login, because two of these navigate for real. Without it the
        // run is green but logs NG04002 for every unmatched navigation.
        provideRouter([{ path: 'login', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(ActivateComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);

    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function fill(overrides: Partial<Record<'mobileOrEmail' | 'code' | 'password', string>> = {}) {
    component.form.setValue({
      mobileOrEmail: 'ravi@example.com',
      code: 'ABCD-2345',
      password: 'a-long-enough-password',
      ...overrides,
    });
  }

  it('sends the code and the chosen password', () => {
    fill();

    component.submit();

    const request = http.expectOne('/v1/identity/activations/redeem');

    expect(request.request.body).toEqual({
      mobileOrEmail: 'ravi@example.com',
      code: 'ABCD-2345',
      password: 'a-long-enough-password',
    });

    request.flush({ userId: 'u1', tenantSlug: 'mahavir-samaj', fullName: 'Ravi Shah' });
  });

  it('trims the identifier and the code, because both get read off paper', () => {
    fill({ mobileOrEmail: '  ravi@example.com ', code: ' ABCD-2345 ' });

    component.submit();

    const request = http.expectOne('/v1/identity/activations/redeem');

    expect(request.request.body.mobileOrEmail).toBe('ravi@example.com');
    expect(request.request.body.code).toBe('ABCD-2345');

    request.flush({ userId: 'u1', tenantSlug: 'mahavir-samaj', fullName: 'Ravi Shah' });
  });

  it('sends the member to sign in afterwards, not to Home', () => {
    // Redeeming sets a password; it does not issue a token. Navigating to Home
    // would land on the guard and bounce straight back with "session ended".
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fill();
    component.submit();

    http
      .expectOne('/v1/identity/activations/redeem')
      .flush({ userId: 'u1', tenantSlug: 'mahavir-samaj', fullName: 'Ravi Shah' });

    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { activated: 'true' } });
  });

  it('will not send a password shorter than the service accepts', () => {
    // A round trip that comes back 400 for a rule the form already knows is a
    // round trip the person did not need to wait for.
    fill({ password: 'short' });

    component.submit();
    fixture.detectChanges();

    http.expectNone('/v1/identity/activations/redeem');
    expect(text()).toContain('at least 10 characters');
  });

  it('will not send an empty form', () => {
    component.submit();

    http.expectNone('/v1/identity/activations/redeem');
  });

  it('shows the service refusal as it is written', () => {
    // One message for every failure, by design: distinguishing "no such
    // account" from "wrong code" would let somebody with a list of identifiers
    // work out which are mid-conversion.
    fill();
    component.submit();

    http.expectOne('/v1/identity/activations/redeem').flush(
      {
        title: 'Activation.Invalid',
        detail: 'That activation code is not valid. Ask your Samaaj administrator for a new one.',
        status: 403,
      },
      { status: 403, statusText: 'Forbidden' },
    );

    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(text()).toContain('not valid');
  });

  it('stops being busy after a refusal, so the form can be tried again', () => {
    fill();
    component.submit();

    expect(component.busy()).toBe(true);

    http
      .expectOne('/v1/identity/activations/redeem')
      .flush({}, { status: 403, statusText: 'Forbidden' });

    expect(component.busy()).toBe(false);
  });

  it('says a code is single-use and expires, so a stale one is explicable', () => {
    expect(text()).toContain('only be used once');
  });
});
