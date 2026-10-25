import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { BoliDetailComponent } from './boli-detail.component';
import { BoliListComponent } from './boli-list.component';
import { Bid, Boli, BoliResult, Occasion, OccasionDetail } from './boli.models';
import { OccasionComponent } from './occasion.component';

const ACTIVE = '/v1/boli/boli/active';
const CITY = { id: 'c1', name: 'Udaipur' };
const LOCALITY = { id: 'l1', name: 'Hiran Magri', cityId: 'c1' };

/** Every Boli screen loads the City and Locality lists once, to say where a Boli is held. */
function settlePlaces(http: HttpTestingController): void {
  for (const request of http.match((r) => r.url === '/v1/master-data/cities')) {
    request.flush([CITY]);
  }

  for (const request of http.match((r) => r.url === '/v1/master-data/localities')) {
    request.flush([LOCALITY]);
  }
}
const OCCASIONS = '/v1/boli/occasions';
const RESULTS = '/v1/boli/results';

function boli(overrides: Partial<Boli> = {}): Boli {
  return {
    id: 'b1',
    occasionId: 'o1',
    boliTypeId: 't1',
    boliTypeName: 'Mangal Deep',
    title: 'Mangal Deep',
    startAt: '2026-09-01T04:00:00Z',
    endAt: '2099-09-01T06:00:00Z',
    startingAmount: 1_500_000,
    minIncrement: 50_000,
    eligibilityRule: 'One per family.',
    status: 'Open',
    autoExtendSeconds: 0,
    acceptsBids: true,
    highestAmount: 1_510_000,
    minimumNextBid: 1_560_000,
    highestBidderIsMe: false,
    bidCount: 3,
    ...overrides,
  };
}

function occasion(overrides: Partial<Occasion> = {}): Occasion {
  return {
    id: 'o1',
    title: 'Paryushan 2026',
    description: 'The annual Boli.',
    occasionDate: '2026-09-10',
    status: 'Active',
    typeCount: 2,
    boliCount: 3,
    ...overrides,
  };
}

function result(overrides: Partial<BoliResult> = {}): BoliResult {
  return {
    boliId: 'b1',
    boliTitle: 'Mangal Deep',
    amount: 1_610_000,
    winningMemberId: 'm1',
    winnerIsMe: false,
    isPublished: true,
    recordedAt: '2026-09-01T06:10:00Z',
    publishedAt: '2026-09-01T06:15:00Z',
    ...overrides,
  };
}

function bid(overrides: Partial<Bid> = {}): Bid {
  return {
    id: 'bid1',
    amount: 1_510_000,
    placedAt: '2026-09-01T05:12:00Z',
    isMine: false,
    ...overrides,
  };
}

