import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_CONFIG } from '@samaajconnect/shared';
import { PathshalaDonationsComponent } from './pathshala-donations.component';
import { DonationLedger, PathshalaDonation } from '../../core/admin.models';

const DONATIONS = '/v1/pathshala/pathshalas/p1/donations';

function donation(overrides: Partial<PathshalaDonation> = {}): PathshalaDonation {
  return {
    id: 'd1',
    status: 'Paid',
    receiptNumber: 1,
    donorName: 'Shri Mahendra Jain',
    donorMemberId: null,
    amount: 510000,
    donatedOn: '2026-09-20',
    mode: 'Upi',
    paidOn: '2026-09-20',
    purpose: 'Books',
    recordedBy: 'u1',
    recordedAt: '2026-09-20T10:00:00Z',
    updatedAt: null,
    isVoid: false,
    voidReason: null,
    ...overrides,
  };
}

/** Announced at a gathering, to be paid later: no receipt, no mode. */
function pledge(overrides: Partial<PathshalaDonation> = {}): PathshalaDonation {
  return donation({
    id: 'd2',
    status: 'Pledged',
    receiptNumber: null,
    donorName: 'Smt. Kavita Jain',
    amount: 210000,
    mode: null,
    paidOn: null,
    ...overrides,
  });
}

function ledgerOf(...donations: PathshalaDonation[]): DonationLedger {
  const standing = donations.filter((d) => !d.isVoid);

  return {
    donations,
    totalAmount: standing.filter((d) => d.status === 'Paid').reduce((sum, d) => sum + d.amount, 0),
    outstandingAmount: standing.filter((d) => d.status === 'Pledged').reduce((sum, d) => sum + d.amount, 0),
  };
}

