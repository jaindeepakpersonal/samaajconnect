import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  AuthService,
  ConsentNotice,
  TenantSummary,
  describeError,
  fieldErrors,
} from '@samaajconnect/shared';

/**
 * Registration, from the member-portal wireframe's `#register` screen.
 *
 * The wireframe's "Select Samaaj" dropdown had no endpoint behind it, so
 * `GET /v1/identity/tenants/directory` was added to the identity service for
 * it rather than hardcoding the prototype's two sample Samaaj.
 *
 * The consent block is not in the wireframe at all. It is there because the
 * DPDP Act requires the notice at or before consent, consent to be specific
 * per purpose, and — crucially — that it be affirmative: **no box is ticked
 * for the visitor**, including the required ones. A pre-ticked box is not
 * consent. See docs/product/DPDP-COMPLIANCE.md.
 *
 * The wireframe continues to "Verify Mobile". OTP verification is deferred
 * until there is a channel to send a code through, so the flow ends at sign-in
 * and says so plainly instead of leading to a dead screen.
 */
@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  styleUrl: './auth.css',
  template: `
    <div class="auth-wrap">
      <div class="logo">samaajconnect</div>

      <div class="auth-card">
        <h1 class="auth-heading">Register</h1>
        <p class="subtitle">A member can join only one Samaaj.</p>

        @if (loadingSamaaj()) {
          <p class="small" role="status">Loading the list of Samaaj…</p>
        } @else if (samaajError()) {
          <p class="notice error" role="alert">
            {{ samaajError() }}
            <button class="btn link" type="button" (click)="loadSamaaj()">Try again</button>
          </p>
        } @else if (samaaj().length === 0) {
          <p class="notice info" role="status">
            No Samaaj is currently accepting registrations. Please check back shortly.
          </p>
        }

        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <label for="fullName">Full Name</label>
          <input
            id="fullName"
            class="input"
            formControlName="fullName"
            autocomplete="name"
            placeholder="Member name"
            [attr.aria-invalid]="showError('fullName')"
          />
          @if (showError('fullName')) {
            <p class="field-error">Enter your full name.</p>
          }

          <label for="mobileOrEmail">Mobile / Email</label>
          <input
            id="mobileOrEmail"
            class="input"
            formControlName="mobileOrEmail"
            autocomplete="username"
            [attr.aria-invalid]="showError('mobileOrEmail')"
          />
          @if (showError('mobileOrEmail')) {
            <p class="field-error">Enter a mobile number or email address.</p>
          }
          @for (message of serverErrorsFor('MobileOrEmail'); track message) {
            <p class="field-error">{{ message }}</p>
          }

          <label for="tenantSlug">Select Samaaj</label>
          <select id="tenantSlug" class="input" formControlName="tenantSlug">
            <option value="">Choose your Samaaj</option>
            @for (option of samaaj(); track option.slug) {
              <option [value]="option.slug">{{ option.name }}</option>
            }
          </select>
          @if (showError('tenantSlug')) {
            <p class="field-error">Choose the Samaaj you belong to.</p>
          }

          <label for="password">Password</label>
          <input
            id="password"
            class="input"
            type="password"
            formControlName="password"
            autocomplete="new-password"
            [attr.aria-invalid]="showError('password')"
          />
          <p class="small">At least 10 characters.</p>
          @if (showError('password')) {
            <p class="field-error">Choose a password of at least 10 characters.</p>
          }
          @for (message of serverErrorsFor('Password'); track message) {
            <p class="field-error">{{ message }}</p>
          }

          @if (notice(); as consent) {
            <fieldset class="consent">
              <legend>How your information is used</legend>

              @for (item of consent.items; track item.purpose) {
                <label class="consent-item">
                  <input
                    type="checkbox"
                    [checked]="hasAgreed(item.purpose)"
                    (change)="toggleAgreement(item.purpose)"
                  />
                  <span>
                    <strong>{{ item.title }}</strong>
                    @if (item.required) {
                      <span class="pill">Required</span>
                    }
                    <span class="small consent-detail">{{ item.description }}</span>
                  </span>
                </label>
              }

              @if (showConsentError()) {
                <p class="field-error">
                  Please agree to the required items to hold an account.
                </p>
              }

              <p class="small">You can change the optional ones at any time.</p>
            </fieldset>
          } @else if (noticeError(); as message) {
            <p class="notice error" role="alert">
              {{ message }}
              <button class="btn link" type="button" (click)="loadNotice()">Try again</button>
            </p>
          }

          @if (error(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          <div class="actions">
            <button class="btn" type="submit" [disabled]="busy() || !canSubmit()">
              {{ busy() ? 'Creating your account…' : 'Register' }}
            </button>
            <a class="btn secondary" routerLink="/login">Back to login</a>
          </div>
        </form>
      </div>
    </div>
  `,
})
export class RegisterComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  readonly samaaj = signal<readonly TenantSummary[]>([]);
  readonly loadingSamaaj = signal(false);
  readonly samaajError = signal<string | null>(null);

  readonly notice = signal<ConsentNotice | null>(null);
  readonly noticeError = signal<string | null>(null);

  /** Purposes the visitor has actively ticked. Starts empty, always. */
  readonly agreed = signal<readonly string[]>([]);

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly serverErrors = signal<Record<string, string[]>>({});
  readonly attempted = signal(false);

  readonly form = this.formBuilder.nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    mobileOrEmail: ['', Validators.required],
    tenantSlug: ['', Validators.required],
    password: ['', [Validators.required, Validators.minLength(10)]],
  });

  /** True once every purpose the notice marks required has been ticked. */
  readonly requiredConsentGiven = computed(() => {
    const items = this.notice()?.items ?? [];

    return items
      .filter((item) => item.required)
      .every((item) => this.agreed().includes(item.purpose));
  });

  readonly canSubmit = computed(() => this.samaaj().length > 0 && this.notice() !== null);

  readonly showConsentError = computed(() => this.attempted() && !this.requiredConsentGiven());

  ngOnInit(): void {
    this.loadSamaaj();
    this.loadNotice();
  }

  loadSamaaj(): void {
    this.loadingSamaaj.set(true);
    this.samaajError.set(null);

    this.http.get<TenantSummary[]>('/v1/identity/tenants/directory').subscribe({
      next: (found) => {
        this.samaaj.set(found);
        this.loadingSamaaj.set(false);
      },
      error: (failure: unknown) => {
        this.loadingSamaaj.set(false);
        this.samaajError.set(describeError(failure));
      },
    });
  }

  loadNotice(): void {
    this.noticeError.set(null);

    this.auth.consentNotice().subscribe({
      next: (found) => this.notice.set(found),
      error: (failure: unknown) => this.noticeError.set(describeError(failure)),
    });
  }

  hasAgreed(purpose: string): boolean {
    return this.agreed().includes(purpose);
  }

  toggleAgreement(purpose: string): void {
    this.agreed.update((current) =>
      current.includes(purpose)
        ? current.filter((agreed) => agreed !== purpose)
        : [...current, purpose],
    );
  }

  showError(control: 'fullName' | 'mobileOrEmail' | 'tenantSlug' | 'password'): boolean {
    const field = this.form.controls[control];

    return field.invalid && (field.dirty || field.touched);
  }

  /** Validation messages the API returned, keyed by its own field names. */
  serverErrorsFor(field: string): string[] {
    return this.serverErrors()[field] ?? [];
  }

  submit(): void {
    this.attempted.set(true);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const notice = this.notice();

    // Submitting without a notice would produce a consent record that cannot
    // say what the person was shown, which is the thing DPDP s.6(7) is about.
    if (!notice || !this.requiredConsentGiven()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.serverErrors.set({});

    this.auth
      .register({
        ...this.form.getRawValue(),
        consentedPurposes: this.agreed(),
        noticeVersion: notice.version,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);

          void this.router.navigate(['/login'], {
            queryParams: { registered: true },
          });
        },
        error: (failure: unknown) => {
          this.busy.set(false);
          this.serverErrors.set(fieldErrors(failure));
          this.error.set(describeError(failure));
        },
      });
  }
}
