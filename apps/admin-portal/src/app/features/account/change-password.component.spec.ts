import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { ChangePasswordComponent } from './change-password.component';

describe('ChangePasswordComponent', () => {
  let fixture: ComponentFixture<ChangePasswordComponent>;
  let component: ChangePasswordComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      imports: [ChangePasswordComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(ChangePasswordComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  function fill(overrides: Partial<typeof component.form> = {}): void {
    Object.assign(component.form, {
      current: 'old-password',
      next: 'a-new-long-password',
      confirm: 'a-new-long-password',
      ...overrides,
    });
  }

  it('changes the password and reports every other device was signed out', () => {
    fill();

    component.submit();

    const request = http.expectOne('/v1/identity/me/password');

    expect(request.request.body).toEqual({
      currentPassword: 'old-password',
      newPassword: 'a-new-long-password',
    });

    request.flush({ userId: 'u1', changedAt: '2026-01-01T10:00:00Z' });
    fixture.detectChanges();

    expect(text()).toContain('signed out');
  });

  it('clears the form once the password has actually changed', () => {
    fill();

    component.submit();
    http.expectOne('/v1/identity/me/password').flush({ userId: 'u1', changedAt: '2026-01-01T10:00:00Z' });

    expect(component.form.current).toBe('');
    expect(component.form.next).toBe('');
    expect(component.form.confirm).toBe('');
  });

  it('refuses a new password shorter than ten characters, without calling the server', () => {
    fill({ next: 'short', confirm: 'short' });

    component.submit();
    fixture.detectChanges();

    http.expectNone('/v1/identity/me/password');
    expect(component.showNewPasswordError()).toBe(true);
  });

  it('refuses a new password identical to the current one', () => {
    fill({ next: 'old-password', confirm: 'old-password' });

    component.submit();
    fixture.detectChanges();

    http.expectNone('/v1/identity/me/password');
    expect(component.showNewPasswordError()).toBe(true);
  });

  it('refuses a confirmation that does not match the new password', () => {
    fill({ confirm: 'something-else-long' });

    component.submit();
    fixture.detectChanges();

    http.expectNone('/v1/identity/me/password');
    expect(component.showConfirmError()).toBe(true);
  });

  it('shows the server error a wrong current password produces', () => {
    fill();

    component.submit();

    http.expectOne('/v1/identity/me/password').flush(
      { title: 'Auth.StepUpFailed', detail: 'Your current password is not correct.' },
      { status: 403, statusText: 'Forbidden' },
    );
    fixture.detectChanges();

    expect(component.error()).not.toBeNull();
    expect(component.form.current).toBe('old-password');
  });
});
