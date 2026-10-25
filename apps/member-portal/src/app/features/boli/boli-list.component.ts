import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { closesIn, describeError, formatRupees } from '@samaajconnect/shared';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { MasterDataApi } from '../members/master-data.api';
import { MemberLookup } from '../members/member-lookup';
import { City, Locality } from '../members/members.models';
import { BoliApi } from './boli.api';
import { Boli, BoliPlaceFilter, BoliResult, Occasion, OccasionStatusLabels } from './boli.models';

/**
 * Auctions / Boli, from the member-portal wireframe's `#boli` screen.
 *
 * The wireframe puts three cards side by side — an upcoming occasion, the
 * active Boli, and the published results — each with a button. Those are three
 * different things a member might have come for, so the shipped screen makes
 * them three sections and leads with whichever one is live: a member during a
 * Boli has come to bid, and a member the week after has come to see who won.
 *
 * The wireframe's counts ("3 active bidding items", "Last published: 15 Aug
 * 2026") are real here, because the service supplies them.
 *
 * **Three tabs and a place (2026-10-24).** Bidding now, Occasions and Results
 * each have a tab, opening on whichever is live. Every Boli carries its
 * occasion's place, and a City/Locality filter narrows all three - the service
 * filters the first two, and announced results here, from the place each
 * result carries. **Who won is named** - it is announced to the whole Samaaj -
 * through `MemberLookup`, one call per winner on screen.
 */
