import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { BoliApi } from './boli.api';
import { closesIn, formatRupees } from './boli.format';
import { Boli, BoliResult, Occasion, OccasionStatusLabels } from './boli.models';

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
 */
@Component({
  selector: 'app-boli-list',
  imports: [RouterLink],
  styleUrl: './boli.css',
  template: `
    <div class="boli-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Auctions / Boli</h1>
          <p class="subtitle">Bid on a Boli, and see what the Samaaj has announced.</p>
        </div>
        <a class="btn secondary" routerLink="/home">Back to home</a>
      </header>

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else {
        <!-- Bidding now ---------------------------------------------------- -->
        @if (active().length > 0) {
          <h2 class="section-heading">Bidding now</h2>

          <div class="grid">
            @for (lot of active(); track lot.id) {
              <div class="card" [class.mine]="lot.highestBidderIsMe">
                <h3>{{ lot.title }}</h3>
                <p class="small">{{ lot.boliTypeName }}</p>

                @if (lot.highestAmount === null) {
                  <!-- Null is not zero: nobody has bid, which is different from
                       somebody having bid nothing. -->
                  <p class="stat unknown">No bids yet</p>
                  <p class="small">Opens at {{ money(lot.startingAmount) }}</p>
                } @else {
                  <p class="stat">{{ money(lot.highestAmount) }}</p>
                  <p class="small">
                    {{ lot.bidCount }} {{ lot.bidCount === 1 ? 'bid' : 'bids' }}
                  </p>
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
        } @else {
          <p class="notice info" role="status">
            Nothing is taking bids at the moment.
          </p>
        }

        <!-- Occasions ------------------------------------------------------ -->
        @if (occasions().length > 0) {
          <h2 class="section-heading">Occasions</h2>

          <div class="grid">
            @for (occasion of occasions(); track occasion.id) {
              <div class="card">
                <h3>{{ occasion.title }}</h3>

                @if (occasion.description; as description) {
                  <p class="small">{{ description }}</p>
                }

                <div class="badges">
                  <span class="pill" [class]="occasionClass(occasion)">
                    {{ stage(occasion) }}
                  </span>
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

        <!-- Published results ---------------------------------------------- -->
        <h2 class="section-heading">Announced results</h2>

        @if (results().length === 0) {
          <p class="small">Nothing has been announced yet.</p>
        } @else {
          <div class="table-scroll">
            <table>
              <caption class="sr-only">Results the Samaaj has announced</caption>
              <tr>
                <th scope="col">Boli</th>
                <th scope="col">Amount</th>
                <th scope="col">Announced</th>
              </tr>
              @for (result of results(); track result.boliId) {
                <tr>
                  <td>
                    {{ result.boliTitle }}
                    @if (result.winnerIsMe) {
                      <span class="pill ok">Won by you</span>
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
    </div>
  `,
})
export class BoliListComponent implements OnInit {
  private readonly api = inject(BoliApi);

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
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    forkJoin({
      active: this.api.active(),
      occasions: this.api.occasions(),

      // A Samaaj that has never announced anything is not an error, and the
      // rest of the screen is still worth showing.
      results: this.api.publishedResults().pipe(catchError(() => of([] as BoliResult[]))),
    }).subscribe({
      next: ({ active, occasions, results }) => {
        this.active.set(active);
        this.occasions.set(occasions);
        this.results.set(results);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  // ---- Rendering ---------------------------------------------------------

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
