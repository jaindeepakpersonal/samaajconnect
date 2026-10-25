import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, describeError } from '@samaajconnect/shared';

/**
 * Change your password, extracted from the My Profile screen's own card as of
 * 2026-09-11. It kept its reasoning intact - this is still not a directory
 * field, so it is still its own form and its own request rather than a third
 * field on a Save button - and only its address changed: reachable from the
 * top bar's user menu now, alongside Privacy settings and Sign out, rather
 * than inline on Profile. See apps/member-portal/CLAUDE.md for why the three
 * moved together.
 */
@Component({
  selector: 'app-change-password',
  imports: [FormsModule],
  styleUrl: './change-password.css',
  template: `
    <div class="change-password-page">
      <header class="page-head">
        <h1 class="page-title">Change your password</h1>
        <p class="subtitle">Every other signed-in device is signed out once this takes effect.</p>
      </header>

      <form class="card" (ngSubmit)="changePassword()">
        <label for="current-password">Current password</label>
        <input
          class="input"
          id="current-password"
          name="currentPassword"
          type="password"
          autocomplete="current-password"
          [(ngModel)]="passwordForm.current"
          [attr.aria-invalid]="passwordAttempted() && !passwordForm.current ? 'true' : null"
        />

        <label for="new-password">New password</label>
        <input
          class="input"
          id="new-password"
          name="newPassword"
          type="password"
          autocomplete="new-password"
          [(ngModel)]="passwordForm.next"
          [attr.aria-invalid]="showNewPasswordError() ? 'true' : null"
        />
        <p class="small">At least 10 characters, and different from your current one.</p>
        @if (showNewPasswordError()) {
          <p class="field-error">
            Choose a password of at least 10 characters, different from your current one.
          </p>
        }

        <label for="confirm-password">Confirm new password</label>
        <input
          class="input"
          id="confirm-password"
          name="confirmPassword"
          type="password"
          autocomplete="new-password"
          [(ngModel)]="passwordForm.confirm"
          [attr.aria-invalid]="showConfirmError() ? 'true' : null"
        />
        @if (showConfirmError()) {
          <p class="field-error">The passwords do not match.</p>
        }

        @if (passwordError(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        @if (passwordChanged()) {
          <p class="notice ok" role="status">
            Your password has been changed. Every other signed-in device has been signed out.
          </p>
        }

        <div class="actions">
          <button class="btn" type="submit" [disabled]="changingPassword()">
            {{ changingPassword() ? 'Changing…' : 'Change password' }}
          </button>
        </div>
      </form>
    </div>
  `,
})
export class ChangePasswordComponent {
  private readonly auth = inject(AuthService);

  readonly passwordForm = { current: '', next: '', confirm: '' };
  readonly passwordAttempted = signal(false);
  readonly changingPassword = signal(false);
  readonly passwordChanged = signal(false);
  readonly passwordError = signal<string | null>(null);

  /**
   * A plain method, not a `computed` - `passwordForm`'s fields are ordinary
   * `ngModel`-bound properties rather than signals. A `computed` here would
   * cache against `passwordAttempted()` alone and never see a keystroke.
   */
  showNewPasswordError(): boolean {
    return (
      this.passwordAttempted()
      && (this.passwordForm.next.length < 10 || this.passwordForm.next === this.passwordForm.current)
    );
  }

  showConfirmError(): boolean {
    return this.passwordAttempted() && this.passwordForm.confirm !== this.passwordForm.next;
  }

  changePassword(): void {
    this.passwordAttempted.set(true);
    this.passwordChanged.set(false);
    this.passwordError.set(null);

    if (this.showNewPasswordError() || this.showConfirmError() || !this.passwordForm.current) {
      return;
    }

    this.changingPassword.set(true);

    this.auth.changePassword(this.passwordForm.current, this.passwordForm.next).subscribe({
      next: () => {
        this.changingPassword.set(false);
        this.passwordAttempted.set(false);
        this.passwordChanged.set(true);
        this.passwordForm.current = '';
        this.passwordForm.next = '';
        this.passwordForm.confirm = '';
      },
      error: (failure) => {
        this.changingPassword.set(false);
        this.passwordError.set(describeError(failure));
      },
    });
  }
}
