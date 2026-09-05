import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError, formatRupees } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { isForbidden, isNotFound } from '../../core/http-status';
import { Occasion, PendingResult } from '../../core/admin.models';

/**
 * Auctions / Boli — the occasions, and the results waiting to be announced.
 *
 * Wireframe `#boli`, and `#publishboli` folded into it (see below).
 *
 * **Members could bid and nobody could run an auction.** Seven endpoints —
 * creating an occasion, moving its status, defining a Boli type, opening a Boli,
 * closing it, recording a result and announcing it — were complete, tested, and
 * reachable only by curl. A Samaaj could not hold a Boli at all.
 *
 * **The publication queue needed an endpoint that did not exist.** Recording a
 * result and announcing it are deliberately two acts, so a result sits between
 * them; but the only read that reached one needed the Boli id you were looking
 * for. Finding what was waiting meant walking every occasion, then every Boli,
 * asking each for a result — so the middle state of the platform's most
 * deliberate workflow was invisible, and a result was announced only if somebody
 * remembered it. `GET /v1/boli/results/pending` is that queue.
 *
 * **The queue names an amount and not a winner, and the wireframe drew a
 * winner.** boli-service names the winner in exactly one shape, only once it is
 * published, for everybody including the manager who recorded it. Nothing is
 * lost by it: the winner is read from the highest bid and is not something the
 * publisher chooses, and the amount is what identifies that bid.
 */