describe('BoliListComponent', () => {
  let fixture: ComponentFixture<BoliListComponent>;
  let component: BoliListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BoliListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(BoliListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(options: {
    active?: Boli[];
    occasions?: Occasion[];
    results?: BoliResult[];
  } = {}): void {
    fixture.detectChanges();
    settlePlaces(http);

    http.expectOne((r) => r.url === ACTIVE).flush(options.active ?? [boli()]);
    http.expectOne((r) => r.url === OCCASIONS).flush(options.occasions ?? [occasion()]);
    http.expectOne(RESULTS).flush(options.results ?? []);

    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  it('leads with what is taking bids now', () => {
    load();

    expect(text()).toContain('Bidding now');
    expect(text()).toContain('Mangal Deep');
    expect(text()).toContain('15,100');
  });

  it('says nobody has bid rather than showing a zero', () => {
    // Null is not zero: no bids is different from somebody having bid nothing.
    load({ active: [boli({ highestAmount: null, bidCount: 0 })] });

    expect(text()).toContain('No bids yet');
    expect(text()).not.toContain('₹0');
  });

  it('marks the Boli the reader is currently winning', () => {
    load({ active: [boli({ highestBidderIsMe: true })] });

    expect(text()).toContain('You are leading');
    expect(fixture.nativeElement.querySelector('.card.mine')).not.toBeNull();
  });

  it('says so when nothing is taking bids', () => {
    load({ active: [], occasions: [] });

    expect(text()).toContain('Nothing is taking bids');
  });

  it('opens on the occasions when nothing is taking bids', () => {
    load({ active: [] });

    expect(component.tab()).toBe('occasions');
    expect(text()).toContain('Paryushan 2026');
  });

  it('shows announced results with the amount', () => {
    load({ results: [result({ winnerIsMe: true })] });
    component.tab.set('results');
    fixture.detectChanges();

    expect(text()).toContain('16,100');
  });

  it('tells a member which announced Boli they won', () => {
    load({ results: [result({ winnerIsMe: true })] });
    component.tab.set('results');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('td .pill.ok')?.textContent.trim()).toBe('You');
  });

  it('names who won, because a result is announced to the whole Samaaj', () => {
    load({ results: [result()] });

    // One lookup per winner on screen, never one per member of the Samaaj.
    http.expectOne('/v1/members/m1').flush({ id: 'm1', fullName: 'Ravi Jain', photoUrl: null });
    component.tab.set('results');
    fixture.detectChanges();

    expect(text()).toContain('Ravi Jain');
  });

  it('says where each Boli is held', () => {
    load({ active: [boli({ venue: 'Shri Mahavir Mandir', localityId: 'l1', cityId: 'c1' })] });

    expect(text()).toContain('Shri Mahavir Mandir · Hiran Magri, Udaipur');
  });

  it('asks the service for the chosen place', () => {
    load();

    component.chooseCity('c1');
    component.load();

    const active = http.expectOne((r) => r.url === ACTIVE);
    expect(active.request.params.get('cityId')).toBe('c1');
    active.flush([]);

    const occasions = http.expectOne((r) => r.url === OCCASIONS);
    expect(occasions.request.params.get('cityId')).toBe('c1');
    occasions.flush([]);
    http.expectOne(RESULTS).flush([]);
  });

  it('narrows announced results to the chosen place', () => {
    const both = [
      result({ cityId: 'c1', winnerIsMe: true }),
      result({ boliId: 'b2', boliTitle: 'Aarti', cityId: 'c2', winnerIsMe: true }),
    ];

    load({ results: both });

    component.chooseCity('c1');
    component.load();
    http.expectOne((r) => r.url === ACTIVE).flush([boli()]);
    http.expectOne((r) => r.url === OCCASIONS).flush([occasion()]);
    http.expectOne(RESULTS).flush(both);

    expect(component.shownResults().map((r) => r.boliId)).toEqual(['b1']);
  });

  it('still shows the rest when the Samaaj has announced nothing', () => {
    fixture.detectChanges();
    settlePlaces(http);

    http.expectOne((r) => r.url === ACTIVE).flush([boli()]);
    http.expectOne((r) => r.url === OCCASIONS).flush([occasion()]);
    http.expectOne(RESULTS).flush(null, { status: 404, statusText: 'Not Found' });
    component.tab.set('results');
    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(text()).toContain('Nothing has been announced here yet');
  });
});

describe('BoliDetailComponent', () => {
  let fixture: ComponentFixture<BoliDetailComponent>;
  let component: BoliDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BoliDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', 'b1']]) } },
        },
      ],
    });

    fixture = TestBed.createComponent(BoliDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(options: { lot?: Boli; bids?: Bid[]; result?: BoliResult | null } = {}): void {
    fixture.detectChanges();
    settlePlaces(http);

    const lot = options.lot ?? boli();

    http.expectOne('/v1/boli/boli/b1').flush(lot);
    http.expectOne('/v1/boli/boli/b1/bids').flush(options.bids ?? [bid()]);

    if (lot.status === 'Closed' || lot.status === 'ResultPublished') {
      const request = http.expectOne('/v1/boli/boli/b1/result');

      if (options.result) {
        request.flush(options.result);
      } else {
        request.flush(null, { status: 404, statusText: 'Not Found' });
      }
    }

    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  it('shows the minimum the server computed, not one of its own', () => {
    load();
    component.openBid(boli());
    fixture.detectChanges();

    // 15,100 + 500. The screen must print the server's `minimumNextBid`,
    // because the increment rule belongs to the Boli and a second copy here
    // would be one that drifts.
    expect(text()).toContain('15,600');
  });

  it('does not talk about a current highest when there is not one', () => {
    // Shipped saying "at least ₹25,000 — ₹1,000 above the current highest"
    // above an empty bid history. The increment does not apply until somebody
    // has bid; the first bid only has to meet the floor.
    const fresh = boli({ highestAmount: null, bidCount: 0, minimumNextBid: 2_500_000 });

    load({ lot: fresh, bids: [] });

    expect(component.guidance(fresh)).toContain('The first bid');
    expect(component.guidance(fresh)).not.toContain('current highest');
  });

  it('explains the increment once there is a highest to beat', () => {
    load();

    expect(component.guidance(boli())).toContain('above the current highest');
  });

  it('does not name who is leading while bidding is open', () => {
    load();

    expect(text()).toContain('names are not shown while bidding is open');
  });

  it('says when the reader is the one leading', () => {
    load({ lot: boli({ highestBidderIsMe: true }) });

    expect(text()).toContain('Yours');
  });

  // The anti-sniping window is only half a rule if bidders do not know about
  // it: a late bid is a bad idea precisely because everybody knows it buys the
  // room another window. It is also the screen's own honesty — with the window
  // on, `endAt` is a time the server moves, and printing it alone would be the
  // portal stating something that stops being true.
  it('says nothing about extending when the Boli has no window', () => {
    load();

    expect(component.extending(boli())).toBeNull();
    expect(text()).not.toContain('nothing to be gained by waiting');
  });

  it('tells bidders that a late bid moves the close', () => {
    load({ lot: boli({ autoExtendSeconds: 120 }) });

    expect(text()).toContain('A bid in the last 2 minutes');
    expect(text()).toContain('nothing to be gained by waiting');
  });

  // Rounding 90 seconds up to "2 minutes" would print a longer window than the
  // one the server keeps, on the line a bidder is being asked to rely on.
  it('never rounds the window it quotes', () => {
    load();

    expect(component.extending(boli({ autoExtendSeconds: 45 }))).toContain('45 seconds');
    expect(component.extending(boli({ autoExtendSeconds: 90 }))).toContain('90 seconds');
    expect(component.extending(boli({ autoExtendSeconds: 60 }))).toContain('1 minute ');
    expect(component.extending(boli({ autoExtendSeconds: 300 }))).toContain('5 minutes');
  });

  it('shows the Samaaj eligibility rule as its own words', () => {
    load();

    expect(text()).toContain('One per family.');
  });

  it('opens the bid form over the page, starting on the minimum', () => {
    load();

    expect(fixture.nativeElement.querySelector('#amount')).toBeNull();

    (fixture.nativeElement.querySelector('.actions .btn') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"] #amount')).not.toBeNull();
    expect(component.amount).toBe('15600');

    component.closeBid();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();
  });

  it('shows everything entered when the Boli was opened', () => {
    load({ lot: boli({ venue: 'Shri Mahavir Mandir', localityId: 'l1', occasionTitle: 'Paryushan 2026' }) });

    expect(component.tab()).toBe('details');
    expect(text()).toContain('Shri Mahavir Mandir · Hiran Magri, Udaipur');
    expect(text()).toContain('Paryushan 2026');
    expect(text()).toContain('15,000');
    expect(text()).toContain('One per family.');
  });

  it('fills the form with the minimum on request', () => {
    load();

    component.useMinimum(boli());

    expect(component.amount).toBe('15600');
  });

  it('refuses a bid that is not an amount before sending it', () => {
    load();

    component.amount = 'twenty thousand';
    component.placeBid(boli());

    expect(component.bidError()).toContain('Enter an amount');
  });

  it('refuses an obviously low bid without a round trip', () => {
    load();

    component.amount = '15200';
    component.placeBid(boli());

    expect(component.bidError()).toContain('at least');
  });

  it('sends paise, not rupees', () => {
    load();

    component.amount = '15600';
    component.placeBid(boli());

    const request = http.expectOne('/v1/boli/boli/b1/bids');

    expect(request.request.body).toEqual({ amount: 1_560_000 });

    request.flush({
      boliId: 'b1',
      bidId: 'new',
      accepted: true,
      reason: null,
      highestAmount: 1_560_000,
      minimumNextBid: 1_610_000,
    });

    // The screen re-reads rather than patching: the highest has moved.
    http.expectOne('/v1/boli/boli/b1').flush(boli({ highestAmount: 1_560_000 }));
    http.expectOne('/v1/boli/boli/b1/bids').flush([bid()]);
    fixture.detectChanges();
  });

  it('treats being outbid as a notice, not an error, and refills the form', () => {
    load();

    component.amount = '15600';
    component.placeBid(boli());

    http.expectOne('/v1/boli/boli/b1/bids').flush({
      boliId: 'b1',
      bidId: null,
      accepted: false,
      reason: 'Somebody has bid at least this much already.',
      highestAmount: 1_600_000,
      minimumNextBid: 1_650_000,
    });

    http.expectOne('/v1/boli/boli/b1').flush(boli({ highestAmount: 1_600_000 }));
    http.expectOne('/v1/boli/boli/b1/bids').flush([bid()]);
    fixture.detectChanges();

    // Not an error: somebody outbid while the form was open has done nothing
    // wrong, and the amount they now need is already in the field.
    expect(component.bidError()).toBeNull();
    expect(component.outbid()).toContain('16,500');
    expect(component.amount).toBe('16500');
  });

  it('does not ask for a result while bidding is still open', () => {
    // The endpoint answers 404 by design until one is recorded. http.verify()
    // in afterEach is what proves the call was not made.
    load({ lot: boli({ status: 'Open', acceptsBids: true }) });
    component.tab.set('result');
    fixture.detectChanges();

    expect(component.result()).toBeNull();
    expect(text()).toContain('No result yet');
  });

  it('says a result is recorded without naming anybody', () => {
    load({
      lot: boli({ status: 'Closed', acceptsBids: false }),
      result: result({ isPublished: false, winningMemberId: null, publishedAt: null }),
    });

    // Not announced, so it does not open on the result.
    expect(component.tab()).toBe('details');
    component.tab.set('result');
    fixture.detectChanges();

    expect(text()).toContain('will be announced shortly');
    expect(text()).not.toContain('Won by you');
  });

  it('shows the announced result once it is published', () => {
    load({
      lot: boli({ status: 'ResultPublished', acceptsBids: false }),
      result: result({ winnerIsMe: true }),
    });

    expect(component.tab()).toBe('result');
    expect(text()).toContain('Won by you');
    expect(text()).toContain('16,100');
  });

  it('names the winner to everybody once it is announced', () => {
    load({
      lot: boli({ status: 'ResultPublished', acceptsBids: false }),
      result: result(),
    });

    http.expectOne('/v1/members/m1').flush({ id: 'm1', fullName: 'Ravi Jain', photoUrl: null });
    fixture.detectChanges();

    expect(text()).toContain('Won by Ravi Jain');
  });

  it('offers no bid form once the window has passed', () => {
    load({ lot: boli({ status: 'Open', acceptsBids: false }) });

    expect(text()).toContain('Bidding has closed');
    expect(fixture.nativeElement.querySelector('#amount')).toBeNull();
  });

  it('tells a Boli that has not opened apart from one that has closed', () => {
    // Both are status Open when a window is involved; they need opposite
    // sentences.
    const notYet = boli({
      status: 'Scheduled',
      acceptsBids: false,
      startAt: '2099-01-01T00:00:00Z',
    });

    load({ lot: notYet });

    expect(component.whyClosed(notYet)).toContain('has not opened yet');
  });

  it('marks the reader own bids in the history and names nobody else', () => {
    load({
      bids: [bid({ id: 'x', isMine: true }), bid({ id: 'y', amount: 1_500_000 })],
    });
    component.tab.set('bids');
    fixture.detectChanges();

    expect(text()).toContain('Yours');
    expect(fixture.nativeElement.querySelectorAll('tr.mine')).toHaveLength(1);
  });
});

describe('OccasionComponent', () => {
  let fixture: ComponentFixture<OccasionComponent>;
  let component: OccasionComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OccasionComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', 'o1']]) } },
        },
      ],
    });

    fixture = TestBed.createComponent(OccasionComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(boliUnder: Boli[]): void {
    fixture.detectChanges();

    const detail: OccasionDetail = {
      id: 'o1',
      title: 'Paryushan 2026',
      description: 'The annual Boli.',
      occasionDate: '2026-09-10',
      status: 'Active',
      types: [{ id: 't1', name: 'Mangal Deep', description: 'Lighting the lamp.' }],
      boli: boliUnder,
    };

    settlePlaces(http);
    http.expectOne('/v1/boli/occasions/o1').flush(detail);
    fixture.detectChanges();
  }

  function text(): string {
    return fixture.nativeElement.textContent as string;
  }

  it('lists every Boli under the occasion, open or not', () => {
    load([boli(), boli({ id: 'b2', title: 'Aarti', acceptsBids: false, status: 'Closed' })]);

    expect(text()).toContain('Mangal Deep');
    expect(text()).toContain('Aarti');
  });

  it('says the reader is leading only while bidding is open', () => {
    load([boli({ highestBidderIsMe: true, acceptsBids: true })]);

    expect(text()).toContain('You are leading');
  });

  it('does not say a finished Boli is one the reader is leading', () => {
    // Wrong tense beside "Result announced" - and on a Boli closed without a
    // published result it would announce the winner before the Samaaj did.
    const finished = boli({
      highestBidderIsMe: true,
      acceptsBids: false,
      status: 'ResultPublished',
    });

    load([finished]);

    expect(component.leading(finished)).toBe(false);
    expect(text()).not.toContain('You are leading');
  });

  it('does not pre-announce a winner on a Boli closed without a result', () => {
    const closed = boli({ highestBidderIsMe: true, acceptsBids: false, status: 'Closed' });

    load([closed]);

    expect(text()).not.toContain('You are leading');
    expect(text()).not.toContain('Won by you');
  });
});