@Component({
  selector: 'app-boli-list',
  imports: [FormsModule, RouterLink],
  styleUrl: './boli.css',
  template: `
    <div class="boli-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Auctions / Boli</h1>
          <p class="subtitle">Bid on a Boli, and see what the Samaaj has announced.</p>
        </div>
      </header>

      <form class="card filters" (ngSubmit)="load()">
        <div class="form-grid">
          <div class="field">
            <label for="boli-city">City or village</label>
            <select class="input" id="boli-city" name="cityId" [ngModel]="filter.cityId" (ngModelChange)="chooseCity($event)">
              <option value="">Anywhere</option>
              @for (city of cities(); track city.id) {
                <option [value]="city.id">{{ city.name }}</option>
              }
            </select>
          </div>

          <div class="field">
            <label for="boli-locality">Locality</label>
            <select class="input" id="boli-locality" name="localityId" [(ngModel)]="filter.localityId">
              <option value="">Anywhere</option>
              @for (locality of localitiesFor(filter.cityId); track locality.id) {
                <option [value]="locality.id">{{ locality.name }}</option>
              }
            </select>
          </div>

          <div class="actions span-all">
            <button class="btn small" type="submit" [disabled]="loading()">Show</button>
          </div>
        </div>
      </form>

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else {
        <div class="tabs" role="tablist" aria-label="Auctions">
          @for (item of tabs; track item.value) {
            <button
              type="button"
              role="tab"
              [id]="'boli-tab-' + item.value"
              [class.on]="tab() === item.value"
              [attr.aria-selected]="tab() === item.value"
              aria-controls="boli-tab-panel"
              (click)="tab.set(item.value)"
            >
              {{ item.label }} ({{ countFor(item.value) }})
            </button>
          }
        </div>

        <div id="boli-tab-panel" role="tabpanel" [attr.aria-labelledby]="'boli-tab-' + tab()">
          @switch (tab()) {
            <!-- Occasions -------------------------------------------------- -->
            @case ('occasions') {
              @if (occasions().length === 0) {
                <p class="notice info" role="status">No occasions here.</p>
              } @else {
                <div class="grid">
                  @for (occasion of occasions(); track occasion.id) {
                    <div class="card">
                      <h2>{{ occasion.title }}</h2>

                      @if (whereOf(occasion); as where) {
                        <p class="small place">{{ where }}</p>
                      }

                      @if (occasion.description; as description) {
                        <p class="small">{{ description }}</p>
                      }

                      <div class="badges">
                        <span class="pill" [class]="occasionClass(occasion)">{{ stage(occasion) }}</span>
                      </div>

                      <p class="small">{{ describe(occasion) }}</p>

                      <div class="actions">
                        <a class="btn small secondary" [routerLink]="['/boli/occasions', occasion.id]">
                          View occasion
                        </a>
                      </div>
                    </div>
                  }
                </div>
              }
            }

            <!-- Announced results ------------------------------------------ -->
            @case ('results') {
              @if (shownResults().length === 0) {
                <p class="notice info" role="status">Nothing has been announced here yet.</p>
              } @else {
                <div class="table-scroll">
                  <table>
                    <caption class="sr-only">Results the Samaaj has announced</caption>
                    <tr>
                      <th scope="col">Boli</th>
                      <th scope="col">Won by</th>
                      <th scope="col">Amount</th>
                      <th scope="col">Announced</th>
                    </tr>
                    @for (result of shownResults(); track result.boliId) {
                      <tr>
                        <td><a [routerLink]="['/boli', result.boliId]">{{ result.boliTitle }}</a></td>
                        <td>
                          @if (result.winnerIsMe) {
                            <span class="pill ok">You</span>
                          } @else if (result.winningMemberId; as winnerId) {
                            <a [routerLink]="['/members', winnerId]">{{ nameOf(winnerId) }}</a>
                          }
                        </td>
                        <td>{{ money(result.amount) }}</td>
                        <td>{{ date(result.publishedAt) }}</td>
                      </tr>
                    }
                  </table>
                </div>
              }
            }

            <!-- Bidding now ------------------------------------------------ -->
            @default {
              @if (active().length === 0) {
                <p class="notice info" role="status">Nothing is taking bids here at the moment.</p>
              } @else {
                <div class="grid">
                  @for (lot of active(); track lot.id) {
                    <div class="card" [class.mine]="lot.highestBidderIsMe">
                      <h2>{{ lot.title }}</h2>
                      <p class="small">{{ lot.boliTypeName }}@if (lot.occasionTitle) { · {{ lot.occasionTitle }} }</p>

                      @if (whereOf(lot); as where) {
                        <p class="small place">{{ where }}</p>
                      }

                      @if (lot.highestAmount === null) {
                        <!-- Null is not zero: nobody has bid, which is different from
                             somebody having bid nothing. -->
                        <p class="stat unknown">No bids yet</p>
                        <p class="small">Opens at {{ money(lot.startingAmount) }}</p>
                      } @else {
                        <p class="stat">{{ money(lot.highestAmount) }}</p>
                        <p class="small">{{ lot.bidCount }} {{ lot.bidCount === 1 ? 'bid' : 'bids' }}</p>
                      }

                      <div class="badges">
                        <span class="pill warn">{{ closing(lot) }}</span>

                        @if (lot.highestBidderIsMe) {
                          <span class="pill ok">You are leading</span>
                        }
                      </div>

                      <div class="actions">
                        <a class="btn small" [routerLink]="['/boli', lot.id]">
                          {{ lot.highestBidderIsMe ? 'View' : 'Bid for this Boli' }}
                        </a>
                      </div>
                    </div>
                  }
                </div>
              }
            }
          }
        </div>
      }
    </div>
  `,
})
export class BoliListComponent implements OnInit {
  private readonly api = inject(BoliApi);
  private readonly masterData = inject(MasterDataApi);
  private readonly lookup = inject(MemberLookup);

  readonly tabs: readonly { value: BoliListTab; label: string }[] = [
    { value: 'active', label: 'Bidding now' },
    { value: 'occasions', label: 'Occasions' },
    { value: 'results', label: 'Results' },
  ];

  readonly tab = signal<BoliListTab>('active');

  readonly cities = signal<readonly City[]>([]);
  readonly localities = signal<readonly Locality[]>([]);

  filter: BoliPlaceFilter = { cityId: '', localityId: '' };

  /** The place the list was last loaded for - results are narrowed to it here. */
  private readonly applied = signal<BoliPlaceFilter>({ cityId: '', localityId: '' });

  readonly shownResults = computed(() => {
    const place = this.applied();

    return this.results().filter((r) =>
      (!place.localityId || r.localityId === place.localityId) && (!place.cityId || r.cityId === place.cityId));
  });

