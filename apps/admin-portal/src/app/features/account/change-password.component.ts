import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, describeError } from '@samaajconnect/shared';

/**
 * Change password. No wireframe covers it - the admin wireframe's sign-in
 * screen has no forgot-password link at all, and nothing on this platform ever
 * let any account, including the bootstrap Super Admin, replace a password it
 * already has. Built for the same reason `/privacy` and the Boli/Pathshala
 * detail screens were: the platform genuinely needs it, not because a
 * wireframe drew it.
 *
 * The name in the shell's own top bar links here now, the same way
 * member-portal's user chip links to its own `/profile`.
 */
@Component({
  selector: 'app-change-password',
  imports: [FormsModule],
  template: `
    <h1 class="title">Change your password</h1>
    <p class="sub">Every other signed-in device is signed out once this takes effect.</p>

    <form class="card" (ngSubmit)="submit()">
      <label for="current-password">Current password</label>
      <input
        id="current-password"
        class="input"
        name="currentPassword"
        type="password"
        autocomplete="current-password"
        [(ngModel)]="form.current"
        [attr.aria-invalid]="attempted() && !form.current ? 'true' : null"
      />

      <label for="new-password">New password</label>
      <input
        id="new-password"
        class="input"
        name="newPassword"
        type="password"
        autocomplete="new-password"
        [(ngModel)]="form.next"
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
        id="confirm-password"
        class="input"
        name="confirmPassword"
        type="password"
        autocomplete="new-password"
        [(ngModel)]="form.confirm"
        [attr.aria-invalid]="showConfirmError() ? 'true' : null"
      />
      @if (showConfirmError()) {
        <p class="field-error">The passwords do not match.</p>
      }

      @if (error(); as message) {
        <p class="notice error" role="alert">{{ message }}</p>
      }

      @if (changed()) {
        <p class="notice ok" role="status">
          Your password has been changed. Every other signed-in device has been signed out.
        </p>
      }

      <button class="btn" type="submit" [disabled]="changing()">
        {{ changing() ? 'Changing…' : 'Change password' }}
      </button>
    </form>
  `,
})
export class ChangePasswordComponent {
  private readonly auth = inject(AuthService);

  readonly form = { current: '', next: '', confirm: '' };
  readonly attempted = signal(false);
  readonly changing = signal(false);
  readonly changed = signal(false);
  readonly error = signal<string | null>(null);

  /**
   * A plain method, not a `computed` - `form`'s fields are ordinary
   * `ngModel`-bound properties rather than signals, so a `computed` would
   * cache against `attempted()` alone and never see a keystroke.
   */
  showNewPasswordError(): boolean {
    return this.attempted() && (this.form.next.length < 10 || this.form.next === this.form.current);
  }

  showConfirmError(): boolean {
    return this.attempted() && this.form.confirm !== this.form.next;
  }

  submit(): void {
    this.attempted.set(true);
    this.changed.set(false);
    this.error.set(null);

    if (this.showNewPasswordError() || this.showConfirmError() || !this.form.current) {
      return;
    }

    this.changing.set(true);

    this.auth.changePassword(this.form.current, this.form.next).subscribe({
      next: () => {
        this.changing.set(false);
        this.attempted.set(false);
        this.changed.set(true);
        this.form.current = '';
        this.form.next = '';
        this.form.confirm = '';
      },
      error: (failure) => {
        this.changing.set(false);
        this.error.set(describeError(failure));
      },
    });
  }
}
