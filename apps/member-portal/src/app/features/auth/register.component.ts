import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  AuthService,
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
 * The wireframe continues to "Verify Mobile". OTP verification is deferred
 * until audit-notification-service can actually send a code, so the flow ends
 * at sign-in and says so plainly instead of leading to a dead screen.
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

          @if (error(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          <div class="actions">
            <button class="btn" type="submit" [disabled]="busy() || samaaj().length === 0">
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
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly serverErrors = signal<Record<string, string[]>>({});

  readonly form = this.formBuilder.nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(200)]],
    mobileOrEmail: ['', Validators.required],
    tenantSlug: ['', Validators.required],
    password: ['', [Validators.required, Validators.minLength(10)]],
  });

  ngOnInit(): void {
    this.loadSamaaj();
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

  showError(control: 'fullName' | 'mobileOrEmail' | 'tenantSlug' | 'password'): boolean {
    const field = this.form.controls[control];

    return field.invalid && (field.dirty || field.touched);
  }

  /** Validation messages the API returned, keyed by its own field names. */
  serverErrorsFor(field: string): string[] {
    return this.serverErrors()[field] ?? [];
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.serverErrors.set({});

    this.auth.register(this.form.getRawValue()).subscribe({
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
