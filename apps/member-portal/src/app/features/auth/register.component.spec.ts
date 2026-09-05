import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { RegisterComponent } from './register.component';

describe('RegisterComponent', () => {
  let fixture: ComponentFixture<RegisterComponent>;
  let component: RegisterComponent;
  let http: HttpTestingController;

  const directory = [
    {
      id: 't1',
      name: 'Mahavir Samaaj',
      slug: 'mahavir-samaj',
      logoUrl: null,
      status: 'Active',
      enabledModules: [],
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([{ path: 'login', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(RegisterComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const notice = {
    version: '2026-08-28.1',
    items: [
      { purpose: 'Membership', title: 'Your membership', description: '...', required: true },
      { purpose: 'Communications', title: 'Samaaj communications', description: '...', required: false },
    ],
  };

  /** Answers the two calls the screen makes on load. */
  function loadScreen(body: object = directory, noticeBody: object = notice): void {
    fixture.detectChanges();
    http.expectOne('/v1/identity/tenants/directory').flush(body);
    http.expectOne('/v1/identity/consent-notice').flush(noticeBody);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function fillValidForm(): void {
    component.form.setValue({
      fullName: "Ravi Shah",
      mobileOrEmail: "ravi@example.com",
      tenantSlug: "mahavir-samaj",
      password: "a-long-enough-password",
    });

    // Nothing is ticked for the visitor, so a valid submission has to tick
    // the required purpose explicitly.
    component.toggleAgreement("Membership");
  }

  it('fills the Samaaj picker from the API rather than the wireframe sample list', () => {
    loadScreen();

    const options = (fixture.nativeElement as HTMLElement).querySelectorAll('select option');

    // One placeholder plus one real Samaaj - not the prototype's two samples.
    expect(options.length).toBe(2);
    expect(options[1]!.textContent).toContain('Mahavir Samaaj');
  });

  it("offers a retry when the Samaaj list cannot be loaded", () => {
    fixture.detectChanges();
    http
      .expectOne("/v1/identity/tenants/directory")
      .flush({}, { status: 503, statusText: "Unavailable" });
    http.expectOne("/v1/identity/consent-notice").flush(notice);
    fixture.detectChanges();

    expect(text()).toContain("Try again");
  });

  it('explains an empty directory instead of showing a blank dropdown', () => {
    loadScreen([]);

    expect(text()).toContain('No Samaaj is currently accepting registrations');
  });

  it('does not submit an incomplete form', () => {
    loadScreen();

    component.submit();

    http.expectNone('/v1/identity/register');
  });

  it('rejects a password shorter than the API will accept', () => {
    loadScreen();

    component.form.setValue({
      fullName: 'Ravi Shah',
      mobileOrEmail: 'ravi@example.com',
      tenantSlug: 'mahavir-samaj',
      password: 'short',
    });

    expect(component.form.controls.password.invalid).toBe(true);
  });

  it('registers and sends the member to sign in', () => {
    loadScreen();

    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fillValidForm();
    component.submit();

    const request = http.expectOne('/v1/identity/register');
    expect(request.request.body).toEqual({
      fullName: "Ravi Shah",
      mobileOrEmail: "ravi@example.com",
      tenantSlug: "mahavir-samaj",
      password: "a-long-enough-password",
      consentedPurposes: ["Membership"],
      noticeVersion: "2026-08-28.1",
    });

    request.flush({
      userId: 'u1',
      tenantId: 't1',
      tenantSlug: 'mahavir-samaj',
      mobileOrEmail: 'ravi@example.com',
      isContactVerified: false,
    });

    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { registered: true } });
  });

  it('surfaces the field-level messages the API returned', () => {
    loadScreen();
    fillValidForm();
    component.submit();

    http.expectOne('/v1/identity/register').flush(
      { errors: { Password: ['Password must be at least 10 characters.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(component.serverErrorsFor('Password')).toContain(
      'Password must be at least 10 characters.',
    );
    expect(text()).toContain('Password must be at least 10 characters.');
  });

  it('shows the conflict message when the identifier is already registered', () => {
    loadScreen();
    fillValidForm();
    component.submit();

    http.expectOne('/v1/identity/register').flush(
      {
        title: 'User.IdentifierTaken',
        detail: 'That mobile number or email is already registered.',
      },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(text()).toContain('already registered');
  });

  it('leaves every consent box unticked, including the required one', () => {
    loadScreen();

    // A pre-ticked box is not consent. DPDP requires it to be affirmative.
    const boxes = (fixture.nativeElement as HTMLElement)
      .querySelectorAll<HTMLInputElement>('.consent input[type="checkbox"]');

    expect(boxes.length).toBe(2);
    expect(Array.from(boxes).every((box) => !box.checked)).toBe(true);
    expect(component.agreed()).toEqual([]);
  });

  it('will not submit until the required consent is ticked', () => {
    loadScreen();

    component.form.setValue({
      fullName: 'Ravi Shah',
      mobileOrEmail: 'ravi@example.com',
      tenantSlug: 'mahavir-samaj',
      password: 'a-long-enough-password',
    });

    component.submit();

    http.expectNone('/v1/identity/register');
    expect(component.showConsentError()).toBe(true);
  });

  it('sends only the purposes actually ticked', () => {
    loadScreen();
    fillValidForm();
    component.toggleAgreement('Communications');

    component.submit();

    const body = http.expectOne('/v1/identity/register').request.body as {
      consentedPurposes: string[];
    };

    expect(body.consentedPurposes.sort()).toEqual(['Communications', 'Membership']);
  });

  it('unticking removes the purpose again', () => {
    loadScreen();

    component.toggleAgreement('Communications');
    component.toggleAgreement('Communications');

    expect(component.agreed()).toEqual([]);
  });

  it('marks which purposes the notice says are required', () => {
    loadScreen();

    expect(text()).toContain('Required');
    expect(text()).toContain('Your membership');
  });

  it('cannot be submitted at all when the notice could not be loaded', () => {
    fixture.detectChanges();
    http.expectOne('/v1/identity/tenants/directory').flush(directory);
    http
      .expectOne('/v1/identity/consent-notice')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    // Registering without a notice would produce a consent record that cannot
    // say what the person was shown.
    expect(component.canSubmit()).toBe(false);
    expect(text()).toContain('Try again');
  });

  // ---- The chosen Samaaj's logo -------------------------------------------
  //
  // A plain <img src>, because this is the one image on the platform served
  // without a token - and this screen is why: nobody registering has one yet.

  it('shows nothing until a Samaaj is chosen', () => {
    loadScreen();

    expect(fixture.nativeElement.querySelector('.samaaj-logo')).toBeNull();
  });

  it("shows the chosen Samaaj's logo so somebody can see they picked the right one", () => {
    loadScreen([{ ...directory[0], logoUrl: '/v1/identity/tenants/t1/logo' }]);

    component.form.controls.tenantSlug.setValue('mahavir-samaj');
    fixture.detectChanges();

    const logo: HTMLImageElement = fixture.nativeElement.querySelector('.samaaj-logo');

    expect(logo).not.toBeNull();
    expect(logo.getAttribute('src')).toBe('/v1/identity/tenants/t1/logo');
  });

  it('draws no logo for a Samaaj that has none', () => {
    loadScreen();

    component.form.controls.tenantSlug.setValue('mahavir-samaj');
    fixture.detectChanges();

    expect(component.chosen()?.name).toBe('Mahavir Samaaj');
    expect(fixture.nativeElement.querySelector('.samaaj-logo')).toBeNull();
  });
});
