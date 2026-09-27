import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_CONFIG } from '@samaajconnect/shared';
import { TrustCollection, TrustDonation, TrustExpense } from '../../core/admin.models';
import { TrustCollectionsComponent } from './trust-collections.component';
import { TrustDonationsComponent } from './trust-donations.component';
import { TrustExpensesComponent } from './trust-expenses.component';

const BASE = '/v1/trust/trusts/t1';
const NAMES: ReadonlyMap<string, string> = new Map([
  ['m1', 'Diya Jain'],
  ['a1', 'Rajesh Jain'],
]);

function donation(overrides: Partial<TrustDonation> = {}): TrustDonation {
  return {
    id: 'd1',
    trustId: 't1',
    donorMemberId: null,
    donorName: 'Shri Mahendra Jain',
    donorPhone: '9829012345',
    amount: 5_100_000,
    purpose: 'Temple fund',
    mode: 'Upi',
    status: 'Pledged',
    receiptNumber: null,
    recordedBy: 'a1',
    createdAt: '2026-09-20T10:00:00Z',
    collectedBy: null,
    collectedAt: null,
    ...overrides,
  };
}

function expense(overrides: Partial<TrustExpense> = {}): TrustExpense {
  return {
    id: 'x1',
    trustId: 't1',
    category: 'Maintenance',
    description: 'Roof repair',
    amount: 1_250_000,
    vendor: null,
    status: 'Recorded',
    recordedBy: 'a1',
    recordedAt: '2026-09-20T10:00:00Z',
    approvedBy: null,
    approvedAt: null,
    ...overrides,
  };
}

function collection(overrides: Partial<TrustCollection> = {}): TrustCollection {
  return {
    id: 'c1',
    trustId: 't1',
    sourceType: 'BoliResult',
    sourceId: 'b1',
    payerMemberId: 'm1',
    amount: 2_100_000,
    status: 'Due',
    mode: null,
    collectedBy: null,
    collectedAt: null,
    createdAt: '2026-09-20T10:00:00Z',
    ...overrides,
  };
}

function setUp<T>(component: Type<T>): {
  fixture: ComponentFixture<T>;
  http: HttpTestingController;
} {
  TestBed.configureTestingModule({
    imports: [component],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
    ],
  });

  const fixture = TestBed.createComponent(component);
  fixture.componentRef.setInput('trustId', 't1');
  fixture.componentRef.setInput('names', NAMES);

  return { fixture, http: TestBed.inject(HttpTestingController) };
}

const textOf = (fixture: ComponentFixture<unknown>) => fixture.nativeElement.textContent as string;

describe('TrustDonationsComponent', () => {
  let fixture: ComponentFixture<TrustDonationsComponent>;
  let http: HttpTestingController;

  beforeEach(() => ({ fixture, http } = setUp(TrustDonationsComponent)));
  afterEach(() => http.verify());

  function load(found: TrustDonation[]) {
    fixture.detectChanges();
    http.expectOne(`${BASE}/donations`).flush(found);
    fixture.detectChanges();
  }

  it('keeps what was promised apart from what arrived', () => {
    load([
      donation(),
      donation({ id: 'd2', amount: 100_000, status: 'Collected', receiptNumber: 4 }),
    ]);

    expect(textOf(fixture)).toContain('Pledged, not yet collected: ₹51,000');
    expect(textOf(fixture)).toContain('Collected: ₹1,000');
  });

  it('records a pledge in paise, with no receipt number yet', () => {
    load([]);
    const component = fixture.componentInstance;

    component.donorName = ' Smt. Pushpa Jain ';
    component.donorPhone = '';
    component.amount = '11,000';
    component.purpose = 'Books';
    component.mode = 'Cash';
    component.record();

    const call = http.expectOne(`${BASE}/donations`);

    expect(call.request.body).toEqual({
      donorMemberId: null,
      donorName: 'Smt. Pushpa Jain',
      donorPhone: null,
      amount: 1_100_000,
      purpose: 'Books',
      mode: 'Cash',
    });

    call.flush(donation());
    http.expectOne(`${BASE}/donations`).flush([donation()]);
  });

  it('will not record an amount or a phone the service would refuse', () => {
    load([]);
    const component = fixture.componentInstance;

    component.donorName = 'Smt. Pushpa Jain';
    component.purpose = 'Books';
    component.amount = '0';

    expect(component.canRecord()).toBe(false);

    component.amount = '500';
    component.donorPhone = 'call me';

    expect(component.canRecord()).toBe(false);

    component.record();
    http.expectNone((r) => r.method === 'POST');
  });

  it('collects a pledge and says the receipt number the service issued', () => {
    load([donation()]);

    fixture.componentInstance.collect(donation());

    const call = http.expectOne('/v1/trust/donations/d1/collect');

    expect(call.request.method).toBe('POST');

    call.flush(donation({ status: 'Collected', receiptNumber: 7 }));
    http.expectOne(`${BASE}/donations`).flush([donation({ status: 'Collected', receiptNumber: 7 })]);
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('Receipt 7 for Shri Mahendra Jain');
  });
});

