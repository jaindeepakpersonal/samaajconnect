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

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function fillPasswordForm(overrides: Partial<typeof component.passwordForm> = {}): void {
    Object.assign(component.passwordForm, {
      current: 'old-password',
      next: 'a-new-long-password',
      confirm: 'a-new-long-password',
      ...overrides,
    });
  }

  it('changes the password and reports every other device was signed out', () => {
    fillPasswordForm();

    component.changePassword();

    const request = http.expectOne('/v1/identity/me/password');

    expect(request.request.body).toEqual({
      currentPassword: 'old-password',
      newPassword: 'a-new-long-password',
    });

    request.flush({ userId: 'm1', changedAt: '2026-01-01T10:00:00Z' });
    fixture.detectChanges();

    expect(text()).toContain('signed out');
  });

  it('clears the form once the password has actually changed', () => {
    fillPasswordForm();

    component.changePassword();
    http.expectOne('/v1/identity/me/password').flush({ userId: 'm1', changedAt: '2026-01-01T10:00:00Z' });

    expect(component.passwordForm.current).toBe('');
    expect(component.passwordForm.next).toBe('');
    expect(component.passwordForm.confirm).toBe('');
  });

  it('refuses a new password shorter than ten characters, without calling the server', () => {
    fillPasswordForm({ next: 'short', confirm: 'short' });

    component.changePassword();
    fixture.detectChanges();

    http.expectNone('/v1/identity/me/password');
    expect(component.showNewPasswordError()).toBe(true);
  });

  it('refuses a new password identical to the current one', () => {
    fillPasswordForm({ next: 'old-password', confirm: 'old-password' });

    component.changePassword();
    fixture.detectChanges();

    http.expectNone('/v1/identity/me/password');
    expect(component.showNewPasswordError()).toBe(true);
  });

  it('refuses a confirmation that does not match the new password', () => {
    fillPasswordForm({ confirm: 'something-else-long' });

    component.changePassword();
    fixture.detectChanges();

    http.expectNone('/v1/identity/me/password');
    expect(component.showConfirmError()).toBe(true);
  });

  it('shows the server error a wrong current password produces', () => {
    fillPasswordForm();

    component.changePassword();

    http.expectOne('/v1/identity/me/password').flush(
      { title: 'Auth.StepUpFailed', detail: 'Your current password is not correct.' },
      { status: 403, statusText: 'Forbidden' },
    );
    fixture.detectChanges();

    expect(component.passwordError()).not.toBeNull();
    expect(component.passwordForm.current).toBe('old-password');
  });
});
