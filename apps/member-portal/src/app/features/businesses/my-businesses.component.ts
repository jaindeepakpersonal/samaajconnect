import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { Locality, LookupItem } from '../members/members.models';
import { BusinessesApi } from './businesses.api';
import { Business, BusinessFields } from './businesses.models';

/**
 * My Businesses, `/businesses/mine` - the caller's own businesses, active or
 * not, plus a create form. Each row links to Manage business for editing and
 * product management.
 */
@Component({
  selector: 'app-my-businesses',
  imports: [FormsModule, RouterLink],
  styleUrl: './businesses.css',
  template: `
    <div class="businesses-page">
      <a class="back" routerLink="/businesses">‹ Back to directory</a>

      <h1 class="page-title">My businesses</h1>

      @if (error(); as message) {
        <p class="notice error" role="alert">{{ message }}</p>
      }

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (businesses().length === 0) {
        <p class="notice info" role="status">You have not registered a business yet.</p>
      } @else {
        <div class="queue">
          @for (business of businesses(); track business.id) {
            <div class="card">
              <p>
                <b>{{ business.name }}</b>
                <span class="pill" [class.warn]="!business.isActive">
                  {{ business.isActive ? 'Active' : 'Inactive' }}
                </span>
              </p>
              <div class="actions">
                <a class="btn small" [routerLink]="['/businesses/mine', business.id]">Manage</a>
                <a class="btn small secondary" [routerLink]="['/businesses', business.id]">View public page</a>
              </div>
            </div>
          }
        </div>
      }

      <div class="card spaced">
        <h2>Register a business</h2>

        <form (ngSubmit)="create()">
          <div class="form-grid">
            <div class="field">
              <label for="name">Name</label>
              <input class="input" id="name" name="name" maxlength="200" [(ngModel)]="form.name" />
            </div>

            <div class="field">
              <label for="categoryId">Category</label>
              <select class="input" id="categoryId" name="categoryId" [(ngModel)]="form.categoryId">
                <option value="">Choose a category…</option>
                @for (option of categories(); track option.id) {
                  <option [value]="option.id">{{ option.label }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="localityId">Locality</label>
              <select class="input" id="localityId" name="localityId" [(ngModel)]="form.localityId">
                <option [ngValue]="null">Not specified</option>
                @for (option of localities(); track option.id) {
                  <option [value]="option.id">{{ option.name }}</option>
                }
              </select>
            </div>

            <div class="field">
              <label for="contactPhone">Contact phone</label>
              <input class="input" id="contactPhone" name="contactPhone" maxlength="20" [(ngModel)]="form.contactPhone" />
            </div>

            <div class="field">
              <label for="contactEmail">Contact email</label>
              <input class="input" id="contactEmail" name="contactEmail" maxlength="200" [(ngModel)]="form.contactEmail" />
            </div>

            <div class="field span-all">
              <label for="description">Description</label>
              <textarea
                class="input"
                id="description"
                name="description"
                rows="3"
                maxlength="1000"
                [(ngModel)]="form.description"
              ></textarea>
            </div>

            <div class="actions span-all">
              <button class="btn" type="submit" [disabled]="creating() || !canCreate()">
                {{ creating() ? 'Registering…' : 'Register business' }}
              </button>
            </div>
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

  readonly categories = signal<readonly LookupItem[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  readonly creating = signal(false);

  form: BusinessFields = {
    name: '',
    categoryId: '',
    cityId: null,
    localityId: null,
    description: null,
    contactPhone: null,
    contactEmail: null,
  };

  ngOnInit(): void {
    this.masterData
      .listBusinessCategories()
      .subscribe({ next: (found) => this.categories.set(found), error: () => {} });
    this.masterData.listLocalities().subscribe({ next: (found) => this.localities.set(found), error: () => {} });

    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

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

  canCreate(): boolean {
    return this.form.name.trim().length > 0 && this.form.categoryId.length > 0;
  }

  create(): void {
    if (!this.canCreate()) {
      return;
    }

    this.creating.set(true);
    this.error.set(null);

    this.api.create(this.form).subscribe({
      next: () => {
        this.creating.set(false);
        this.form = {
          name: '',
          categoryId: '',
          cityId: null,
          localityId: null,
          description: null,
          contactPhone: null,
          contactEmail: null,
        };
        this.load();
      },
      error: (failure: unknown) => {
        this.creating.set(false);
        this.error.set(describeError(failure));
      },
    });
  }
}