describe('PathshalaDonationsComponent', () => {
  let fixture: ComponentFixture<PathshalaDonationsComponent>;
  let component: PathshalaDonationsComponent;
  let http: HttpTestingController;

  const text = () => fixture.nativeElement.textContent as string;

  /** The buttons on one table row, by their label. */
  const rowButtons = (index: number) =>
    [...fixture.nativeElement.querySelectorAll('tbody tr')[index]!.querySelectorAll('button')].map(
      (b) => (b as HTMLButtonElement).textContent!.trim(),
    );

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PathshalaDonationsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PathshalaDonationsComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => http.verify());

  function load(ledger: DonationLedger = ledgerOf(donation())) {
    fixture.componentRef.setInput('pathshalaId', 'p1');
    fixture.detectChanges();
    http.expectOne(DONATIONS).flush(ledger);
    fixture.detectChanges();
  }

  /** Answers the re-read every change fires. */
  function reload(ledger: DonationLedger = ledgerOf()) {
    http.expectOne(DONATIONS).flush(ledger);
    fixture.detectChanges();
  }

  it('shows what was received and what is announced but not yet paid, as the service totals them', () => {
    // The totals come from the service; a second sum here would be a second
    // copy of the rule about which entries count.
    load({ donations: [donation(), pledge()], totalAmount: 510000, outstandingAmount: 210000 });

    expect(text()).toContain('Received: ₹5,100');
    expect(text()).toContain('Announced, not yet paid: ₹2,100');
    expect(text()).toContain('Not paid yet');
    expect(text()).toContain('UPI');
  });

  it('offers Edit and Delete on every entry, Mark paid only on an announcement, and no Void or Cancel', () => {
    load(ledgerOf(pledge(), donation()));

    expect(rowButtons(0)).toEqual(['Mark paid', 'Edit', 'Delete']);
    expect(rowButtons(1)).toEqual(['Edit', 'Delete']);
    expect(text()).not.toContain('Void');
    expect(fixture.nativeElement.querySelector('tbody')!.textContent).not.toContain('Cancel');
  });

  it('keeps deleted entries out of the table until asked for', () => {
    load(ledgerOf(donation(), donation({ id: 'd3', receiptNumber: 2, isVoid: true, voidReason: 'Entered twice' })));

    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(1);
    expect(text()).toContain('Deleted (1)');

    component.show.set('deleted');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('tbody tr.deleted').length).toBe(1);
    expect(text()).toContain('Entered twice');
    expect(rowButtons(0)).toEqual([]);
  });

  it('narrows the table to what is still to be paid', () => {
    load(ledgerOf(donation(), pledge()));

    component.show.set('pending');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(1);
    expect(text()).toContain('Smt. Kavita Jain');
  });

  it('records a paid donation in paise, with its mode', () => {
    load();

    component.openRecord();
    component.donorName = ' Smt. Pushpa Jain ';
    component.amount = '2,500.50';
    component.donatedOn = '2026-09-25';
    component.mode = 'Cash';
    component.save();

    const call = http.expectOne(DONATIONS);

    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual({
      donorName: 'Smt. Pushpa Jain',
      donorMemberId: null,
      amount: 250050,
      donatedOn: '2026-09-25',
      mode: 'Cash',
      purpose: null,
      paid: true,
    });

    call.flush(donation({ receiptNumber: 7, amount: 250050 }));
    reload();

    expect(text()).toContain('Receipt 7');
    expect(component.popup()).toBeNull();
  });

  it('records an announcement with no mode, and says no receipt is issued yet', () => {
    load();

    component.openRecord();
    component.paidNow = false;
    fixture.detectChanges();

    expect(text()).toContain('Announced on');
    expect(fixture.nativeElement.querySelector('#donation-mode')).toBeNull();
    expect(text()).toContain('No receipt yet');

    component.donorName = 'Smt. Kavita Jain';
    component.amount = '2,100';
    component.save();

    const call = http.expectOne(DONATIONS);

    expect(call.request.body).toEqual(expect.objectContaining({ mode: null, paid: false, amount: 210000 }));

    call.flush(pledge());
    reload();

    expect(text()).toContain('announced by Smt. Kavita Jain, not yet paid');
  });

  it('will not record an amount that is not one', () => {
    load();

    component.openRecord();
    component.donorName = 'Smt. Pushpa Jain';

    for (const amount of ['', '0', 'five hundred', '-100']) {
      component.amount = amount;

      expect(component.canSave(), amount).toBe(false);
    }

    component.save();
    http.expectNone((r) => r.method === 'POST');
  });

  it('marks an announcement paid, which issues its receipt', () => {
    load(ledgerOf(pledge()));

    component.openPaid(pledge());
    component.mode = 'Upi';
    component.paidOn = '2026-09-28';
    component.markPaid();

    const call = http.expectOne('/v1/pathshala/donations/d2/paid');

    expect(call.request.body).toEqual({ mode: 'Upi', paidOn: '2026-09-28' });

    call.flush(pledge({ status: 'Paid', receiptNumber: 4, mode: 'Upi', paidOn: '2026-09-28' }));
    reload();

    expect(text()).toContain('Paid: receipt 4');
  });

  it('edits a paid entry with its mode and paid date, and an announcement without either', () => {
    load(ledgerOf(donation(), pledge()));

    component.openEdit(donation());
    expect(component.amount).toBe('5100');
    component.amount = '5,500';
    component.save();

    const paid = http.expectOne('/v1/pathshala/donations/d1');

    expect(paid.request.method).toBe('PATCH');
    expect(paid.request.body).toEqual({
      donorName: 'Shri Mahendra Jain',
      amount: 550000,
      donatedOn: '2026-09-20',
      mode: 'Upi',
      paidOn: '2026-09-20',
      purpose: 'Books',
    });
    paid.flush(donation({ amount: 550000 }));
    reload();

    component.openEdit(pledge());
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#donation-mode')).toBeNull();
    component.save();

    const announced = http.expectOne('/v1/pathshala/donations/d2');

    expect(announced.request.body).toEqual(expect.objectContaining({ mode: null, paidOn: null }));
    announced.flush(pledge());
    reload();
  });

  it('asks why before deleting an entry with a receipt, and not for an announcement', () => {
    load(ledgerOf(donation(), pledge()));

    component.openDelete(donation());
    fixture.detectChanges();

    expect(text()).toContain('not issued again');
    expect(component.canDelete(donation())).toBe(false);

    component.deleteDonation();
    http.expectNone('/v1/pathshala/donations/d1/void');

    component.deleteReason = 'Entered twice';
    component.deleteDonation();

    const call = http.expectOne('/v1/pathshala/donations/d1/void');

    expect(call.request.body).toEqual({ reason: 'Entered twice' });
    call.flush(donation({ isVoid: true, voidReason: 'Entered twice' }));
    reload();

    expect(text()).toContain('Receipt 1 is deleted');

    component.openDelete(pledge());
    expect(component.canDelete(pledge())).toBe(true);
    component.deleteDonation();

    const announced = http.expectOne('/v1/pathshala/donations/d2/void');

    expect(announced.request.body).toEqual({ reason: null });
    announced.flush(pledge({ isVoid: true }));
    reload();
  });
});