@Component({
  selector: 'app-boli-list',
  imports: [FormsModule, DatePipe, RouterLink],
  template: `
    <h1 class="title">Auctions / Boli</h1>
    <p class="sub">Occasion → Boli type → bids → result → publish.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the Boli module. Switch it on under the Samaaj's settings
        to hold auctions.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else {
      <!-- Awaiting publication ------------------------------------------- -->
      <div class="card">
        <h2>Results awaiting publication</h2>

        @if (pendingUnavailable()) {
          <p class="empty">
            You may run a Boli but not announce its result, so this queue belongs to somebody
            else.
          </p>
        } @else if (pending().length === 0) {
          <p class="empty">Nothing is waiting. Recorded results appear here until announced.</p>
        } @else {
          <p class="small">
            Announcing a result names the winner to the whole Samaaj and cannot be undone here.
            A correction afterwards is a separate, audited workflow.
          </p>

          <div class="table-wrap">
            <table>
              <caption class="sr-only">Results recorded and waiting to be announced</caption>
              <thead>
                <tr><th>Boli</th><th>Winning bid</th><th>Recorded</th><th></th></tr>
              </thead>
              <tbody>
                @for (result of pending(); track result.boliId) {
                  <tr>
                    <td>
                      <a [routerLink]="['/boli', result.occasionId]"><b>{{ result.boliTitle }}</b></a>
                    </td>
                    <td>{{ rupees(result.amount) }}</td>
                    <td>{{ result.recordedAt | date: 'd MMM y, h:mm a' }}</td>
                    <td>
                      <!--
                        The trigger stays in the DOM, and enabled, when the
                        confirmation opens. Swapping it out destroyed the focused
                        element, which drops keyboard focus to the body and loses
                        a keyboard user's place entirely (WCAG 2.4.3) - and
                        disabling it instead does the same thing, because a
                        disabled control is blurred and taken out of the tab
                        order. Pressing it again simply re-opens what is already
                        open.
                      -->
                      <button
                        class="btn"
                        type="button"
                        [disabled]="busy()"
                        [attr.aria-expanded]="confirming() === result.boliId"
                        (click)="confirming.set(result.boliId)"
                      >
                        Review and publish
                      </button>

                      @if (confirming() === result.boliId) {
                        <div class="notice" role="status">
                          <p>
                            Publishing {{ rupees(result.amount) }} for
                            <b>{{ result.boliTitle }}</b> announces the highest bidder as the
                            winner. This is irreversible through this panel.
                          </p>
                          <div class="row-actions">
                            <button class="btn" type="button" [disabled]="busy()"
                              (click)="publish(result)">
                              Confirm and publish
                            </button>
                            <button class="btn alt" type="button" (click)="confirming.set(null)">
                              Cancel
                            </button>
                          </div>
                        </div>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <!-- Occasions ------------------------------------------------------ -->
      <div class="card spaced">
        <h2>Occasions</h2>

        @if (occasions().length === 0) {
          <p class="empty">
            No occasions yet. An occasion is the event the Boli belong to — Paryushan, a temple
            anniversary, a fundraiser.
          </p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Boli occasions</caption>
              <thead>
                <tr><th>Occasion</th><th>Date</th><th>Status</th><th>Types</th><th>Boli</th></tr>
              </thead>
              <tbody>
                @for (occasion of occasions(); track occasion.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/boli', occasion.id]"><b>{{ occasion.title }}</b></a>
                    </td>
                    <td>{{ occasion.occasionDate }}</td>
                    <td>{{ occasion.status }}</td>
                    <td>{{ occasion.typeCount }}</td>
                    <td>{{ occasion.boliCount }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        <form (ngSubmit)="create()">
          <h3 class="section-heading">Announce an occasion</h3>

          <label for="occasion-title">Title</label>
          <input id="occasion-title" class="input" name="title" [(ngModel)]="title"
            maxlength="200" placeholder="Paryushan 2026" />

          <label for="occasion-description">Description</label>
          <input id="occasion-description" class="input" name="description"
            [(ngModel)]="description" maxlength="500" />

          <label for="occasion-date">Date</label>
          <input id="occasion-date" class="input inline" type="date" name="occasionDate"
            [(ngModel)]="occasionDate" />

          <p class="small">It starts Upcoming. Nothing under it takes bids until you make it active.</p>

          <button class="btn" type="submit" [disabled]="busy() || !canCreate()">
            Announce occasion
          </button>
        </form>
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

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
      margin-top: var(--space-2);
    }
  `,
})
export class BoliListComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);

  readonly occasions = signal<readonly Occasion[]>([]);
  readonly pending = signal<readonly PendingResult[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  /**
   * Which row is showing its confirmation, if any.
   *
   * The wireframe made publishing a screen of its own with an irreversibility
   * notice on it. It is inline here because the row already carries everything
   * that screen showed — title, amount, when it was recorded — and there is no
   * endpoint that reads one pending result, so a separate route would have to
   * re-fetch the whole queue to draw one row of it. What the wireframe was
   * actually buying is the deliberate second click and the warning, and both
   * are here.
   */
  readonly confirming = signal<string | null>(null);

  /**
   * True when the queue came back 403: this caller may run a Boli but not
   * announce one. Distinct from an empty queue, which is the far more common
   * case and means something quite different.
   */
  readonly pendingUnavailable = signal(false);

  title = '';
  description = '';
  occasionDate = '';

  /** Paise to rupees, via the one place this repo converts money. */
  rupees = formatRupees;

  ngOnInit(): void {
    this.load();
  }

  canCreate(): boolean {
    return this.title.trim().length > 0 && this.occasionDate.length > 0;
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.listOccasions().subscribe({
      next: (found) => {
        this.occasions.set(found);
        this.loading.set(false);
        this.loadPending();
      },
      error: (failure: unknown) => {
        // The gateway answers 404 for a Samaaj that has switched the module
        // off, so a Samaaj without Boli is indistinguishable from a platform
        // with no such feature. Reporting it as an error would send an
        // administrator hunting a bug that is a setting.
        if (isNotFound(failure)) {
          this.moduleOff.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  private loadPending(): void {
    this.api.pendingBoliResults().subscribe({
      next: (found) => {
        this.pending.set(found);
        this.pendingUnavailable.set(false);
      },
      error: (failure: unknown) => {
        if (isForbidden(failure)) {
          this.pendingUnavailable.set(true);
          this.pending.set([]);
        } else {
          this.error.set(describeError(failure));
        }
      },
    });
  }

  create(): void {
    if (!this.canCreate()) {
      return;
    }

    const title = this.title.trim();

    this.act(
      this.api.createOccasion(title, blankToNull(this.description), this.occasionDate),
      `${title} announced.`,
      () => {
        this.title = '';
        this.description = '';
        this.occasionDate = '';
      },
    );
  }

  publish(result: PendingResult): void {
    this.confirming.set(null);

    this.act(
      this.api.publishBoliResult(result.boliId),
      `${result.boliTitle} announced at ${formatRupees(result.amount)}.`,
    );
  }

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
