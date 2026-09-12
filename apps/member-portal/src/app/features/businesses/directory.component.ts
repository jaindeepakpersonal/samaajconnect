import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { Locality, LookupItem } from '../members/members.models';
import { BusinessesApi } from './businesses.api';
import { Business, DirectoryFilters } from './businesses.models';

/**
 * The Businesses directory, `/businesses` - every active business in the
 * caller's own Samaaj, filtered by category and location. The service
 * already scopes to the caller's Samaaj and excludes inactive businesses;
 * this screen only renders what comes back, the same shape Matrimony's own
 * search screen already uses.
 */
@Component({
  selector: 'app-businesses-directory',
  imports: [FormsModule, RouterLink],
  styleUrl: './businesses.css',
  template: `
    <div class="businesses-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Businesses</h1>
          <p class="subtitle">Members' own businesses, in your Samaaj.</p>
        </div>
        <a class="btn" routerLink="/businesses/mine">My businesses</a>
      </header>

      <form class="card filters" (ngSubmit)="load()">
        <div class="form-grid">
          <div class="field">
            <label for="term">Search</label>
            <input class="input" id="term" name="term" [(ngModel)]="filters.term" placeholder="Business name" />
          </div>

          <div class="field">
            <label for="categoryId">Category</label>
            <select class="input" id="categoryId" name="categoryId" [(ngModel)]="filters.categoryId">
              <option value="">Any</option>
              @for (option of categories(); track option.id) {
                <option [value]="option.id">{{ option.label }}</option>
              }
            </select>
          </div>

          <div class="field">
            <label for="localityId">Locality</label>
            <select class="input" id="localityId" name="localityId" [(ngModel)]="filters.localityId">
              <option value="">Any</option>
              @for (option of localities(); track option.id) {
                <option [value]="option.id">{{ option.name }}</option>
              }
            </select>
          </div>

          <div class="actions span-all">
            <button class="btn" type="submit" [disabled]="loading()">
              {{ loading() ? 'Searching…' : 'Search' }}
            </button>
          </div>
        </div>
      </form>

      @if (loading()) {
        <p role="status">Searching…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (results().length === 0) {
        <p class="notice info" role="status">No businesses match these filters.</p>
      } @else {
        <div class="grid">
          @for (business of results(); track business.id) {
            <div class="card">
              <h2>{{ business.name }}</h2>
              @if (business.description; as description) {
                <p class="group-body">{{ description }}</p>
              }
              <div class="actions">
                <a class="btn small" [routerLink]="['/businesses', business.id]">View</a>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class BusinessesDirectoryComponent implements OnInit {
  private readonly api = inject(BusinessesApi);
  private readonly masterData = inject(MasterDataApi);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly results = signal<readonly Business[]>([]);

  readonly categories = signal<readonly LookupItem[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  filters: DirectoryFilters = { categoryId: '', cityId: '', localityId: '', term: '' };

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

    this.api.search(this.filters).subscribe({
      next: (found) => {
        this.results.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }
}
