import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { LookupItem } from '../members/members.models';
import { BusinessFieldsComponent } from './business-fields.component';
import { initialsOf } from '../members/member-lookup';
import { BusinessesApi } from './businesses.api';
import { Business, BusinessFields } from './businesses.models';

/**
 * My Businesses, `/businesses/mine` - the caller's own businesses, active or
 * not, plus a register form. Each tile links to Manage business for editing and
 * product management.
 *
 * **Registering takes the location and a photo too (2026-10-24).** The photo is
 * its own request after the business exists, because the upload endpoint needs
 * the business's id and a file is not a JSON field. A failed upload leaves the
 * business registered and says so - Manage business can add the photo later -
 * rather than reporting the whole registration as failed.
 */
@Component({
  selector: 'app-my-businesses',
  imports: [FormsModule, RouterLink, AuthedImageDirective, BusinessFieldsComponent],
  styleUrl: './businesses.css',
  template: `
    <div class="businesses-page">
      <a class="back" routerLink="/businesses">‹ Back to directory</a>

      <h1 class="page-title">My businesses</h1>

      @if (error(); as message) {
        <p class="notice error" role="alert">{{ message }}</p>
      }

      @if (done(); as message) {
        <p class="notice info" role="status">{{ message }}</p>
      }

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (businesses().length === 0) {
        <p class="notice info" role="status">You have not registered a business yet.</p>
      } @else {
        <ul class="biz-grid" aria-label="Your businesses">
          @for (business of businesses(); track business.id) {
            <li class="card biz-card">
              @if (business.photoUrl; as path) {
                <img class="biz-cover" [scAuthedSrc]="path" alt="" />
              } @else {
                <span class="biz-cover placeholder" aria-hidden="true">{{ initials(business.name) }}</span>
              }

              <div class="biz-body">
                <h2 class="biz-name">{{ business.name }}</h2>
                <p>
                  <span class="pill" [class.warn]="!business.isActive">
                    {{ business.isActive ? 'Active' : 'Inactive' }}
                  </span>
                  <span class="small">
                    {{ business.products.length }} {{ business.products.length === 1 ? 'product' : 'products' }}
                  </span>
                </p>
                <div class="actions">
                  <a class="btn small" [routerLink]="['/businesses/mine', business.id]">
                    Manage<span class="sr-only"> {{ business.name }}</span>
                  </a>
                  <a class="btn small secondary" [routerLink]="['/businesses', business.id]">
                    View public page<span class="sr-only"> of {{ business.name }}</span>
                  </a>
                </div>
              </div>
            </li>
          }
        </ul>
      }

      <div class="card spaced">
        <h2>Register a business</h2>

        <form (ngSubmit)="create(photoInput)">
          <app-business-fields [fields]="form" [categories]="categories()" prefix="new-business" />

          <label for="new-business-photo">Photo (optional)</label>
          <input #photoInput id="new-business-photo" class="input" type="file" name="photo"
            accept="image/jpeg,image/png,image/webp" (change)="choosePhoto($event)" />
          <p class="small">A JPEG, PNG or WebP picture of the shop or what it sells, 2 MB or smaller.</p>

          <div class="actions">
            <button class="btn" type="submit" [disabled]="creating() || !canCreate()">
              {{ creating() ? 'Registering…' : 'Register business' }}
            </button>
          </div>
        </form>
      </div>
    </div>
  `,
})
export class MyBusinessesComponent implements OnInit {
  private readonly api = inject(BusinessesApi);
  private readonly masterData = inject(MasterDataApi);

  readonly businesses = signal<readonly Business[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly categories = signal<readonly LookupItem[]>([]);

  readonly creating = signal(false);

  form: BusinessFields = blankFields();

  /** The picture chosen on the register form, sent once the business exists. */
  photo: File | null = null;

  ngOnInit(): void {
    this.masterData
      .listBusinessCategories()
      .subscribe({ next: (found) => this.categories.set(found), error: () => {} });

    this.load();
  }

  initials(name: string): string {
    return initialsOf(name);
  }

  load(): void {
    this.loading.set(true);

    this.api.mine().subscribe({
      next: (found) => {
        this.businesses.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  choosePhoto(event: Event): void {
    this.photo = (event.target as HTMLInputElement).files?.[0] ?? null;
  }

  canCreate(): boolean {
    return this.form.name.trim().length > 0 && this.form.categoryId.length > 0;
  }

  /** `photoInput` is the file control, cleared with the rest of the form once the business is registered. */
  create(photoInput?: HTMLInputElement): void {
    if (!this.canCreate()) {
      return;
    }

    this.creating.set(true);
    this.error.set(null);
    this.done.set(null);

    const photo = this.photo;
    const name = this.form.name.trim();

    this.api.create(this.form).subscribe({
      next: (created) => {
        this.form = blankFields();
        this.photo = null;

        if (photoInput) {
          photoInput.value = '';
        }

        if (photo === null) {
          this.finish(`${name} is registered.`);
          return;
        }

        this.api.uploadPhoto(created.id, photo).subscribe({
          next: () => this.finish(`${name} is registered, with its photo.`),
          error: (failure: unknown) => {
            this.finish(`${name} is registered.`);
            this.error.set(`The photo was not saved: ${describeError(failure)} Add it from Manage.`);
          },
        });
      },
      error: (failure: unknown) => {
        this.creating.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private finish(message: string): void {
    this.creating.set(false);
    this.done.set(message);
    this.load();
  }
}

function blankFields(): BusinessFields {
  return {
    name: '',
    categoryId: '',
    cityId: null,
    localityId: null,
    addressLine: null,
    description: null,
    contactPhone: null,
    contactEmail: null,
  };
}
