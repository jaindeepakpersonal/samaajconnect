import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
/** A mutable draft of SecurityPolicy - ngModel needs to write into it. */
interface SecurityPolicyDraft {
  minPasswordLength: number | null;
  otpLifetimeMinutes: number | null;
  maxFailedLoginAttempts: number | null;
  lockoutDurationMinutes: number | null;
  refreshTokenLifetimeDays: number | null;
}

/**
 * Settings, from the admin wireframe's `#settings` screen. Four cards, and
 * three different outcomes.
 *
 * Module Controls and the logo/contact half of Tenant Branding are already
 * built - they live on the Samaaj/Tenants screen, and the wireframe's own
 * "Module Controls" button already navigates there rather than to a second
 * copy. "Colors" is not built: nothing in either Angular app has a per-tenant
 * theming system to apply one to, and storing a colour nothing renders would
 * be exactly the half-finished feature this platform's conventions warn
 * against.
 *
 * Security is the one genuinely new capability here - a Samaaj's own
 * overrides to five platform-wide constants (password length, OTP lifetime,
 * lockout threshold and duration, session length), each independently
 * clearable back to the platform default.
 *
 * Notification Providers stays unbuilt, honestly: there is no real email,
 * SMS or WhatsApp provider anywhere on this platform, only the logging
 * stand-in, and a "Configure" control for a provider that does not exist
 * would be a screen that promises something untrue.
 */
@Component({
  selector: 'app-settings',
  imports: [FormsModule, RouterLink],
  template: `
    <h1 class="title">Settings</h1>
    <p class="sub">Platform and tenant configuration.</p>

    <div class="grid2">
      <div class="card">
        <h2>Tenant Branding</h2>
        <p class="muted">Logo and contact details live on the Samaaj screen. Colour theming is not built.</p>
        <a class="btn" routerLink="/tenants">Go to Samaaj</a>
      </div>

      <div class="card">
        <h2>Module Controls</h2>
        <p class="muted">Enable or disable modules per Samaaj, from the Samaaj screen.</p>
        <a class="btn" routerLink="/tenants">Go to Samaaj</a>
      </div>

      <div class="card">
        <h2>Security</h2>
        <p class="muted">Password, OTP, session and lockout policy for this Samaaj.</p>

        @if (needsSamaaj()) {
          <p class="notice">Choose a Samaaj in the top bar to configure its security policy.</p>
        } @else {
          @if (loading()) {
            <p class="muted">Loading…</p>
          } @else {
            <label for="minPasswordLength">Minimum password length</label>
            <input
              class="input"
              id="minPasswordLength"
              type="number"
              min="10"
              max="64"
              placeholder="Platform default: 10"
              [(ngModel)]="policy.minPasswordLength"
              [ngModelOptions]="{ standalone: true }"
            />

            <label for="otpLifetimeMinutes">One-time code lifetime (minutes)</label>
            <input
              class="input"
              id="otpLifetimeMinutes"
              type="number"
              min="5"
              max="30"
              placeholder="Platform default: 10"
              [(ngModel)]="policy.otpLifetimeMinutes"
              [ngModelOptions]="{ standalone: true }"
            />

            <label for="maxFailedLoginAttempts">Failed attempts before lockout</label>
            <input
              class="input"
              id="maxFailedLoginAttempts"
              type="number"
              min="3"
              max="10"
              placeholder="Platform default: 5"
              [(ngModel)]="policy.maxFailedLoginAttempts"
              [ngModelOptions]="{ standalone: true }"
            />

            <label for="lockoutDurationMinutes">Lockout duration (minutes)</label>
            <input
              class="input"
              id="lockoutDurationMinutes"
              type="number"
              min="5"
              max="1440"
              placeholder="Platform default: 15"
              [(ngModel)]="policy.lockoutDurationMinutes"
              [ngModelOptions]="{ standalone: true }"
            />

            <label for="refreshTokenLifetimeDays">Session length (days)</label>
            <input
              class="input"
              id="refreshTokenLifetimeDays"
              type="number"
              min="1"
              max="90"
              placeholder="Platform default: 14"
              [(ngModel)]="policy.refreshTokenLifetimeDays"
              [ngModelOptions]="{ standalone: true }"
            />

            <p class="small">Leave a field blank to use the platform default.</p>

            @if (error()) {
              <p class="small" role="alert">{{ error() }}</p>
            }

            <div class="actions">
              <button class="btn" type="button" [disabled]="saving()" (click)="save()">
                {{ saving() ? 'Saving…' : 'Save' }}
              </button>

              @if (saved()) {
                <span class="small" role="status">Saved.</span>
              }
            </div>
          }
        }
      </div>

      <div class="card">
        <h2>Notification Providers</h2>
        <p class="muted">
          Email/SMS/WhatsApp adapters. Not built yet — this needs a real provider account and
          secret storage this platform does not have.
        </p>
      </div>
    </div>
  `,
})
export class SettingsComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly scope = inject(AdminScope);

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly saved = signal(false);
  readonly error = signal<string | null>(null);

  policy: SecurityPolicyDraft = {
    minPasswordLength: null,
    otpLifetimeMinutes: null,
    maxFailedLoginAttempts: null,
    lockoutDurationMinutes: null,
    refreshTokenLifetimeDays: null,
  };

  readonly isSuperAdmin = computed(() => this.auth.roles().includes('SuperAdmin'));

  /**
   * A Super Admin acts on whichever Samaaj they have selected; a Samaaj
   * Admin's own tenant is on their token and never offered as a choice, so
   * their own id - not the platform sentinel `auth.user()` carries for a
   * Super Admin - is what this screen acts on instead.
   */
  readonly actingTenantId = computed(() =>
    this.isSuperAdmin() ? this.scope.tenantId() : (this.auth.user()?.tenantId ?? null),
  );

  readonly needsSamaaj = computed(() => this.isSuperAdmin() && this.actingTenantId() === null);

  ngOnInit(): void {
    const tenantId = this.actingTenantId();

    if (tenantId === null) {
      return;
    }

    this.loading.set(true);

    this.api.securityPolicy(tenantId).subscribe({
      next: (policy) => {
        this.policy = { ...policy };
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  save(): void {
    const tenantId = this.actingTenantId();

    if (tenantId === null) {
      return;
    }

    this.saving.set(true);
    this.saved.set(false);
    this.error.set(null);

    this.api.setSecurityPolicy(tenantId, this.policy).subscribe({
      next: (policy) => {
        this.policy = { ...policy };
        this.saving.set(false);
        this.saved.set(true);
      },
      error: () => {
        this.saving.set(false);
        this.error.set('Could not save. Check each value is within its allowed range.');
      },
    });
  }
}
