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
    receiptNumber: 1,
    donorName: 'Shri Mahendra Jain',
    donorMemberId: null,
    amount: 510000,
    donatedOn: '2026-09-20',
    mode: 'Upi',
    purpose: 'Books',
    recordedBy: 'u1',
    recordedAt: '2026-09-20T10:00:00Z',
    isVoid: false,
    voidReason: null,
    ...overrides,
  };
}

describe('PathshalaDonationsComponent', () => {
  let fixture: ComponentFixture<PathshalaDonationsComponent>;
  let component: PathshalaDonationsComponent;
  let http: HttpTestingController;

  const text = () => fixture.nativeElement.textContent as string;

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

  function load(ledger: DonationLedger = { donations: [donation()], totalAmount: 510000 }) {
    fixture.componentRef.setInput('pathshalaId', 'p1');
    fixture.detectChanges();
    http.expectOne(DONATIONS).flush(ledger);
    fixture.detectChanges();
  }

  it('shows the ledger and the total the service worked out', () => {
    // The total comes from the service, which leaves voided entries out; a
    // second sum here would be a second copy of that rule.
    load({
      donations: [donation(), donation({ id: 'd2', receiptNumber: 2, amount: 100000, isVoid: true, voidReason: 'Entered twice' })],
      totalAmount: 510000,
    });

    expect(text()).toContain('₹5,100');
    expect(text()).toContain('Entered twice');
    expect(text()).toContain('UPI');
    expect(fixture.nativeElement.querySelectorAll('tr.void').length).toBe(1);
  });

  it('records a donation in paise', () => {
    load();

    component.donorName = ' Smt. Pushpa Jain ';
    component.amount = '2,500.50';
    component.donatedOn = '2026-09-25';
    component.mode = 'Cash';
    component.purpose = '';
    component.record();

    const call = http.expectOne(DONATIONS);

    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual({
      donorName: 'Smt. Pushpa Jain',
      donorMemberId: null,
      amount: 250050,
      donatedOn: '2026-09-25',
      mode: 'Cash',
      purpose: null,
    });

    call.flush(donation({ receiptNumber: 7, amount: 250050 }));
    http.expectOne(DONATIONS).flush({ donations: [], totalAmount: 0 });
    fixture.detectChanges();

    expect(text()).toContain('Receipt 7');
    expect(component.amount).toBe('');
  });

  it('will not record an amount that is not one', () => {
    load();

    component.donorName = 'Smt. Pushpa Jain';

    for (const amount of ['', '0', 'five hundred', '-100']) {
      component.amount = amount;

      expect(component.canRecord(), amount).toBe(false);
    }

    component.record();
    http.expectNone((r) => r.method === 'POST');
  });

  it('voids only with a reason, and keeps the entry', () => {
    load();

    component.startVoid(donation());
    fixture.detectChanges();

    expect(text()).toContain('stays on the ledger');

    component.voidDonation(donation());
    http.expectNone('/v1/pathshala/donations/d1/void');

    component.voidReason = 'Entered twice';
    component.voidDonation(donation());

    const call = http.expectOne('/v1/pathshala/donations/d1/void');

    expect(call.request.body).toEqual({ reason: 'Entered twice' });

    call.flush(donation({ isVoid: true, voidReason: 'Entered twice' }));
    http.expectOne(DONATIONS).flush({ donations: [donation({ isVoid: true })], totalAmount: 0 });
    fixture.detectChanges();

    expect(text()).toContain('Receipt 1 is void');
  });
});
