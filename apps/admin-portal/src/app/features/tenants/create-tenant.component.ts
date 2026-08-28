import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { ModuleDescriptor } from '../../core/admin.models';

/**
 * Create Samaaj, from the admin wireframe's `#createtenant` screen.
 *
 * The wireframe previews `parshwanath-samaj.samaajconnect.com` under the slug
 * field. There are no subdomains any more (root `CLAUDE.md` §6), so the hint
 * says what the slug is actually for: it is what a member picks their Samaaj by
 * when registering, and it is permanent.
 *
 * The **Upload Logo** button is not here. There is no file storage on the
 * platform yet, and a button that opens a picker and then does nothing is worse
 * than one that is absent.
 *
 * The module toggles come from `GET /v1/identity/tenants/modules` rather than
 * being listed in this file, so the panel and the backend cannot disagree about
 * which modules exist or what they are called.
 */
@Component({
  selector: 'app-create-tenant',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <a class="back" routerLink="/tenants">‹ Back to Samaaj / Tenants</a>

    <h1 class="title">Create Samaaj</h1>
    <p class="sub">
      The slug must be unique across the platform and cannot be changed once members are using it.
    </p>

    <div class="card modal-card">
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <label for="name">Samaaj name</label>
        <input
          id="name"
          class="input"
          formControlName="name"
          placeholder="e.g. Parshwanath Samaaj"
          [attr.aria-invalid]="showError('name')"
        />
        @if (showError('name')) {
          <p class="field-error">Enter the Samaaj's name.</p>
        }

        <label for="slug">Slug</label>
        <input
          id="slug"
          class="input"
          formControlName="slug"
          placeholder="e.g. parshwanath-samaj"
          [attr.aria-invalid]="showError('slug')"
        />
        @if (showError('slug')) {
          <p class="field-error">
            Lowercase letters, numbers and hyphens only, between 3 and 63 characters.
          </p>
        } @else {
          <p class="field-hint">
            How members find this Samaaj when they register. Permanent once anyone has joined.
          </p>
        }

        <label for="contactPerson">Contact person</label>
        <input id="contactPerson" class="input" formControlName="contactPerson" placeholder="Name" />

        <label for="contactEmail">Contact email</label>
        <input
          id="contactEmail"
          class="input"
          type="email"
          formControlName="contactEmail"
          placeholder="admin@example.com"
          [attr.aria-invalid]="showError('contactEmail')"
        />
        @if (showError('contactEmail')) {
          <p class="field-error">Enter a valid email address, or leave it blank.</p>
        }

        <fieldset class="modules">
          <legend>Enabled modules</legend>
          <p class="small">
            A module switched off makes every screen in it answer “not found” for this Samaaj. All
            of this can be changed later.
          </p>

          @if (modules().length === 0) {
            <p class="muted">Loading modules…</p>
          }

          @for (module of modules(); track module.key) {
            <div class="toggle-row">
              <label [for]="'module-' + module.key">{{ module.label }}</label>
              <input
                type="checkbox"
                [id]="'module-' + module.key"
                [checked]="enabled().includes(module.key)"
                (change)="toggle(module.key)"
              />
            </div>
          }
        </fieldset>

        @if (error(); as message) {
          <p class="notice error" role="alert">{{ message }}</p>
        }

        <p class="notice plain">
          A new Samaaj is created <b>Inactive</b>. Creating it and letting it serve traffic are two
          separate decisions, each with its own audit entry — activate it from the Samaaj list when
          it is ready.
        </p>

        <div class="actions">
          <button class="btn" type="submit" [disabled]="busy()">
            {{ busy() ? 'Creating…' : 'Create Samaaj (Inactive)' }}
          </button>
          <a class="btn alt" routerLink="/tenants">Cancel</a>
        </div>

        <p class="sr-only" role="status">{{ busy() ? 'Creating the Samaaj' : '' }}</p>
      </form>
    </div>
  `,
  styles: `
    .modules {
      border: 1px solid var(--line);
      border-radius: var(--radius-sm);
      padding: var(--space-3) var(--space-4);
      margin: var(--space-4) 0 0;
    }

    .modules legend {
      font-size: 14px;
      font-weight: 600;
      padding: 0 var(--space-2);
    }
  `,
})
export class CreateTenantComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  readonly modules = signal<readonly ModuleDescriptor[]>([]);
  readonly enabled = signal<readonly string[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    slug: [
      '',
      [Validators.required, Validators.pattern(/^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])$/)],
    ],
    contactPerson: [''],
    contactEmail: ['', Validators.email],
  });

  ngOnInit(): void {
    this.api.listModules().subscribe({
      next: (modules) => {
        this.modules.set(modules);

        // The catalogue says which modules a Samaaj normally runs, so the form
        // opens on that rather than on nothing. Boli is off by default because
        // most Samaaj do not run auctions.
        this.enabled.set(modules.filter((m) => m.defaultOn).map((m) => m.key));
      },
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  showError(control: 'name' | 'slug' | 'contactEmail'): boolean {
    const field = this.form.controls[control];

    return field.invalid && (field.dirty || field.touched);
  }

  toggle(key: string): void {
    const current = this.enabled();

    this.enabled.set(current.includes(key) ? current.filter((k) => k !== key) : [...current, key]);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    const raw = this.form.getRawValue();

    this.api
      .createTenant({
        name: raw.name.trim(),
        slug: raw.slug.trim(),
        domain: null,
        contactPerson: blankToNull(raw.contactPerson),
        contactEmail: blankToNull(raw.contactEmail),
        enabledModules: this.enabled(),
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          void this.router.navigate(['/tenants'], { queryParams: { created: 'true' } });
        },
        error: (failure: unknown) => {
          this.busy.set(false);
          this.error.set(describeError(failure));
        },
      });
  }
}

/** An empty optional field is absent, not an empty string. */
function blankToNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed === '' ? null : trimmed;
}
