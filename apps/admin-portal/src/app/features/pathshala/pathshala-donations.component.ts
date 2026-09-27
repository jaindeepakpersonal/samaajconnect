import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError, formatRupees, parseRupees } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { DonationLedger, DonationMode, PathshalaDonation } from '../../core/admin.models';
import { blankToNull } from '../../core/modal-keys';

/**
 * The Pathshala's own donation ledger: gifts made to this Pathshala, recorded
 * with a receipt number, and never deleted.
 *
 * **It is not the Trust's ledger.** A Samaaj's trusts keep their own books;
 * this is the money given to a Pathshala specifically, which is what a
 * Pathshala committee is asked to account for.
 *
 * **A mistake is voided, with a reason, not removed.** A receipt number has
 * been written on a slip somebody holds, so the entry stays on the ledger,
 * struck through, and the total leaves it out. That total comes from the
 * service rather than being summed here.
 *
 * Amounts are integer paise on the wire; `parseRupees`/`formatRupees` in
 * `libs/shared` are the only conversion.
 */
@Component({
  selector: 'app-pathshala-donations',
  imports: [FormsModule, DatePipe],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    <div class="card">
      <h2>Record a donation</h2>

      <form (ngSubmit)="record()">
        <div class="form-grid">
          <div class="field">
            <label for="donor-name">Donor</label>
            <input id="donor-name" class="input" name="donorName" [(ngModel)]="donorName"
              maxlength="200" placeholder="Shri Mahendra Jain" />
          </div>

          <div class="field">
            <label for="donation-amount">Amount (₹)</label>
            <input id="donation-amount" class="input" name="amount" inputmode="decimal"
              [(ngModel)]="amount" placeholder="5,100"
              [attr.aria-invalid]="amount.trim() && !paise() ? 'true' : null" />
          </div>

          <div class="field">
            <label for="donation-date">Given on</label>
            <input id="donation-date" class="input" type="date" name="donatedOn" [(ngModel)]="donatedOn" />
          </div>

          <div class="field">
            <label for="donation-mode">Mode</label>
            <select id="donation-mode" class="input" name="mode" [(ngModel)]="mode">
              @for (option of modes; track option.value) {
                <option [value]="option.value">{{ option.label }}</option>
              }
            </select>
          </div>
        </div>

        <label for="donation-purpose">Purpose</label>
        <input id="donation-purpose" class="input" name="purpose" [(ngModel)]="purpose"
          maxlength="200" placeholder="Books for the new session" />

        <div class="actions">
          <button class="btn" type="submit" [disabled]="busy() || !canRecord()">Record</button>
        </div>
      </form>
    </div>

    <div class="card spaced">
      <h2>Ledger</h2>

      @if (voiding(); as donation) {
        <form class="notice" role="status" (ngSubmit)="voidDonation(donation)">
          <p>
            Receipt {{ donation.receiptNumber }} from <b>{{ donation.donorName }}</b> stays on the
            ledger, marked void, and leaves the total. This cannot be undone.
          </p>
          <label for="void-reason">Why</label>
          <input id="void-reason" class="input" name="voidReason" [(ngModel)]="voidReason"
            maxlength="500" placeholder="Entered twice" />
          <div class="row-actions">
            <button class="btn" type="submit" [disabled]="busy() || !voidReason.trim()">Void it</button>
            <button class="btn alt" type="button" (click)="voiding.set(null)">Keep it</button>
          </div>
        </form>
      }

      @if (ledger(); as book) {
        <p class="total">Total received: <b>{{ rupees(book.totalAmount) }}</b></p>

        @if (book.donations.length === 0) {
          <p class="empty">Nothing recorded yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Donations to this Pathshala</caption>
              <thead>
                <tr><th>Receipt</th><th>Donor</th><th>Amount</th><th>Given on</th><th>Mode</th><th>Purpose</th><th></th></tr>
              </thead>
              <tbody>
                @for (donation of book.donations; track donation.id) {
                  <tr [class.void]="donation.isVoid">
                    <td>{{ donation.receiptNumber }}</td>
                    <td><b>{{ donation.donorName }}</b></td>
                    <td>{{ rupees(donation.amount) }}</td>
                    <td>{{ donation.donatedOn | date: 'd MMM y' }}</td>
                    <td>{{ modeLabel(donation.mode) }}</td>
                    <td>{{ donation.purpose ?? '—' }}</td>
                    <td>
                      @if (donation.isVoid) {
                        <span class="pill off">Void</span>
                        <span class="small"> {{ donation.voidReason }}</span>
                      } @else {
                        <button class="btn small alt" type="button" [disabled]="busy()"
                          [attr.aria-expanded]="voiding()?.id === donation.id"
                          (click)="startVoid(donation)">
                          Void…
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      } @else if (!error()) {
        <p class="empty" role="status">Loading…</p>
      }
    </div>
  `,
  styles: `
    .spaced {
      margin-top: var(--space-4);
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }

    .total {
      font-size: 16px;
    }

    tr.void td:not(:last-child) {
      text-decoration: line-through;
      color: var(--muted);
    }
  `,
})
export class PathshalaDonationsComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly pathshalaId = input.required<string>();

  readonly ledger = signal<DonationLedger | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);
  readonly voiding = signal<PathshalaDonation | null>(null);

  readonly modes: readonly { value: DonationMode; label: string }[] = [
    { value: 'Cash', label: 'Cash' },
    { value: 'Upi', label: 'UPI' },
    { value: 'Cheque', label: 'Cheque' },
    { value: 'BankTransfer', label: 'Bank transfer' },
    { value: 'Other', label: 'Other' },
  ];

  donorName = '';
  amount = '';
  donatedOn = today();
  mode: DonationMode = 'Cash';
  purpose = '';
  voidReason = '';

  ngOnInit(): void {
    this.load();
  }

  rupees(paise: number): string {
    return formatRupees(paise);
  }

  modeLabel(mode: DonationMode): string {
    return this.modes.find((m) => m.value === mode)?.label ?? mode;
  }

  /** The amount in paise, or null while the box holds nothing sendable. */
  paise(): number | null {
    const value = parseRupees(this.amount);

    return value !== null && value > 0 ? value : null;
  }

  canRecord(): boolean {
    return this.donorName.trim().length > 0 && this.paise() !== null && this.donatedOn.length > 0;
  }

  record(): void {
    const amount = this.paise();

    if (!this.canRecord() || amount === null) {
      return;
    }

    const donor = this.donorName.trim();

    this.run(
      this.api.recordDonation(this.pathshalaId(), {
        donorName: donor,
        donorMemberId: null,
        amount,
        donatedOn: this.donatedOn,
        mode: this.mode,
        purpose: blankToNull(this.purpose),
      }),
      (saved) => {
        this.donorName = '';
        this.amount = '';
        this.purpose = '';
        return `Receipt ${saved.receiptNumber}: ${formatRupees(saved.amount)} from ${donor}.`;
      },
    );
  }

  startVoid(donation: PathshalaDonation): void {
    this.voidReason = '';
    this.voiding.set(donation);
  }

  voidDonation(donation: PathshalaDonation): void {
    const reason = this.voidReason.trim();

    if (reason.length === 0) {
      return;
    }

    this.voiding.set(null);
    this.run(
      this.api.voidDonation(donation.id, reason),
      () => `Receipt ${donation.receiptNumber} is void.`,
    );
  }

  private load(): void {
    this.api.donations(this.pathshalaId()).subscribe({
      next: (found) => this.ledger.set(found),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  private run(
    work: { subscribe: (o: { next: (saved: PathshalaDonation) => void; error: (e: unknown) => void }) => void },
    message: (saved: PathshalaDonation) => string,
  ): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: (saved) => {
        this.done.set(message(saved));
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

function today(): string {
  const now = new Date();
  const month = `${now.getMonth() + 1}`.padStart(2, '0');
  const day = `${now.getDate()}`.padStart(2, '0');

  return `${now.getFullYear()}-${month}-${day}`;
}
