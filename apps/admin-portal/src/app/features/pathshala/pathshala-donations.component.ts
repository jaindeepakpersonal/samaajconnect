import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError, formatRupees, parseRupees } from '@samaajconnect/shared';
import { Observable } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import { DonationLedger, DonationMode, PathshalaDonation } from '../../core/admin.models';
import { blankToNull } from '../../core/modal-keys';

/**
 * The Pathshala's own donation ledger: gifts made to this Pathshala - paid, or
 * announced and still to be paid.
 *
 * **It is not the Trust's ledger.** A Samaaj's trusts keep their own books;
 * this is the money given to a Pathshala specifically, which is what a
 * Pathshala committee is asked to account for.
 *
 * **An announcement has no receipt until it is paid.** A member often announces
 * a gift and pays later, so the form asks which, and "Mark paid" is what issues
 * the receipt number - a number on a slip always means money received. The two
 * totals (received, and announced but not yet paid) come from the service
 * rather than being summed here.
 *
 * **Delete takes an entry off the ledger, and the service keeps the row.** A
 * receipt number has been written on a slip somebody holds, so it is never
 * issued again and a reason is asked for; an announcement needs none. Deleted
 * entries stay one choice away, under Show.
 *
 * Amounts are integer paise on the wire; `parseRupees`/`formatRupees` in
 * `libs/shared` are the only conversion.
 */
