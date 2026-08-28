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

  function loadDirectory(body: object = directory): void {
    fixture.detectChanges();
    http.expectOne('/v1/identity/tenants/directory').flush(body);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function fillValidForm(): void {
    component.form.setValue({
      fullName: 'Ravi Shah',
      mobileOrEmail: 'ravi@example.com',
      tenantSlug: 'mahavir-samaj',
      password: 'a-long-enough-password',
    });
  }

  it('fills the Samaaj picker from the API rather than the wireframe sample list', () => {
    loadDirectory();

    const options = (fixture.nativeElement as HTMLElement).querySelectorAll('select option');

    // One placeholder plus one real Samaaj - not the prototype's two samples.
    expect(options.length).toBe(2);
    expect(options[1].textContent).toContain('Mahavir Samaaj');
  });

  it('offers a retry when the Samaaj list cannot be loaded', () => {
    fixture.detectChanges();
    http
      .expectOne('/v1/identity/tenants/directory')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });

  it('explains an empty directory instead of showing a blank dropdown', () => {
    loadDirectory([]);

    expect(text()).toContain('No Samaaj is currently accepting registrations');
  });

  it('does not submit an incomplete form', () => {
    loadDirectory();

    component.submit();

    http.expectNone('/v1/identity/register');
  });

  it('rejects a password shorter than the API will accept', () => {
    loadDirectory();

    component.form.setValue({
      fullName: 'Ravi Shah',
      mobileOrEmail: 'ravi@example.com',
      tenantSlug: 'mahavir-samaj',
      password: 'short',
    });

    expect(component.form.controls.password.invalid).toBe(true);
  });

  it('registers and sends the member to sign in', () => {
    loadDirectory();

    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fillValidForm();
    component.submit();

    const request = http.expectOne('/v1/identity/register');
    expect(request.request.body).toEqual({
      fullName: 'Ravi Shah',
      mobileOrEmail: 'ravi@example.com',
      tenantSlug: 'mahavir-samaj',
      password: 'a-long-enough-password',
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
    loadDirectory();
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
    loadDirectory();
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
});
