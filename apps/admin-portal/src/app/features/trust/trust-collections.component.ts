import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError, formatRupees } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { TrustCollection } from '../../core/admin.models';

/**
 * Money owed to the Trust, and recording it once it is paid.
 *
 * **Nothing here is created by hand.** An amount falls due when a Boli in this
 * Trust's locality has its result published: boli-service announces it and
 * trust-service opens a Collection against the winner, once, however often the
 * announcement is redelivered. So the screen has no "add" form - a figure typed
 * here would be a debt nobody incurred.
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
                  <td>{{ collection.sourceType === 'BoliResult' ? 'A Boli they won' : 'A pledged donation' }}</td>
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
    return (collection.payerMemberId ? this.names().get(collection.payerMemberId) : undefined) ?? 'A member';
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
