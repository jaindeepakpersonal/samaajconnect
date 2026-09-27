import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError, formatRupees } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { TrustCollection } from '../../core/admin.models';

/**
 * Money owed to the Trust, and recording it once it is paid.
 *
 * **Nothing here is created by hand.** An amount falls due when a pledge is
 * recorded on the Donations tab, or when a Boli in this Trust's locality has
 * its result published - trust-service opens a Collection against the winner,
 * once, however often the announcement is redelivered. So the screen has no
 * "add" form - a figure typed here would be a debt nobody incurred.
 *
 * **Paying a pledge here collects the donation**, receipt number and all, and
 * collecting it on the Donations tab settles it here: one fact, two places.
 *
 * **A reminder is a notification to the member who owes it**, at most one a
 * day, and the service refuses both a second one and one to a donor with no
 * account. The button mirrors the day rule so it is not a control that always
 * answers 409, and is not drawn at all for somebody who can only be phoned.
 *
 * **Recording a payment asks how it was paid** and nothing else; the amount is
 * the winning bid, which is not the Trust's to change.
 */
@Component({
  selector: 'app-trust-collections',
  imports: [FormsModule, DatePipe],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    <div class="card">
      <h2>Collections</h2>
      <p class="small">
        Amounts fall due here when a Boli in this Trust's locality has its result announced,
        owed by the winner.
      </p>

      @if (loading()) {
        <p class="empty" role="status">Loading…</p>
      } @else if (collections().length === 0) {
        <p class="empty">Nothing is owed to this Trust yet.</p>
      } @else {
        <p>
          Still due: <b>{{ rupees(dueTotal()) }}</b> · Collected: <b>{{ rupees(collectedTotal()) }}</b>
        </p>

        <div class="table-wrap">
          <table>
            <caption class="sr-only">Amounts owed to this Trust</caption>
            <thead>
              <tr><th>Owed by</th><th>For</th><th>Amount</th><th>Due since</th><th>Payment</th></tr>
            </thead>
            <tbody>
              @for (collection of collections(); track collection.id) {
                <tr>
                  <td><b>{{ payer(collection) }}</b></td>
                  <td>
                    {{ collection.sourceType === 'BoliResult' ? 'A Boli they won' : 'A pledged donation' }}
                    @if (collection.lastRemindedAt; as reminded) {
                      <br /><span class="small">Reminded {{ reminded | date: 'd MMM, h:mm a' }}</span>
                    }
                  </td>
                  <td>{{ rupees(collection.amount) }}</td>
                  <td>{{ collection.createdAt | date: 'd MMM y' }}</td>
                  <td>
                    @if (collection.status === 'Collected') {
                      <span class="pill ok">Paid · {{ collection.mode }}</span>
                      <span class="small"> {{ collection.collectedAt | date: 'd MMM y' }}</span>
                    } @else {
                      <div class="pay">
                        <label class="sr-only" [attr.for]="'mode-' + collection.id">
                          How {{ payer(collection) }} paid
                        </label>
                        <select class="input inline" [id]="'mode-' + collection.id"
                          [name]="'mode-' + collection.id" [(ngModel)]="modes[collection.id]">
                          @for (option of paymentModes; track option) {
                            <option [value]="option">{{ option }}</option>
                          }
                        </select>
                        <button class="btn small" type="button" [disabled]="busy()" (click)="pay(collection)">
                          Record payment
                        </button>
                        @if (collection.payerMemberId) {
                          <button class="btn small alt" type="button"
                            [disabled]="busy() || remindedToday(collection)"
                            [attr.title]="remindedToday(collection) ? 'Reminded in the last day' : null"
                            (click)="remind(collection)">
                            Send a reminder
                          </button>
                        }
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
  `,
  styles: `
    .pay {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
      align-items: center;
    }
  `,
})
export class TrustCollectionsComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly trustId = input.required<string>();

  /** Member id to name, from the page. */
  readonly names = input<ReadonlyMap<string, string>>(new Map());

  readonly collections = signal<readonly TrustCollection[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly paymentModes = ['Cash', 'UPI', 'Cheque', 'Bank transfer'] as const;

  /** Collection id to the mode picked for it; seeded so each select shows a value. */
  modes: Record<string, string> = {};

  readonly dueTotal = computed(() =>
    this.collections().filter((c) => c.status === 'Due').reduce((sum, c) => sum + c.amount, 0),
  );

  readonly collectedTotal = computed(() =>
    this.collections().filter((c) => c.status === 'Collected').reduce((sum, c) => sum + c.amount, 0),
  );

  ngOnInit(): void {
    this.load();
  }

  rupees(paise: number): string {
    return formatRupees(paise);
  }

  payer(collection: TrustCollection): string {
    return (
      (collection.payerMemberId ? this.names().get(collection.payerMemberId) : undefined) ??
      collection.payerName ??
      'A member'
    );
  }

  /** The service allows one reminder a day; this mirrors it for the button. */
  remindedToday(collection: TrustCollection): boolean {
    return (
      collection.lastRemindedAt !== null &&
      Date.now() - Date.parse(collection.lastRemindedAt) < 24 * 60 * 60 * 1000
    );
  }

  remind(collection: TrustCollection): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.remindTrustPayer(collection.id).subscribe({
      next: () => {
        this.done.set(`${this.payer(collection)} has been sent a reminder.`);
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  pay(collection: TrustCollection): void {
    const mode = this.modes[collection.id] ?? 'Cash';

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.recordTrustPayment(collection.id, mode).subscribe({
      next: () => {
        this.done.set(`${formatRupees(collection.amount)} from ${this.payer(collection)} recorded as paid.`);
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  private load(): void {
    this.api.trustCollections(this.trustId()).subscribe({
      next: (found) => {
        this.collections.set(found);
        this.loading.set(false);

        for (const collection of found) {
          this.modes[collection.id] ??= 'Cash';
        }
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }
}