@Component({
  selector: 'app-pathshala-donations',
  imports: [FormsModule, DatePipe],
  template: `
    @if (error(); as message) {
      @if (!popup()) {
        <p class="notice error" role="alert">{{ message }}</p>
      }
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    <div class="card">
      <div class="section-head">
        <h2>Ledger</h2>
        <button class="btn small" type="button" (click)="openRecord()">Record a donation</button>
      </div>

      @if (ledger(); as book) {
        <div class="totals">
          <p>Received: <b>{{ rupees(book.totalAmount) }}</b></p>
          <p>Announced, not yet paid: <b>{{ rupees(book.outstandingAmount) }}</b></p>
        </div>

        @if (book.donations.length === 0) {
          <p class="empty">Nothing recorded yet.</p>
        } @else {
          <label for="donation-show">Show</label>
          <select id="donation-show" class="input show" name="show" [ngModel]="show()" (ngModelChange)="show.set($event)">
            <option value="all">All ({{ count('all') }})</option>
            <option value="paid">Paid ({{ count('paid') }})</option>
            <option value="pending">Not paid yet ({{ count('pending') }})</option>
            <option value="deleted">Deleted ({{ count('deleted') }})</option>
          </select>

          @if (shown().length === 0) {
            <p class="empty">{{ emptyFor() }}</p>
          } @else {
            <div class="table-wrap">
              <table>
                <caption class="sr-only">Donations to this Pathshala</caption>
                <thead>
                  <tr>
                    <th>Receipt</th><th>Donor</th><th>Amount</th><th>Date</th><th>Status</th><th>Mode</th><th>Purpose</th><th></th>
                  </tr>
                </thead>
                <tbody>
                  @for (donation of shown(); track donation.id) {
                    <tr [class.deleted]="donation.isVoid">
                      <td>{{ donation.receiptNumber ?? '—' }}</td>
                      <td><b>{{ donation.donorName }}</b></td>
                      <td>{{ rupees(donation.amount) }}</td>
                      <td>
                        {{ donation.donatedOn | date: 'd MMM y' }}
                        @if (donation.paidOn && donation.paidOn !== donation.donatedOn) {
                          <span class="small"><br />paid {{ donation.paidOn | date: 'd MMM y' }}</span>
                        }
                      </td>
                      <td>
                        @if (donation.isVoid) {
                          <span class="pill off">Deleted</span>
                          @if (donation.voidReason) {
                            <span class="small"> {{ donation.voidReason }}</span>
                          }
                        } @else if (donation.status === 'Paid') {
                          <span class="pill ok">Paid</span>
                        } @else {
                          <span class="pill warn">Not paid yet</span>
                        }
                      </td>
                      <td>{{ donation.mode ? modeLabel(donation.mode) : '—' }}</td>
                      <td>{{ donation.purpose ?? '—' }}</td>
                      <td>
                        @if (!donation.isVoid) {
                          <div class="row-actions">
                            @if (donation.status === 'Pledged') {
                              <button class="btn small" type="button" [disabled]="busy()" (click)="openPaid(donation)">
                                Mark paid
                              </button>
                            }
                            <button class="btn small alt" type="button" [disabled]="busy()" (click)="openEdit(donation)">
                              Edit
                            </button>
                            <button class="btn small alt" type="button" [disabled]="busy()" (click)="openDelete(donation)">
                              Delete
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
        }
      } @else if (!error()) {
        <p class="empty" role="status">Loading…</p>
      }
    </div>

    @if (popup(); as open) {
      <div class="modal-backdrop" (click)="closePopup()">
        <div class="card modal-card modal-dialog" role="dialog" aria-modal="true"
          aria-labelledby="donation-popup-title" (click)="$event.stopPropagation()" (keydown.escape)="closePopup()">
          <h2 id="donation-popup-title">{{ popupTitle(open) }}</h2>

          @if (error(); as message) {
            <p class="notice error" role="alert">{{ message }}</p>
          }

          @switch (open.kind) {
            @case ('paid') {
              <form (ngSubmit)="markPaid()">
                <p>
                  {{ rupees(open.donation.amount) }} from <b>{{ open.donation.donorName }}</b>, announced on
                  {{ open.donation.donatedOn | date: 'd MMM y' }}. Marking it paid issues the next receipt number.
                </p>

                <div class="form-grid">
                  <div class="field">
                    <label for="paid-on">Paid on</label>
                    <input id="paid-on" class="input" type="date" name="paidOn" [(ngModel)]="paidOn"
                      [min]="open.donation.donatedOn" [max]="todayValue" />
                  </div>

                  <div class="field">
                    <label for="paid-mode">Mode</label>
                    <select id="paid-mode" class="input" name="paidMode" [(ngModel)]="mode">
                      @for (option of modes; track option.value) {
                        <option [value]="option.value">{{ option.label }}</option>
                      }
                    </select>
                  </div>
                </div>

                <div class="actions">
                  <button class="btn" type="submit" [disabled]="busy() || !paidOn">Mark paid</button>
                  <button class="btn alt" type="button" (click)="closePopup()">Cancel</button>
                </div>
              </form>
            }

            @case ('delete') {
              <form (ngSubmit)="deleteDonation()">
                @if (open.donation.receiptNumber !== null) {
                  <p>
                    Receipt {{ open.donation.receiptNumber }} from <b>{{ open.donation.donorName }}</b> leaves the
                    ledger and its total. The receipt number is not issued again, so the entry is kept under
                    "Deleted" with your reason.
                  </p>
                  <label for="delete-reason">Why</label>
                  <input id="delete-reason" class="input" name="deleteReason" [(ngModel)]="deleteReason"
                    maxlength="500" placeholder="Entered twice" />
                } @else {
                  <p>
                    The announcement of {{ rupees(open.donation.amount) }} from <b>{{ open.donation.donorName }}</b>
                    leaves the ledger. No receipt was issued for it.
                  </p>
                  <label for="delete-reason">Why (optional)</label>
                  <input id="delete-reason" class="input" name="deleteReason" [(ngModel)]="deleteReason"
                    maxlength="500" placeholder="Will not be paid" />
                }

                <div class="actions">
                  <button class="btn" type="submit" [disabled]="busy() || !canDelete(open.donation)">Delete</button>
                  <button class="btn alt" type="button" (click)="closePopup()">Keep it</button>
                </div>
              </form>
            }

            @default {
              <form (ngSubmit)="save()">
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

                  @if (open.kind === 'record') {
                    <div class="field">
                      <label for="donation-paid">Payment</label>
                      <select id="donation-paid" class="input" name="paidNow" [(ngModel)]="paidNow">
                        <option [ngValue]="true">Paid now</option>
                        <option [ngValue]="false">Announced, to be paid later</option>
                      </select>
                    </div>
                  }

                  <div class="field">
                    <label for="donation-date">{{ isPaidForm() ? 'Given on' : 'Announced on' }}</label>
                    <input id="donation-date" class="input" type="date" name="donatedOn" [(ngModel)]="donatedOn"
                      [max]="todayValue" />
                  </div>

                  @if (isPaidForm()) {
                    @if (open.kind === 'edit') {
                      <div class="field">
                        <label for="donation-paid-on">Paid on</label>
                        <input id="donation-paid-on" class="input" type="date" name="editPaidOn" [(ngModel)]="paidOn"
                          [min]="donatedOn" [max]="todayValue" />
                      </div>
                    }

                    <div class="field">
                      <label for="donation-mode">Mode</label>
                      <select id="donation-mode" class="input" name="mode" [(ngModel)]="mode">
                        @for (option of modes; track option.value) {
                          <option [value]="option.value">{{ option.label }}</option>
                        }
                      </select>
                    </div>
                  }
                </div>

                <label for="donation-purpose">Purpose</label>
                <input id="donation-purpose" class="input" name="purpose" [(ngModel)]="purpose"
                  maxlength="200" placeholder="Books for the new session" />

                @if (open.kind === 'record' && !paidNow) {
                  <p class="small">No receipt yet. Use "Mark paid" on its row when the money comes in.</p>
                }

                <div class="actions">
                  <button class="btn" type="submit" [disabled]="busy() || !canSave()">
                    {{ open.kind === 'edit' ? 'Save' : 'Record' }}
                  </button>
                  <button class="btn alt" type="button" (click)="closePopup()">Cancel</button>
                </div>
              </form>
            }
          }
        </div>
      </div>
    }
  `,
  styles: `
    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .section-head h2 {
      margin: 0;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }

    .totals {
      display: flex;
      gap: var(--space-4);
      flex-wrap: wrap;
      font-size: 16px;
    }

    .show {
      max-width: 240px;
    }

    /* Receipt, donor, amount and date: what the entry claimed. */
    tr.deleted td:nth-child(-n + 4) {
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

  /** Which form is open over the tab, and on which entry. */
  readonly popup = signal<DonationPopup | null>(null);

  /** Which entries the table shows. Deleted ones are a choice away, not mixed in. */
  readonly show = signal<DonationFilter>('all');

  readonly shown = computed(() => (this.ledger()?.donations ?? []).filter((d) => matches(d, this.show())));

  readonly modes: readonly { value: DonationMode; label: string }[] = [
    { value: 'Cash', label: 'Cash' },
    { value: 'Upi', label: 'UPI' },
    { value: 'Cheque', label: 'Cheque' },
    { value: 'BankTransfer', label: 'Bank transfer' },
    { value: 'Other', label: 'Other' },
  ];

  /** Today on this browser's calendar - the latest date any of these forms takes. */
  readonly todayValue = today();

  donorName = '';
  amount = '';
  donatedOn = today();
  paidNow = true;
  paidOn = today();
  mode: DonationMode = 'Cash';
  purpose = '';
  deleteReason = '';

  ngOnInit(): void {
    this.load();
  }

  rupees(paise: number): string {
    return formatRupees(paise);
  }

  modeLabel(mode: DonationMode): string {
    return this.modes.find((m) => m.value === mode)?.label ?? mode;
  }

  count(filter: DonationFilter): number {
    return (this.ledger()?.donations ?? []).filter((d) => matches(d, filter)).length;
  }

  emptyFor(): string {
    switch (this.show()) {
      case 'paid':
        return 'Nothing has been paid yet.';
      case 'pending':
        return 'Nothing is waiting to be paid.';
      case 'deleted':
        return 'Nothing has been deleted.';
      default:
        return 'Every entry has been deleted. Choose "Deleted" to see them.';
    }
  }

  popupTitle(open: DonationPopup): string {
    switch (open.kind) {
      case 'record':
        return 'Record a donation';
      case 'edit':
        return open.donation.receiptNumber === null ? 'Edit the announcement' : `Edit receipt ${open.donation.receiptNumber}`;
      case 'paid':
        return 'Mark it paid';
      case 'delete':
        return 'Delete this entry?';
    }
  }

  /** Whether the open form is for money received - so it asks for a mode. */
  isPaidForm(): boolean {
    const open = this.popup();

    if (open?.kind === 'edit') {
      return open.donation.status === 'Paid';
    }

    return this.paidNow;
  }

  openRecord(): void {
    this.donorName = '';
    this.amount = '';
    this.donatedOn = today();
    this.paidNow = true;
    this.mode = 'Cash';
    this.purpose = '';
    this.openPopup({ kind: 'record' });
  }

  openEdit(donation: PathshalaDonation): void {
    this.donorName = donation.donorName;
    this.amount = (donation.amount / 100).toString();
    this.donatedOn = donation.donatedOn;
    this.paidOn = donation.paidOn ?? donation.donatedOn;
    this.mode = donation.mode ?? 'Cash';
    this.purpose = donation.purpose ?? '';
    this.openPopup({ kind: 'edit', donation });
  }

  openPaid(donation: PathshalaDonation): void {
    this.paidOn = today();
    this.mode = 'Cash';
    this.openPopup({ kind: 'paid', donation });
  }

  openDelete(donation: PathshalaDonation): void {
    this.deleteReason = '';
    this.openPopup({ kind: 'delete', donation });
  }

  closePopup(): void {
    this.popup.set(null);
    this.error.set(null);
  }

  /** The amount in paise, or null while the box holds nothing sendable. */
  paise(): number | null {
    const value = parseRupees(this.amount);

    return value !== null && value > 0 ? value : null;
  }

  canSave(): boolean {
    return this.donorName.trim().length > 0 && this.paise() !== null && this.donatedOn.length > 0
      && (!(this.popup()?.kind === 'edit' && this.isPaidForm()) || this.paidOn >= this.donatedOn);
  }

  /** Once a receipt has been issued, its deletion has to say why. */
  canDelete(donation: PathshalaDonation): boolean {
    return donation.receiptNumber === null || this.deleteReason.trim().length > 0;
  }

  save(): void {
    const open = this.popup();
    const amount = this.paise();

    if (!open || !this.canSave() || amount === null) {
      return;
    }

    const donor = this.donorName.trim();

    if (open.kind === 'edit') {
      const paid = open.donation.status === 'Paid';

      this.run(
        this.api.updateDonation(open.donation.id, {
          donorName: donor,
          amount,
          donatedOn: this.donatedOn,
          mode: paid ? this.mode : null,
          paidOn: paid ? this.paidOn : null,
          purpose: blankToNull(this.purpose),
        }),
        (saved) => `${saved.receiptNumber === null ? 'The announcement' : `Receipt ${saved.receiptNumber}`} from ${donor} is corrected.`,
      );

      return;
    }

    if (open.kind !== 'record') {
      return;
    }

    const paidNow = this.paidNow;

    this.run(
      this.api.recordDonation(this.pathshalaId(), {
        donorName: donor,
        donorMemberId: null,
        amount,
        donatedOn: this.donatedOn,
        mode: paidNow ? this.mode : null,
        purpose: blankToNull(this.purpose),
        paid: paidNow,
      }),
      (saved) => saved.receiptNumber === null
        ? `${formatRupees(saved.amount)} announced by ${donor}, not yet paid.`
        : `Receipt ${saved.receiptNumber}: ${formatRupees(saved.amount)} from ${donor}.`,
    );
  }

  markPaid(): void {
    const open = this.popup();

    if (open?.kind !== 'paid' || !this.paidOn) {
      return;
    }

    const donor = open.donation.donorName;

    this.run(
      this.api.markDonationPaid(open.donation.id, this.mode, this.paidOn),
      (saved) => `Paid: receipt ${saved.receiptNumber} for ${formatRupees(saved.amount)} from ${donor}.`,
    );
  }

  deleteDonation(): void {
    const open = this.popup();

    if (open?.kind !== 'delete' || !this.canDelete(open.donation)) {
      return;
    }

    const donation = open.donation;

    this.run(
      this.api.deleteDonation(donation.id, blankToNull(this.deleteReason)),
      () => donation.receiptNumber === null
        ? `The announcement from ${donation.donorName} is deleted.`
        : `Receipt ${donation.receiptNumber} is deleted.`,
    );
  }

  private openPopup(open: DonationPopup): void {
    this.error.set(null);
    this.done.set(null);
    this.popup.set(open);
  }

  private load(): void {
    this.api.donations(this.pathshalaId()).subscribe({
      next: (found) => this.ledger.set(found),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  /** Sends one change; on success closes the popup, says what happened and re-reads the ledger. */
  private run(work: Observable<PathshalaDonation>, message: (saved: PathshalaDonation) => string): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: (saved) => {
        this.popup.set(null);
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

type DonationFilter = 'all' | 'paid' | 'pending' | 'deleted';

type DonationPopup =
  | { readonly kind: 'record' }
  | { readonly kind: 'edit'; readonly donation: PathshalaDonation }
  | { readonly kind: 'paid'; readonly donation: PathshalaDonation }
  | { readonly kind: 'delete'; readonly donation: PathshalaDonation };

/** "All" is every entry still on the ledger; deleted ones are shown only when asked for. */
function matches(donation: PathshalaDonation, filter: DonationFilter): boolean {
  switch (filter) {
    case 'deleted':
      return donation.isVoid;
    case 'paid':
      return !donation.isVoid && donation.status === 'Paid';
    case 'pending':
      return !donation.isVoid && donation.status === 'Pledged';
    default:
      return !donation.isVoid;
  }
}

function today(): string {
  const now = new Date();
  const month = `${now.getMonth() + 1}`.padStart(2, '0');
  const day = `${now.getDate()}`.padStart(2, '0');

  return `${now.getFullYear()}-${month}-${day}`;
}
