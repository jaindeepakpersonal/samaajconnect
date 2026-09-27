import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError, formatRupees, parseRupees } from '@samaajconnect/shared';
import { Observable } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import { TrustExpense } from '../../core/admin.models';

/**
 * What a Trust has spent: each expense recorded, then approved.
 *
 * **Approval is its own act, and the screen shows who did each.** The audit
 * trail names the approver for the same reason - an approval is somebody
 * vouching for a payment, and a ledger where recording and approving looked
 * like one step would hide whether anybody had. The service does not require
 * the approver to be a different person, so the screen does not pretend it
 * does; it just shows both names.
 */
@Component({
  selector: 'app-trust-expenses',
  imports: [FormsModule, DatePipe],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    <div class="card">
      <h2>Record an expense</h2>

      <form (ngSubmit)="record()">
        <div class="form-grid">
          <div class="field">
            <label for="expense-category">Category</label>
            <input id="expense-category" class="input" name="category" [(ngModel)]="category"
              maxlength="100" placeholder="Maintenance" />
          </div>

          <div class="field">
            <label for="expense-amount">Amount (₹)</label>
            <input id="expense-amount" class="input" name="amount" inputmode="decimal"
              [(ngModel)]="amount" placeholder="12,500"
              [attr.aria-invalid]="amount.trim() && paise() === null ? 'true' : null" />
          </div>

          <div class="field">
            <label for="expense-vendor">Paid to</label>
            <input id="expense-vendor" class="input" name="vendor" [(ngModel)]="vendor"
              maxlength="200" placeholder="Mewar Builders" />
          </div>
        </div>

        <label for="expense-description">What it was for</label>
        <textarea id="expense-description" class="input" name="description" rows="3"
          [(ngModel)]="description" maxlength="500"></textarea>

        <div class="actions">
          <button class="btn" type="submit" [disabled]="busy() || !canRecord()">Record expense</button>
        </div>
      </form>
    </div>

    <div class="card spaced">
      <h2>Expenses</h2>

      @if (loading()) {
        <p class="empty" role="status">Loading…</p>
      } @else if (expenses().length === 0) {
        <p class="empty">Nothing spent yet.</p>
      } @else {
        <p>
          Awaiting approval: <b>{{ rupees(recordedTotal()) }}</b> ·
          Approved: <b>{{ rupees(approvedTotal()) }}</b>
        </p>

        <div class="table-wrap">
          <table>
            <caption class="sr-only">Expenses of this Trust</caption>
            <thead>
              <tr><th>Category</th><th>For</th><th>Paid to</th><th>Amount</th><th>Recorded</th><th></th></tr>
            </thead>
            <tbody>
              @for (expense of expenses(); track expense.id) {
                <tr>
                  <td><b>{{ expense.category }}</b></td>
                  <td>{{ expense.description }}</td>
                  <td>{{ expense.vendor ?? '—' }}</td>
                  <td>{{ rupees(expense.amount) }}</td>
                  <td>
                    {{ expense.recordedAt | date: 'd MMM y' }}
                    <br /><span class="small">by {{ name(expense.recordedBy) }}</span>
                  </td>
                  <td>
                    @if (expense.status === 'Approved') {
                      <span class="pill ok">Approved</span>
                      @if (expense.approvedBy; as approver) {
                        <br /><span class="small">by {{ name(approver) }}</span>
                      }
                    } @else {
                      <button class="btn small" type="button" [disabled]="busy()" (click)="approve(expense)">
                        Approve
                      </button>
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
    .spaced {
      margin-top: var(--space-4);
    }
  `,
})
export class TrustExpensesComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly trustId = input.required<string>();

  /** Account id to name, from the page. An administrator's account id is their member id. */
  readonly names = input<ReadonlyMap<string, string>>(new Map());

  readonly expenses = signal<readonly TrustExpense[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly recordedTotal = computed(() =>
    this.expenses().filter((e) => e.status === 'Recorded').reduce((sum, e) => sum + e.amount, 0),
  );

  readonly approvedTotal = computed(() =>
    this.expenses().filter((e) => e.status === 'Approved').reduce((sum, e) => sum + e.amount, 0),
  );

  category = '';
  description = '';
  amount = '';
  vendor = '';

  ngOnInit(): void {
    this.load();
  }

  rupees(paise: number): string {
    return formatRupees(paise);
  }

  name(accountId: string): string {
    return this.names().get(accountId) ?? 'an administrator';
  }

  paise(): number | null {
    const value = parseRupees(this.amount);

    return value !== null && value > 0 ? value : null;
  }

  canRecord(): boolean {
    return this.category.trim().length > 0 && this.description.trim().length > 0 && this.paise() !== null;
  }

  record(): void {
    const amount = this.paise();

    if (!this.canRecord() || amount === null) {
      return;
    }

    const category = this.category.trim();

    this.run(
      this.api.recordTrustExpense(this.trustId(), {
        category,
        description: this.description.trim(),
        amount,
        vendor: this.vendor.trim() || null,
      }),
      () => {
        this.category = '';
        this.description = '';
        this.amount = '';
        this.vendor = '';
        return `${formatRupees(amount)} recorded under ${category}. It needs approving.`;
      },
    );
  }

  approve(expense: TrustExpense): void {
    this.run(
      this.api.approveTrustExpense(expense.id),
      () => `${formatRupees(expense.amount)} for ${expense.category} is approved.`,
    );
  }

  private load(): void {
    this.api.trustExpenses(this.trustId()).subscribe({
      next: (found) => {
        this.expenses.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  private run(work: Observable<TrustExpense>, message: () => string): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message());
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
