import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { closesIn, describeError, formatRupees, parseRupees, toInputValue } from '@samaajconnect/shared';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { BoliApi } from './boli.api';
import { Bid, Boli, BoliResult, BoliStatusLabels } from './boli.models';

/**
 * One Boli: the current highest, the bid form, the history, and the result.
 *
 * This is the wireframe's `#bolidetail` — "Current Highest Bid" and "Bid
 * History" side by side — with the announced result added below, because a
 * member arriving after the Boli closed wants the outcome where the bidding
 * was.
 *
 * Four things this screen has to keep straight.
 *
 * **The minimum comes from the server.** `minimumNextBid` is computed by the
 * Boli, which owns the increment rule. Recomputing it here would put a second
 * copy of that rule in the portal, and the first time a Samaaj changed an
 * increment the screen would confidently tell a bidder the wrong number.
 *
 * **Being outbid is not an error.** The service answers 200 with
 * `accepted: false` and the amount now needed. Showing that as a red failure
 * would be telling somebody off for being slow while their form was open; it is
 * a notice, and the form is refilled with the number that would work.
 *
 * **Nobody is named while bidding is open.** The history carries amounts and
 * times and "is this mine", and nothing else — a public running list of who
 * will pay what turns an auction into a statement about people's means.
 *
 * **A recorded result is not an announced one.** `/result` answers 404 until
 * something has been recorded and names no winner until it has been published,
 * so the screen only asks once the Boli says it is closed.
 */
