import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { ForgotPasswordComponent } from './forgot-password.component';

describe('ForgotPasswordComponent', () => {
  let fixture: ComponentFixture<ForgotPasswordComponent>;
  let component: ForgotPasswordComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [
        provideRouter([{ path: 'reset', children: [] }, { path: 'login', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(ForgotPasswordComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  it('sends the identifier', () => {
    component.form.setValue({ mobileOrEmail: 'ravi@example.com' });
    component.submit();

    const request = http.expectOne('/v1/identity/password-reset/request');

    expect(request.request.body).toEqual({ mobileOrEmail: 'ravi@example.com' });

    request.flush(null);
  });

  it('will not send an empty form', () => {
    component.submit();

    http.expectNone('/v1/identity/password-reset/request');
  });

  it('shows the identical confirmation whether or not the account exists', () => {
    component.form.setValue({ mobileOrEmail: 'ravi@example.com' });
    component.submit();

    http.expectOne('/v1/identity/password-reset/request').flush(null);
    fixture.detectChanges();

    expect(text()).toContain('If that identifier has an account');
  });

  it('offers to continue to the code screen once a code has been requested', () => {
    component.form.setValue({ mobileOrEmail: 'ravi@example.com' });
    component.submit();

    http.expectOne('/v1/identity/password-reset/request').flush(null);
    fixture.detectChanges();

    const link = (fixture.nativeElement as HTMLElement).querySelector('a[href^="/reset"]');

    expect(link).not.toBeNull();
    expect(link?.getAttribute('href')).toContain('identifier=ravi@example.com');
  });

  it('shows the same confirmation even if the request itself fails', () => {
    // Anti-enumeration has to hold even against a server error - anything
    // this screen shows differently for a failure would be a signal too.
    component.form.setValue({ mobileOrEmail: 'ravi@example.com' });
    component.submit();

    http
      .expectOne('/v1/identity/password-reset/request')
      .flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(text()).toContain('If that identifier has an account');
  });
});