  readonly active = signal<readonly Boli[]>([]);
  readonly occasions = signal<readonly Occasion[]>([]);
  readonly results = signal<readonly BoliResult[]>([]);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /** The wireframe's "Upcoming Occasion" card, if there is one. */
  readonly upcoming = computed(() =>
    this.occasions().find((occasion) => occasion.status === 'Upcoming') ?? null,
  );

  ngOnInit(): void {
    this.masterData.listCities().subscribe({ next: (found) => this.cities.set(found), error: () => {} });
    this.masterData.listLocalities().subscribe({ next: (found) => this.localities.set(found), error: () => {} });
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    const place = { ...this.filter };
    this.applied.set(place);

    forkJoin({
      active: this.api.active(place),
      occasions: this.api.occasions(place),

      // A Samaaj that has never announced anything is not an error, and the
      // rest of the screen is still worth showing.
      results: this.api.publishedResults().pipe(catchError(() => of([] as BoliResult[]))),
    }).subscribe({
      next: ({ active, occasions, results }) => {
        this.active.set(active);
        this.occasions.set(occasions);
        this.results.set(results);
        this.lookup.resolve(results.filter((r) => !r.winnerIsMe && r.winningMemberId).map((r) => r.winningMemberId!));
        this.loading.set(false);

        // Open on whatever is live: bidding if anything is, else the occasions.
        if (this.tab() === 'active' && active.length === 0 && occasions.length > 0) {
          this.tab.set('occasions');
        }
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  // ---- Rendering ---------------------------------------------------------

  countFor(tab: BoliListTab): number {
    switch (tab) {
      case 'occasions':
        return this.occasions().length;
      case 'results':
        return this.shownResults().length;
      default:
        return this.active().length;
    }
  }

  /** A winner's name once looked up; "A member" otherwise - never an id. */
  nameOf(memberId: string): string {
    return this.lookup.member(memberId)?.fullName ?? 'A member';
  }

  /** "Shri Mahavir Mandir · Hiran Magri, Udaipur" - the venue, then the locality and city. */
  whereOf(place: { readonly venue?: string | null; readonly localityId?: string | null; readonly cityId?: string | null }): string | null {
    const locality = this.localities().find((l) => l.id === place.localityId);
    const city = this.cities().find((c) => c.id === (place.cityId ?? locality?.cityId));
    const area = [locality?.name, city?.name].filter((part): part is string => !!part).join(', ');
    const parts = [place.venue, area].filter((part): part is string => !!part && part.trim().length > 0);

    return parts.length > 0 ? parts.join(' · ') : null;
  }

  localitiesFor(cityId: string): readonly Locality[] {
    return cityId ? this.localities().filter((l) => l.cityId === cityId) : this.localities();
  }

  /** A locality from another city would match nothing, so changing the city clears it. */
  chooseCity(cityId: string): void {
    this.filter.cityId = cityId;

    if (cityId && !this.localitiesFor(cityId).some((l) => l.id === this.filter.localityId)) {
      this.filter.localityId = '';
    }
  }

  money(paise: number): string {
    return formatRupees(paise);
  }

  closing(lot: Boli): string {
    return closesIn(lot.endAt);
  }

  stage(occasion: Occasion): string {
    return OccasionStatusLabels[occasion.status];
  }

  occasionClass(occasion: Occasion): string {
    switch (occasion.status) {
      case 'Active':
        return 'warn';
      case 'Closed':
        return '';
      case 'Upcoming':
        return 'ok';
    }
  }

  describe(occasion: Occasion): string {
    const boli = `${occasion.boliCount} ${occasion.boliCount === 1 ? 'Boli' : 'Boli'}`;
    const types = `${occasion.typeCount} ${occasion.typeCount === 1 ? 'type' : 'types'}`;

    return `${this.date(occasion.occasionDate)} • ${boli} • ${types}`;
  }

  date(iso: string | null): string {
    if (iso === null) {
      return '';
    }

    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }
}

type BoliListTab = 'active' | 'occasions' | 'results';
