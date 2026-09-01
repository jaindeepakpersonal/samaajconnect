import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { BoliApi } from './boli.api';
import { closesIn, formatRupees } from './boli.format';
import { Boli, OccasionDetail, OccasionStatusLabels } from './boli.models';

/**
 * One occasion and the Boli under it — the wireframe's "View Occasion".
 *
 * A Samaaj holds several Boli at one occasion, and a member who missed the
 * announcement wants the whole card rather than whichever one happens to be
 * taking bids this minute. Each Boli here shows what it is doing now, from
 * `acceptsBids` rather than from its status.
 */
@Component({
  selector: 'app-occasion',
  imports: [RouterLink],
  styleUrl: './boli.css',
  template: `
    <div class="boli-page">
      <a class="back" routerLink="/boli">‹ Back to Auctions / Boli</a>

      @if (loading()) {
        <p role="status">Loading…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (occasion(); as found) {
        <h1 class="page-title">{{ found.title }}</h1>
        <p class="subtitle">
          <span class="pill" [class]="pillClass(found.status)">{{ stage(found.status) }}</span>
          {{ date(found.occasionDate) }}
        </p>

        @if (found.description; as description) {
          <p>{{ description }}</p>
        }

        <h2 class="section-heading">Boli at this occasion</h2>

        @if (found.boli.length === 0) {
          <p class="small">No Boli has been opened for this occasion yet.</p>
        } @else {
          <div class="grid">
            @for (lot of found.boli; track lot.id) {
              <div class="card" [class.mine]="leading(lot)">
                <h3>{{ lot.title }}</h3>
                <p class="small">{{ lot.boliTypeName }}</p>

                @if (lot.highestAmount === null) {
                  <p class="stat unknown">No bids yet</p>
                } @else {
                  <p class="stat">{{ money(lot.highestAmount) }}</p>
                }

                <div class="badges">
                  <span class="pill" [class]="lot.acceptsBids ? 'warn' : ''">
                    {{ describe(lot) }}
                  </span>

                  @if (leading(lot)) {
                    <span class="pill ok">You are leading</span>
                  }
                </div>

                <div class="actions">
                  <a class="btn small" [routerLink]="['/boli', lot.id]">
                    {{ lot.acceptsBids ? 'Bid for this Boli' : 'View' }}
                  </a>
                </div>
              </div>
            }
          </div>
        }

        @if (found.types.length > 0) {
          <h2 class="section-heading">Types offered</h2>

          <ul>
            @for (type of found.types; track type.id) {
              <li>
                <b>{{ type.name }}</b>
                @if (type.description; as description) {
                  — {{ description }}
                }
              </li>
            }
          </ul>
        }
      }
    </div>
  `,
})
export class OccasionComponent implements OnInit {
  private readonly api = inject(BoliApi);
  private readonly route = inject(ActivatedRoute);

  readonly occasion = signal<OccasionDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name an occasion.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.occasion(id).subscribe({
      next: (found) => {
        this.occasion.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  money(paise: number): string {
    return formatRupees(paise);
  }

  stage(status: OccasionDetail['status']): string {
    return OccasionStatusLabels[status];
  }

  pillClass(status: OccasionDetail['status']): string {
    switch (status) {
      case 'Active':
        return 'warn';
      case 'Upcoming':
        return 'ok';
      case 'Closed':
        return '';
    }
  }

  /**
   * Whether to say the reader is leading — only while bidding is actually open.
   *
   * Two reasons this is not just `highestBidderIsMe`. On a Boli that has closed
   * it is the wrong tense: "You are leading" beside "Result announced" reads as
   * a live race that finished hours ago. And on one that has closed *without* a
   * published result it would be worse than wrong — it would tell the reader
   * they won before the Samaaj announced it, which is exactly what the
   * service's record-then-publish split exists to prevent.
   *
   * Once a result is published the list screen says "Won by you", from the
   * result itself rather than from the leading bid.
   */
  leading(lot: Boli): boolean {
    return lot.acceptsBids && lot.highestBidderIsMe;
  }

  /** What this Boli is doing now — from the flag, not the status. */
  describe(lot: Boli): string {
    if (lot.acceptsBids) {
      return closesIn(lot.endAt);
    }

    return lot.status === 'ResultPublished' ? 'Result announced' : 'Bidding closed';
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
