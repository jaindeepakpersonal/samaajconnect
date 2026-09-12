import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthedImageDirective, describeError } from '@samaajconnect/shared';
import { MasterDataApi } from '../members/master-data.api';
import { Locality, LookupItem } from '../members/members.models';
import { MatrimonyApi } from './matrimony.api';
import { Biodata, Diet, DietLabels, Diets, Gender, SearchFilters } from './matrimony.models';

/**
 * Matrimony search, `/matrimony/search`.
 *
 * Every result is already scoped to the caller's own Samaaj and excludes the
 * caller and every inactive profile - the service does both, so this screen
 * only has to render what comes back.
 */
@Component({
  selector: 'app-matrimony-search',
  imports: [FormsModule, RouterLink, AuthedImageDirective],
  styleUrl: './matrimony.css',
  template: `
    <div class="matrimony-page">
      <a class="back" routerLink="/matrimony">‹ Back to My Biodata</a>

      <header class="page-head">
        <div>
          <h1 class="page-title">Search candidates</h1>
          <p class="subtitle">Within your own Samaaj, opted-in profiles only.</p>
        </div>
      </header>

      <form class="card filters" (ngSubmit)="load()">
        <label for="gender">Gender</label>
        <select class="input" id="gender" name="gender" [(ngModel)]="filters.gender">
          <option value="">Any</option>
          @for (option of genders; track option) {
            <option [value]="option">{{ option }}</option>
          }
        </select>

        <label for="minAge">Min age</label>
        <input class="input" id="minAge" name="minAge" type="number" min="18" [(ngModel)]="filters.minAge" />

        <label for="maxAge">Max age</label>
        <input class="input" id="maxAge" name="maxAge" type="number" min="18" [(ngModel)]="filters.maxAge" />

        <label for="maritalStatusId">Marital status</label>
        <select class="input" id="maritalStatusId" name="maritalStatusId" [(ngModel)]="filters.maritalStatusId">
          <option value="">Any</option>
          @for (option of maritalStatuses(); track option.id) {
            <option [value]="option.id">{{ option.label }}</option>
          }
        </select>

        <label for="minHeightCm">Min height (cm)</label>
        <input
          class="input"
          id="minHeightCm"
          name="minHeightCm"
          type="number"
          min="0"
          [(ngModel)]="filters.minHeightCm"
        />

        <label for="maxHeightCm">Max height (cm)</label>
        <input
          class="input"
          id="maxHeightCm"
          name="maxHeightCm"
          type="number"
          min="0"
          [(ngModel)]="filters.maxHeightCm"
        />

        <label for="diet">Diet</label>
        <select class="input" id="diet" name="diet" [(ngModel)]="filters.diet">
          <option value="">Any</option>
          @for (option of diets; track option) {
            <option [value]="option">{{ dietLabel(option) }}</option>
          }
        </select>

        <label for="localityId">Locality</label>
        <select class="input" id="localityId" name="localityId" [(ngModel)]="filters.localityId">
          <option value="">Any</option>
          @for (option of localities(); track option.id) {
            <option [value]="option.id">{{ option.name }}</option>
          }
        </select>

        <label>
          <input type="checkbox" name="excludeOwnGautra" [(ngModel)]="filters.excludeOwnGautra" />
          Exclude my own Gautra
        </label>

        <div class="actions">
          <button class="btn" type="submit" [disabled]="loading()">
            {{ loading() ? 'Searching…' : 'Search' }}
          </button>
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
        <p class="notice info" role="status">No candidates match these filters.</p>
      } @else {
        <div class="grid">
          @for (candidate of results(); track candidate.memberId) {
            <div class="card">
              @if (candidate.photoUrl; as path) {
                <img class="candidate-photo" [scAuthedSrc]="path" alt="" />
              }
              <h2>{{ candidate.fullName }}</h2>
              <p>
                {{ candidate.age }} years
                @if (candidate.heightCm; as height) {
                  • {{ height }} cm
                }
              </p>
              <div class="actions">
                <a class="btn small" [routerLink]="['/matrimony/candidates', candidate.memberId]">View</a>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class MatrimonySearchComponent implements OnInit {
  private readonly api = inject(MatrimonyApi);
  private readonly masterData = inject(MasterDataApi);

  readonly genders: readonly Gender[] = ['Male', 'Female', 'Other'];
  readonly diets = Diets;

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly results = signal<readonly Biodata[]>([]);

  readonly maritalStatuses = signal<readonly LookupItem[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  filters: SearchFilters = {
    gender: '',
    minAge: null,
    maxAge: null,
    maritalStatusId: '',
    minHeightCm: null,
    maxHeightCm: null,
    diet: '',
    localityId: '',
    excludeOwnGautra: false,
  };

  ngOnInit(): void {
    this.masterData
      .listMaritalStatuses()
      .subscribe({ next: (found) => this.maritalStatuses.set(found), error: () => {} });
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

  dietLabel(diet: Diet): string {
    return DietLabels[diet];
  }
}