@Component({
  selector: 'app-boli-detail',
  imports: [FormsModule, RouterLink],
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
      } @else if (boli(); as lot) {
        <h1 class="page-title">{{ lot.title }}</h1>
        <p class="subtitle">
          <span class="pill" [class]="pillClass(lot)">{{ stage(lot) }}</span>
          {{ lot.boliTypeName }}
        </p>

        @if (lot.eligibilityRule; as rule) {
          <!-- The Samaaj's own words. Nothing on the platform enforces this,
               and the screen does not pretend otherwise. -->
          <p class="notice info" role="status">
            <b>Who may bid:</b> {{ rule }}
          </p>
        }

        <div class="grid2">
          <!-- Current highest, and the form -------------------------------- -->
          <div class="card">
            <h2>Current highest bid</h2>

            @if (lot.highestAmount === null) {
              <p class="stat unknown">No bids yet</p>
              <p class="small">Bidding opens at {{ money(lot.startingAmount) }}.</p>
            } @else {
              <p class="stat">{{ money(lot.highestAmount) }}</p>
              <p class="small">
                @if (lot.highestBidderIsMe) {
                  Yours. {{ lot.bidCount }} {{ lot.bidCount === 1 ? 'bid' : 'bids' }} so far.
                } @else {
                  <!-- The wireframe's "by Member ID 1042 (name hidden until
                       close)". The portal does not have the id either, and does
                       not need it. -->
                  By another member — names are not shown while bidding is open.
                }
              </p>
            }

            @if (lot.acceptsBids) {
              <p class="small">{{ closing(lot) }} ({{ dateTime(lot.endAt) }}).</p>

              @if (extending(lot); as note) {
                <p class="small">{{ note }}</p>
              }

              <label for="amount">Your bid (₹)</label>
              <input
                class="input"
                id="amount"
                name="amount"
                inputmode="decimal"
                [(ngModel)]="amount"
                [placeholder]="'Minimum ' + money(lot.minimumNextBid)"
                [attr.aria-invalid]="bidError() !== null"
              />
              <p class="small">{{ guidance(lot) }}</p>

              @if (bidError(); as message) {
                <p class="notice error" role="alert">{{ message }}</p>
              }

              @if (outbid(); as message) {
                <!-- Not an error. Somebody was outbid while their form was
                     open, and the amount they now need is in the message. -->
                <p class="notice info" role="status">{{ message }}</p>
              }

              @if (placed(); as message) {
                <p class="notice info" role="status">{{ message }}</p>
              }

              <div class="actions">
                <button
                  class="btn"
                  type="button"
                  [disabled]="busy() || amount.trim().length === 0"
                  (click)="placeBid(lot)"
                >
                  {{ busy() ? 'Placing…' : 'Place bid' }}
                </button>

                <button class="btn secondary" type="button" (click)="useMinimum(lot)">
                  Use the minimum
                </button>
              </div>
            } @else {
              <p class="small">{{ whyClosed(lot) }}</p>
            }
          </div>

          <!-- Bid history ------------------------------------------------- -->
          <div class="card">
            <h2>Bid history</h2>

            @if (bids().length === 0) {
              <p class="small">Nobody has bid yet.</p>
            } @else {
              <div class="table-scroll">
                <table>
                  <caption class="sr-only">Every bid on this Boli, highest first</caption>
                  <tr>
                    <th scope="col">Amount</th>
                    <th scope="col">Time</th>
                  </tr>
                  @for (bid of bids(); track bid.id) {
                    <tr [class.mine]="bid.isMine">
                      <td>
                        {{ money(bid.amount) }}
                        @if (bid.isMine) {
                          <span class="pill ok">Yours</span>
                        }
                      </td>
                      <td>{{ time(bid.placedAt) }}</td>
                    </tr>
                  }
                </table>
              </div>
            }
          </div>
        </div>

        <!-- The result --------------------------------------------------- -->
        @if (result(); as announced) {
          <h2 class="section-heading">Result</h2>

          @if (announced.isPublished) {
            <div class="card">
              <p class="stat">{{ money(announced.amount) }}</p>
              <p class="small">
                @if (announced.winnerIsMe) {
                  Won by you, announced {{ dateTime(announced.publishedAt) }}.
                } @else {
                  Announced {{ dateTime(announced.publishedAt) }}.
                }
              </p>
            </div>
          } @else {
            <!-- Recorded and not yet announced. Saying so is more use than
                 saying nothing, and it names nobody. -->
            <p class="notice info" role="status">
              The result has been recorded and will be announced shortly.
            </p>
          }
        }
      }
    </div>
  `,
})
export class BoliDetailComponent implements OnInit {
  private readonly api = inject(BoliApi);
  private readonly route = inject(ActivatedRoute);

  readonly boli = signal<Boli | null>(null);
  readonly bids = signal<readonly Bid[]>([]);
  readonly result = signal<BoliResult | null>(null);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly busy = signal(false);

  /** A bid the portal itself refused: not a number, or below the minimum. */
  readonly bidError = signal<string | null>(null);

  /** A bid the service refused as too low. Reported as a notice, not an error. */
  readonly outbid = signal<string | null>(null);

  readonly placed = signal<string | null>(null);

  amount = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name a Boli.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.get(id).subscribe({
      next: (lot) => {
        this.boli.set(lot);

        forkJoin({
          bids: this.api.bids(id).pipe(catchError(() => of([] as Bid[]))),

          // Only asked for once bidding is over. Before that the endpoint
          // answers 404 by design, and a speculative call would put an expected
          // 404 on every visit to an open Boli.
          result:
            lot.status === 'Closed' || lot.status === 'ResultPublished'
              ? this.api.result(id).pipe(catchError(() => of(null)))
              : of(null),
        }).subscribe({
          next: ({ bids, result }) => {
            this.bids.set(bids);
            this.result.set(result);
            this.loading.set(false);
          },
          error: () => this.loading.set(false),
        });
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  // ---- Bidding -----------------------------------------------------------

  placeBid(lot: Boli): void {
    this.bidError.set(null);
    this.outbid.set(null);
    this.placed.set(null);

    const paise = parseRupees(this.amount);

    if (paise === null) {
      this.bidError.set('Enter an amount in rupees, like 15600 or 15600.50.');
      return;
    }

    // Checked here as a courtesy so an obviously-too-low bid does not need a
    // round trip. The server checks it again against the live highest, which is
    // the check that counts - this one is looking at a number that may already
    // be stale.
    if (paise < lot.minimumNextBid) {
      this.bidError.set(
        `The next bid has to be at least ${formatRupees(lot.minimumNextBid)}.`,
      );
      return;
    }

    this.busy.set(true);

    this.api.bid(lot.id, paise).subscribe({
      next: (outcome) => {
        this.busy.set(false);

        if (outcome.accepted) {
          this.amount = '';
          this.placed.set(`Your bid of ${formatRupees(paise)} is the highest.`);
        } else {
          // Somebody got there first. Refill the form with the amount that
          // would now work, so the member does not have to do the arithmetic.
          this.amount = toInputValue(outcome.minimumNextBid);
          this.outbid.set(
            `Somebody bid first. The next bid has to be at least ` +
              `${formatRupees(outcome.minimumNextBid)}.`,
          );
        }

        this.load();
      },
      error: (failure: unknown) => {
        this.busy.set(false);
        this.bidError.set(describeError(failure));
      },
    });
  }

  useMinimum(lot: Boli): void {
    this.amount = toInputValue(lot.minimumNextBid);
    this.bidError.set(null);
  }

  // ---- Rendering ---------------------------------------------------------

  money(paise: number): string {
    return formatRupees(paise);
  }

  closing(lot: Boli): string {
    return closesIn(lot.endAt);
  }

  /**
   * The anti-sniping window, in the bidder's words, or null when it is off.
   *
   * Saying it is not decoration. Without this line the screen prints a closing
   * time the server will quietly move, which is the portal stating something
   * that stops being true; and the rule only does its job once bidders know it,
   * because a late bid is a bad idea only if you know it buys everybody else
   * another window.
   */
  extending(lot: Boli): string | null {
    if (lot.autoExtendSeconds <= 0) {
      return null;
    }

    // Minutes only when it is a whole number of them. Rounding 90 seconds to
    // "2 minutes" would print a window a minute longer than the one the server
    // is actually keeping, and this line exists to be relied on.
    const minutes = lot.autoExtendSeconds / 60;
    const window =
      Number.isInteger(minutes) && minutes >= 1
        ? `${minutes} minute${minutes === 1 ? '' : 's'}`
        : `${lot.autoExtendSeconds} seconds`;

    return (
      `A bid in the last ${window} moves the close to ${window} after that bid, ` +
      'so there is nothing to be gained by waiting until the end.'
    );
  }

  /**
   * What the next bid has to clear, and why.
   *
   * The first bid and every later one are different rules, and saying so
   * matters: with nothing bid yet there *is* no current highest, and the screen
   * shipped saying "₹25,000 — ₹1,000 above the current highest" above an empty
   * bid history. The increment does not apply until somebody has bid.
   */
  guidance(lot: Boli): string {
    if (lot.highestAmount === null) {
      return `The first bid has to be at least ${formatRupees(lot.minimumNextBid)}.`;
    }

    return (
      `The next bid has to be at least ${formatRupees(lot.minimumNextBid)} — ` +
      `${formatRupees(lot.minIncrement)} above the current highest.`
    );
  }

  stage(lot: Boli): string {
    return lot.acceptsBids ? 'Bidding open' : BoliStatusLabels[lot.status];
  }

  pillClass(lot: Boli): string {
    if (lot.status === 'ResultPublished') {
      return 'ok';
    }

    return lot.acceptsBids ? 'warn' : '';
  }

  /**
   * Why a Boli is not taking bids, which the status alone does not say.
   *
   * A Boli whose window has not arrived and one whose window has passed are
   * both `Open`, and they need opposite sentences.
   */
  whyClosed(lot: Boli): string {
    if (lot.status === 'Scheduled') {
      return `Bidding has not opened yet. It opens ${this.dateTime(lot.startAt)}.`;
    }

    if (lot.status === 'Open' && new Date(lot.startAt) > new Date()) {
      return `Bidding opens ${this.dateTime(lot.startAt)}.`;
    }

    return 'Bidding has closed.';
  }

  time(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  }

  dateTime(iso: string | null): string {
    if (iso === null) {
      return '';
    }

    const date = new Date(iso);

    return Number.isNaN(date.getTime()) ? '' : date.toLocaleString();
  }
}