describe('TrustExpensesComponent', () => {
  let fixture: ComponentFixture<TrustExpensesComponent>;
  let http: HttpTestingController;

  beforeEach(() => ({ fixture, http } = setUp(TrustExpensesComponent)));
  afterEach(() => http.verify());

  function load(found: TrustExpense[]) {
    fixture.detectChanges();
    http.expectOne(`${BASE}/expenses`).flush(found);
    fixture.detectChanges();
  }

  it('names who recorded and who approved each expense', () => {
    load([expense({ status: 'Approved', approvedBy: 'a1', approvedAt: '2026-09-21T10:00:00Z' })]);

    expect(textOf(fixture)).toContain('by Rajesh Jain');
    expect(textOf(fixture)).toContain('Approved');
  });

  it('records an expense, a blank vendor sent as null', () => {
    load([]);
    const component = fixture.componentInstance;

    component.category = 'Maintenance';
    component.description = 'Roof repair';
    component.amount = '12,500';
    component.vendor = '  ';
    component.record();

    const call = http.expectOne(`${BASE}/expenses`);

    expect(call.request.body).toEqual({
      category: 'Maintenance',
      description: 'Roof repair',
      amount: 1_250_000,
      vendor: null,
    });

    call.flush(expense());
    http.expectOne(`${BASE}/expenses`).flush([expense()]);
  });

  it('approves one', () => {
    load([expense()]);

    fixture.componentInstance.approve(expense());

    http.expectOne('/v1/trust/expenses/x1/approve').flush(expense({ status: 'Approved' }));
    http.expectOne(`${BASE}/expenses`).flush([expense({ status: 'Approved' })]);
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('is approved');
  });
});

describe('TrustCollectionsComponent', () => {
  let fixture: ComponentFixture<TrustCollectionsComponent>;
  let http: HttpTestingController;

  beforeEach(() => ({ fixture, http } = setUp(TrustCollectionsComponent)));
  afterEach(() => http.verify());

  function load(found: TrustCollection[]) {
    fixture.detectChanges();
    http.expectOne(`${BASE}/collections`).flush(found);
    fixture.detectChanges();
  }

  it('names who owes each amount, and offers no way to invent one', () => {
    load([collection()]);

    expect(textOf(fixture)).toContain('Diya Jain');
    expect(textOf(fixture)).toContain('A Boli they won');
    expect(textOf(fixture)).toContain('Still due: ₹21,000');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('says "A member" rather than printing an id it could not resolve', () => {
    load([collection({ payerMemberId: 'm9' })]);

    expect(textOf(fixture)).toContain('A member');
    expect(textOf(fixture)).not.toContain('m9');
  });

  it('records a payment with the mode picked, starting on Cash', () => {
    load([collection()]);
    const component = fixture.componentInstance;

    expect(component.modes['c1']).toBe('Cash');

    component.modes['c1'] = 'UPI';
    component.pay(collection());

    const call = http.expectOne('/v1/trust/collections/c1/payment');

    expect(call.request.body).toEqual({ mode: 'UPI' });

    call.flush(collection({ status: 'Collected', mode: 'UPI' }));
    http.expectOne(`${BASE}/collections`).flush([collection({ status: 'Collected', mode: 'UPI' })]);
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('recorded as paid');
    expect(textOf(fixture)).toContain('Paid · UPI');
  });
});
