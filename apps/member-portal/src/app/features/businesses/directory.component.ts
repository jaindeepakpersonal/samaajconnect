import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { City, Locality, LookupItem } from '../members/members.models';
import { MemberLookup, initialsOf } from '../members/member-lookup';
import { placeOf } from './business-place';
import { BusinessesApi } from './businesses.api';
import { Business, DirectoryFilters } from './businesses.models';

/**
 * The Businesses directory, `/businesses` - every active business in the
 * caller's own Samaaj, filtered by category and location. The service
 * already scopes to the caller's Samaaj and excludes inactive businesses;
 * this screen only renders what comes back, the same shape Matrimony's own
 * search screen already uses.
 *
 * **A listing shows the business's photo, where it is and who runs it**
 * (2026-10-24). Where it is resolves against the City and Locality lists loaded
 * once; the owner's name and photo come from `MemberLookup`, one lookup per
 * owner on screen and linked to their profile, and say "A member" for one this
 * reader may not see.
 */
@Component({
  selector: 'app-businesses-directory',
  imports: [FormsModule, RouterLink, AuthedImageDirective],
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
            <label for="cityId">City or village</label>
            <select class="input" id="cityId" name="cityId" [ngModel]="filters.cityId" (ngModelChange)="chooseCity($event)">
              <option value="">Any</option>
              @for (city of cities(); track city.id) {
                <option [value]="city.id">{{ city.name }}</option>
              }
            </select>
          </div>

          <div class="field">
            <label for="localityId">Locality</label>
            <select class="input" id="localityId" name="localityId" [(ngModel)]="filters.localityId">
              <option value="">Any</option>
              @for (option of localitiesFor(filters.cityId); track option.id) {
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
        <ul class="biz-grid" aria-label="Businesses">
          @for (business of results(); track business.id) {
            <li class="card biz-card">
              <a class="biz-cover-link" [routerLink]="['/businesses', business.id]" tabindex="-1" aria-hidden="true">
                @if (business.photoUrl; as path) {
                  <img class="biz-cover" [scAuthedSrc]="path" alt="" />
                } @else {
                  <span class="biz-cover placeholder">{{ initials(business.name) }}</span>
                }
              </a>

              <div class="biz-body">
                <h2 class="biz-name">
                  <a [routerLink]="['/businesses', business.id]">{{ business.name }}</a>
                </h2>

                @if (categoryLabel(business.categoryId); as category) {
                  <span class="pill">{{ category }}</span>
                }

                @if (place(business); as where) {
                  <p class="biz-place small">{{ where }}</p>
                }

                @if (business.description; as description) {
                  <p class="biz-description">{{ description }}</p>
                }

                <div class="owner-chip">
                  @let owner = owners.member(business.ownerMemberId);
                  @if (owner?.photoUrl; as path) {
                    <img class="owner-avatar" [scAuthedSrc]="path" alt="" />
                  } @else {
                    <span class="owner-avatar initials" aria-hidden="true">{{ owner ? initials(owner.fullName) : '' }}</span>
                  }
                  <span class="small">
                    Run by
                    @if (owner) {
                      <a [routerLink]="['/members', owner.id]">{{ owner.fullName }}</a>
                    } @else if (owner === null) {
                      a member
                    } @else {
                      …
                    }
                  </span>
                </div>
              </div>
            </li>
          }
        </ul>
      }
    </div>
  `,
})
export class BusinessesDirectoryComponent implements OnInit {
  private readonly api = inject(BusinessesApi);
  private readonly masterData = inject(MasterDataApi);

  readonly owners = inject(MemberLookup);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly results = signal<readonly Business[]>([]);

  readonly categories = signal<readonly LookupItem[]>([]);
  readonly cities = signal<readonly City[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  filters: DirectoryFilters = { categoryId: '', cityId: '', localityId: '', term: '' };

  ngOnInit(): void {
    this.masterData
      .listBusinessCategories()
      .subscribe({ next: (found) => this.categories.set(found), error: () => {} });
    this.masterData.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });
    this.masterData.listLocalities().subscribe({ next: (found) => this.localities.set(found), error: () => {} });

    this.load();
  }

  initials(name: string): string {
    return initialsOf(name);
  }

  categoryLabel(categoryId: string): string | null {
    return this.categories().find((c) => c.id === categoryId)?.label ?? null;
  }

  place(business: Business): string | null {
    return placeOf(business, this.cities(), this.localities());
  }

  /** Every locality, or only the chosen city's. */
  localitiesFor(cityId: string): readonly Locality[] {
    return cityId ? this.localities().filter((l) => l.cityId === cityId) : this.localities();
  }

  /** A locality from another city would match nothing, so changing the city clears it. */
  chooseCity(cityId: string): void {
    this.filters.cityId = cityId;

    if (cityId && !this.localitiesFor(cityId).some((l) => l.id === this.filters.localityId)) {
      this.filters.localityId = '';
    }
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.search(this.filters).subscribe({
      next: (found) => {
        this.results.set(found);
        this.owners.resolve(found.map((b) => b.ownerMemberId));
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }
}
