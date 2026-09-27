import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError, formatRupees, parseRupees } from '@samaajconnect/shared';
import { Observable } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import { AdminMember, TrustDonation, TrustDonationMode } from '../../core/admin.models';

/** What trust-service accepts for a donor's phone, character for character. */
const PHONE = /^\+?[0-9 ()-]{7,20}$/;

/**
 * A Trust's donations: pledges recorded, then collected against a receipt.
 *
 * **A pledge has no receipt number, and that is the point of the two steps.**
 * Somebody promises ₹51,000 at a gathering; the money arrives later. The
 * receipt number is issued when it is collected - the Trust's own sequence,
 * numbered by the service under a lock so two collections at once cannot share
 * one - so a number on a slip always means money received.
 *
 * **The totals come apart the same way**: what was promised and what arrived
 * are different questions a Trust committee is asked, and adding them together
 * would answer neither.
 *
 * **A donor is picked from the directory when they are a member**, the same
 * search Add a trustee uses, so the pledge is owed by their account: it shows
 * on their own Trusts screen and they can be sent a reminder from Collections.
 * Somebody with no account is typed by name instead, and is phoned.
 */
@Component({
  selector: 'app-trust-donations',
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
          <div class="field span-all">
            @if (donorMember(); as picked) {
              <span class="label">Donor</span>
              <p class="picked">
                <b>{{ picked.fullName }}</b>, a member
                <button class="btn link" type="button" (click)="clearDonor()">Change</button>
              </p>
            } @else {
              <label for="donor-search">Donor who is a member</label>
              <div class="search-row">
                <input id="donor-search" class="input" name="donorSearch" [(ngModel)]="donorSearch"
                  placeholder="Name or mobile" (keydown.enter)="$event.preventDefault(); searchDonors()" />
                <button class="btn alt" type="button" [disabled]="searching() || !donorSearch.trim()"
                  (click)="searchDonors()">
                  Search
                </button>
              </div>

              @if (donorResults().length > 0) {
                <ul class="suggestions">
                  @for (member of donorResults(); track member.id) {
                    <li>
                      <button class="btn link" type="button" (click)="pickDonor(member)">
                        {{ member.fullName }}@if (member.mobile) { · {{ member.mobile }} }
                      </button>
                    </li>
                  }
                </ul>
              } @else if (searched()) {
                <p class="small">No member matches. Type their name below instead.</p>
              }

              <label for="donor-name">Or a donor with no account</label>
              <input id="donor-name" class="input" name="donorName" [(ngModel)]="donorName"
                maxlength="200" placeholder="Shri Mahendra Jain" />
            }
          </div>

          <div class="field">
            <label for="donor-phone">Donor's phone</label>
            <input id="donor-phone" class="input" type="tel" name="donorPhone" [(ngModel)]="donorPhone"
              maxlength="20" [attr.aria-invalid]="phoneInvalid() ? 'true' : null" />
          </div>

          <div class="field">
            <label for="donation-amount">Amount (₹)</label>
            <input id="donation-amount" class="input" name="amount" inputmode="decimal"
              [(ngModel)]="amount" placeholder="51,000"
              [attr.aria-invalid]="amount.trim() && paise() === null ? 'true' : null" />
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
          maxlength="200" placeholder="Temple renovation" />

        <p class="small">It is recorded as pledged. Mark it collected when the money arrives, and it gets a receipt number then.</p>

        <div class="actions">
          <button class="btn" type="submit" [disabled]="busy() || !canRecord()">Record pledge</button>
        </div>
      </form>
    </div>

    <div class="card spaced">
      <h2>Donations</h2>

      @if (loading()) {
        <p class="empty" role="status">Loading…</p>
      } @else if (donations().length === 0) {
        <p class="empty">Nothing recorded yet.</p>
      } @else {
        <p class="totals">
          Pledged, not yet collected: <b>{{ rupees(pledgedTotal()) }}</b> ·
          Collected: <b>{{ rupees(collectedTotal()) }}</b>
        </p>

        <div class="table-wrap">
          <table>
            <caption class="sr-only">Donations to this Trust</caption>
            <thead>
              <tr><th>Receipt</th><th>Donor</th><th>Amount</th><th>Purpose</th><th>Mode</th><th>Recorded</th><th></th></tr>
            </thead>
            <tbody>
              @for (donation of donations(); track donation.id) {
                <tr>
                  <td>{{ donation.receiptNumber ?? '—' }}</td>
                  <td>
                    <b>{{ donorLabel(donation) }}</b>
                    @if (donation.donorPhone; as phone) {
                      <br /><a [href]="'tel:' + phone">{{ phone }}</a>
                    }
                  </td>
                  <td>{{ rupees(donation.amount) }}</td>
                  <td>{{ donation.purpose }}</td>
                  <td>{{ modeLabel(donation.mode) }}</td>
                  <td>{{ donation.createdAt | date: 'd MMM y' }}</td>
                  <td>
                    @if (donation.status === 'Collected') {
                      <span class="pill ok">Collected {{ donation.collectedAt | date: 'd MMM' }}</span>
                    } @else {
                      <button class="btn small" type="button" [disabled]="busy()" (click)="collect(donation)">
                        Mark collected
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

    .totals {
      font-size: 15px;
    }

    .label {
      display: block;
      font-weight: 600;
      margin-top: var(--space-3);
    }

    .picked {
      margin: var(--space-1) 0 0;
    }

    .search-row {
      display: flex;
      gap: var(--space-2);
      align-items: flex-start;
    }

    .search-row .input {
      flex: 1;
    }

    .suggestions {
      list-style: none;
      margin: var(--space-2) 0;
      padding: 0;
      border: 1px solid var(--line);
      border-radius: var(--radius-sm);
      max-height: 220px;
      overflow-y: auto;
    }

    .suggestions li + li {
      border-top: 1px solid var(--line);
    }

    .suggestions button {
      display: block;
      width: 100%;
      text-align: left;
      padding: var(--space-2) var(--space-3);
    }
  `,
})
export class TrustDonationsComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly trustId = input.required<string>();

  /** Member id to name, from the page, for a donor who is a member. */
  readonly names = input<ReadonlyMap<string, string>>(new Map());

  readonly donations = signal<readonly TrustDonation[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly pledgedTotal = computed(() =>
    this.donations().filter((d) => d.status === 'Pledged').reduce((sum, d) => sum + d.amount, 0),
  );

  readonly collectedTotal = computed(() =>
    this.donations().filter((d) => d.status === 'Collected').reduce((sum, d) => sum + d.amount, 0),
  );

  readonly modes: readonly { value: TrustDonationMode; label: string }[] = [
    { value: 'Cash', label: 'Cash' },
    { value: 'Upi', label: 'UPI' },
    { value: 'Cheque', label: 'Cheque' },
    { value: 'BankTransfer', label: 'Bank transfer' },
  ];

  donorName = '';
  donorPhone = '';

  donorSearch = '';
  readonly donorMember = signal<AdminMember | null>(null);
  readonly donorResults = signal<readonly AdminMember[]>([]);
  readonly searching = signal(false);
  readonly searched = signal(false);
  amount = '';
  mode: TrustDonationMode = 'Cash';
  purpose = '';

  ngOnInit(): void {
    this.load();
  }

  rupees(paise: number): string {
    return formatRupees(paise);
  }

  modeLabel(mode: TrustDonationMode): string {
    return this.modes.find((m) => m.value === mode)?.label ?? mode;
  }

  donorLabel(donation: TrustDonation): string {
    return (
      donation.donorName ??
      (donation.donorMemberId ? this.names().get(donation.donorMemberId) : undefined) ??
      'A member'
    );
  }

  /** The amount in paise, or null while the box holds nothing sendable. */
  paise(): number | null {
    const value = parseRupees(this.amount);

    return value !== null && value > 0 ? value : null;
  }

  phoneInvalid(): boolean {
    const phone = this.donorPhone.trim();

    return phone.length > 0 && !PHONE.test(phone);
  }

  canRecord(): boolean {
    return (
      (this.donorMember() !== null || this.donorName.trim().length > 0) &&
      this.purpose.trim().length > 0 &&
      this.paise() !== null &&
      !this.phoneInvalid()
    );
  }

  record(): void {
    const amount = this.paise();

    if (!this.canRecord() || amount === null) {
      return;
    }

    const member = this.donorMember();
    const donor = member?.fullName ?? this.donorName.trim();

    this.run(
      this.api.recordTrustDonation(this.trustId(), {
        donorMemberId: member?.id ?? null,
        donorName: donor,
        donorPhone: this.donorPhone.trim() || null,
        amount,
        purpose: this.purpose.trim(),
        mode: this.mode,
      }),
      () => {
        this.donorName = '';
        this.donorPhone = '';
        this.clearDonor();
        this.amount = '';
        this.purpose = '';
        return `${formatRupees(amount)} pledged by ${donor}.`;
      },
    );
  }

  collect(donation: TrustDonation): void {
    this.run(
      this.api.collectTrustDonation(donation.id),
      (collected) => `Collected. Receipt ${collected?.receiptNumber ?? ''} for ${this.donorLabel(donation)}.`,
    );
  }

  searchDonors(): void {
    const term = this.donorSearch.trim();

    if (term.length === 0) {
      return;
    }

    this.searching.set(true);

    this.api.searchMembers(term, null, 0, 10).subscribe({
      next: (page) => {
        this.donorResults.set(page.items);
        this.searched.set(true);
        this.searching.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.searching.set(false);
      },
    });
  }

  /** Their mobile starts the phone box, since the Trust will call the number it has. */
  pickDonor(member: AdminMember): void {
    this.donorMember.set(member);
    this.donorName = '';
    this.donorPhone ||= member.mobile ?? '';
    this.donorResults.set([]);
    this.searched.set(false);
  }

  clearDonor(): void {
    this.donorMember.set(null);
    this.donorSearch = '';
    this.donorResults.set([]);
    this.searched.set(false);
  }

  private load(): void {
    this.api.trustDonations(this.trustId()).subscribe({
      next: (found) => {
        this.donations.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  private run(work: Observable<TrustDonation>, message: (saved: TrustDonation | undefined) => string): void {
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
