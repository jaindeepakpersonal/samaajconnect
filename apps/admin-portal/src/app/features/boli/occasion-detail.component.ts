import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { closesIn, describeError, formatRupees, parseRupees } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Boli, OccasionDetail, OccasionStatus } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';

/**
 * One occasion: its Boli types, the Boli under it, and running each one.
 *
 * **The auction floor, which was curl-only.** Defining a type, opening a Boli,
 * closing it and recording who won are four of the seven Boli endpoints no
 * screen reached. Members could bid the whole time — which is to say they could
 * bid on nothing, because nothing could be opened for them to bid on.
 *
 * **Amounts are typed in rupees and sent as paise.** `parseRupees` from
 * `libs/shared` is the only conversion, and it rounds rather than truncating:
 * `15600.07` parsed as a float and multiplied by a hundred is
 * `1560006.9999999998`, and truncating that takes a paisa off what the manager
 * typed — in a number the Samaaj collects against.
 *
 * **Closing is offered even on a Boli whose window has already passed.** The
 * service treats status and clock as two separate facts: a Boli left `Open` past
 * its closing time stops taking bids on the clock, but it is still `Open` until
 * somebody closes it, and only a closed Boli can have its result recorded. A
 * screen that hid the button once the clock ran out would strand exactly the
 * Boli that most needs finishing.
 *
 * **Recording takes no winner and the form offers none.** `RecordResultCommand`
 * reads the highest bid; a winner parameter would let a result name somebody the
 * append-only bid history contradicts.
 */
@Component({
  selector: 'app-occasion-detail',
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    <p><a class="btn link" routerLink="/boli">← All occasions</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such occasion in {{ scope.label() }}.</p>
    } @else if (occasion(); as event) {
      <h1 class="title">{{ event.title }}</h1>
      <p class="sub">{{ event.occasionDate }} · {{ event.status }}</p>

      @if (event.description) {
        <p>{{ event.description }}</p>
      }

      <!-- Status --------------------------------------------------------- -->
      <div class="card">
        <h2>Status</h2>

        @if (nextStatus(); as next) {
          <p class="small">
            Upcoming → Active → Closed, and never backwards. The service refuses a step back, so
            this is the one move available.
          </p>

          <button class="btn" type="button" [disabled]="busy()" (click)="move(next)">
            Move to {{ next }}
          </button>
        } @else {
          <p class="empty">This occasion is closed. Nothing moves it further.</p>
        }
      </div>

      <!-- Boli types ----------------------------------------------------- -->
      <div class="card spaced">
        <h2>Boli types</h2>

        @if (event.types.length === 0) {
          <p class="empty">
            None yet. A type is a label the Samaaj reuses — "Mangal Deep", "Swapna". Nobody bids
            on a type; a Boli is opened against one.
          </p>
        } @else {
          <ul class="plain">
            @for (type of event.types; track type.id) {
              <li>
                <b>{{ type.name }}</b>
                @if (type.description) { — {{ type.description }} }
              </li>
            }
          </ul>
        }

        <form (ngSubmit)="defineType()">
          <h3 class="section-heading">Define a type</h3>

          <label for="type-name">Name</label>
          <input id="type-name" class="input" name="typeName" [(ngModel)]="typeName"
            maxlength="120" placeholder="Mangal Deep" />

          <label for="type-description">Description</label>
          <input id="type-description" class="input" name="typeDescription"
            [(ngModel)]="typeDescription" maxlength="500" />

          <p class="small">One name per occasion, whatever the capitalisation.</p>

          <button class="btn" type="submit" [disabled]="busy() || !typeName.trim()">
            Define type
          </button>
        </form>
      </div>

      <!-- The Boli ------------------------------------------------------- -->
      <div class="card spaced">
        <h2>Boli</h2>

        @if (event.boli.length === 0) {
          <p class="empty">None opened yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Boli in this occasion</caption>
              <thead>
                <tr>
                  <th>Boli</th><th>Window</th><th>Highest</th><th>Bids</th>
                  <th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (lot of event.boli; track lot.id) {
                  <tr>
                    <td>
                      <b>{{ lot.title }}</b>
                      <div class="muted">{{ lot.boliTypeName }}</div>
                    </td>
                    <td>
                      {{ lot.startAt | date: 'd MMM, h:mm a' }} —
                      {{ lot.endAt | date: 'h:mm a' }}
                      @if (lot.acceptsBids) {
                        <div class="muted">{{ closes(lot.endAt) }}</div>
                      }
                      @if (lot.autoExtendSeconds > 0) {
                        <!--
                          Worth showing: a closing time that moves on its own is
                          surprising unless the screen says it can.
                        -->
                        <div class="muted">
                          A bid in the last {{ lot.autoExtendSeconds }}s pushes this out
                        </div>
                      }
                    </td>
                    <td>
                      @if (lot.highestAmount !== null) {
                        {{ rupees(lot.highestAmount) }}
                      } @else {
                        <span class="muted">No bids · floor {{ rupees(lot.startingAmount) }}</span>
                      }
                    </td>
                    <td>{{ lot.bidCount }}</td>
                    <td>
                      {{ lot.status }}
                      @if (lot.status === 'Open' && !lot.acceptsBids) {
                        <div class="muted">Window has passed</div>
                      }
                    </td>
                    <td>
                      <div class="row-actions">
                        @if (lot.status === 'Open' || lot.status === 'Scheduled') {
                          <button class="btn small" type="button" [disabled]="busy()"
                            (click)="close(lot)">
                            Close
                          </button>
                        }

                        @if (lot.status === 'Closed') {
                          @if (lot.bidCount === 0) {
                            <span class="muted">Nobody bid</span>
                          } @else {
                            <button class="btn small" type="button" [disabled]="busy()"
                              (click)="record(lot)">
                              Record result
                            </button>
                          }
                        }

                        @if (lot.status === 'ResultPublished') {
                          <span class="muted">Announced</span>
                        }
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <p class="small">
            A recorded result waits on the Auctions screen until somebody announces it. Recording
            names nobody — the winner is read from the highest bid and appears when it is
            published.
          </p>
        }

        @if (event.types.length === 0) {
          <p class="notice">
            Define a Boli type first. A Boli is opened against one, so there is nothing to open
            it as yet.
          </p>
        } @else {
          <form (ngSubmit)="open()">
            <h3 class="section-heading">Open a Boli</h3>

            <label for="boli-type">Type</label>
            <select id="boli-type" class="input" name="boliType" [(ngModel)]="boliTypeId">
              <option value="">Choose a type…</option>
              @for (type of event.types; track type.id) {
                <option [value]="type.id">{{ type.name }}</option>
              }
            </select>

            <label for="boli-title">Title</label>
            <input id="boli-title" class="input" name="boliTitle" [(ngModel)]="boliTitle"
              maxlength="200" placeholder="Mangal Deep — first day" />

            <div class="filter-row">
              <div>
                <label for="boli-start">Opens</label>
                <input id="boli-start" class="input" type="datetime-local" name="startAt"
                  [(ngModel)]="startAt" />
              </div>
              <div>
                <label for="boli-end">Closes</label>
                <input id="boli-end" class="input" type="datetime-local" name="endAt"
                  [(ngModel)]="endAt" />
              </div>
            </div>

            <div class="filter-row">
              <div>
                <label for="boli-floor">Starting amount (₹)</label>
                <input id="boli-floor" class="input" name="startingAmount"
                  [(ngModel)]="startingAmount" inputmode="decimal" placeholder="1000"
                  [attr.aria-invalid]="startingAmount && floorPaise() === null ? 'true' : null" />
              </div>
              <div>
                <label for="boli-increment">Minimum increment (₹)</label>
                <input id="boli-increment" class="input" name="minIncrement"
                  [(ngModel)]="minIncrement" inputmode="decimal" placeholder="500"
                  [attr.aria-invalid]="minIncrement && incrementPaise() === null ? 'true' : null" />
              </div>
            </div>

            <label for="boli-extend">If somebody bids in the last… (seconds)</label>
            <input id="boli-extend" class="input inline" type="number" min="0" max="3600"
              name="autoExtendSeconds" [(ngModel)]="autoExtendSeconds" />

            <p class="small">
              …then the close moves that far past the bid, and keeps moving while people keep
              bidding. Leave it at 0 and the Boli shuts on the clock — which means it can be won
              by whoever bids last rather than by whoever will pay most. Two minutes is a common
              choice; the auctioneer's "going, going" is the same idea.
            </p>

            <label for="boli-eligibility">Eligibility rule</label>
            <input id="boli-eligibility" class="input" name="eligibilityRule"
              [(ngModel)]="eligibilityRule" maxlength="500" placeholder="One per family." />

            <p class="small">
              The eligibility rule is shown to bidders and enforced by the Samaaj, not by the
              platform. Real rules are facts this service does not hold.
            </p>

            <button class="btn" type="submit" [disabled]="busy() || !canOpen()">Open Boli</button>
          </form>
        }
      </div>
    }
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .section-heading {
      margin-top: var(--space-5);
    }

    .filter-row {
      display: flex;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .filter-row > div {
      flex: 1 1 180px;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
      align-items: center;
    }

    ul.plain {
      list-style: none;
      padding: 0;
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }
  `,
})
export class OccasionDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);

  readonly occasion = signal<OccasionDetail | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  typeName = '';
  typeDescription = '';
  boliTypeId = '';
  boliTitle = '';
  startAt = '';
  endAt = '';
  startingAmount = '';
  minIncrement = '';
  eligibilityRule = '';
  autoExtendSeconds = 0;

  rupees = formatRupees;
  closes = (endAt: string) => closesIn(endAt);

  /**
   * The one status this occasion can move to, or null when it is closed.
   *
   * Offering the full list and letting the server refuse two of three would be
   * offering choices that were never there.
   */
  readonly nextStatus = computed<OccasionStatus | null>(() => {
    switch (this.occasion()?.status) {
      case 'Upcoming':
        return 'Active';
      case 'Active':
        return 'Closed';
      default:
        return null;
    }
  });

  readonly floorPaise = computed(() => parseRupees(this.startingAmount));
  readonly incrementPaise = computed(() => parseRupees(this.minIncrement));

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.load();
  }

  canOpen(): boolean {
    return (
      this.boliTypeId.length > 0 &&
      this.boliTitle.trim().length > 0 &&
      this.startAt.length > 0 &&
      this.endAt.length > 0 &&
      parseRupees(this.startingAmount) !== null &&
      parseRupees(this.minIncrement) !== null
    );
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.occasion(this.id).subscribe({
      next: (found) => {
        this.occasion.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.notFound.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  move(status: OccasionStatus): void {
    this.act(this.api.moveOccasion(this.id, status), `Occasion is now ${status}.`);
  }

  defineType(): void {
    const name = this.typeName.trim();

    if (name.length === 0) {
      return;
    }

    this.act(
      this.api.defineBoliType(this.id, name, blankToNull(this.typeDescription)),
      `${name} defined.`,
      () => {
        this.typeName = '';
        this.typeDescription = '';
      },
    );
  }

  open(): void {
    const floor = parseRupees(this.startingAmount);
    const increment = parseRupees(this.minIncrement);

    // Re-checked here as well as in `canOpen`, because these two are the values
    // that would be wrong by a factor of a hundred if they slipped through.
    if (!this.canOpen() || floor === null || increment === null) {
      return;
    }

    const title = this.boliTitle.trim();

    this.act(
      this.api.openBoli(this.id, {
        boliTypeId: this.boliTypeId,
        title,
        startAt: new Date(this.startAt).toISOString(),
        endAt: new Date(this.endAt).toISOString(),
        startingAmount: floor,
        minIncrement: increment,
        eligibilityRule: blankToNull(this.eligibilityRule),
        autoExtendSeconds: this.autoExtendSeconds,
      }),
      `${title} is open for bidding.`,
      () => {
        this.boliTitle = '';
        this.startAt = '';
        this.endAt = '';
        this.startingAmount = '';
        this.minIncrement = '';
        this.eligibilityRule = '';
      },
    );
  }

  close(lot: Boli): void {
    this.act(this.api.closeBoli(lot.id), `${lot.title} is closed.`);
  }

  record(lot: Boli): void {
    this.act(
      this.api.recordBoliResult(lot.id),
      `${lot.title} recorded. It is waiting to be announced on the Auctions screen.`,
    );
  }

  /**
   * Every action re-reads the occasion. Closing a Boli changes which buttons
   * belong on its row, defining a type changes whether the open form can be
   * offered at all, and a bid placed a second ago changes the highest amount —
   * the server is the only thing that knows all of it at once.
   */
  private act(work: { subscribe: (o: object) => void }, message: string, reset?: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        reset?.();
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed.length === 0 ? null : trimmed;
}
