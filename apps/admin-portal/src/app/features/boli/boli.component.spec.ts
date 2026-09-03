import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { BoliListComponent } from './boli-list.component';
import { OccasionDetailComponent } from './occasion-detail.component';
import { Boli, Occasion, OccasionDetail, PendingResult } from '../../core/admin.models';

const OCCASION_ID = 'o1';

function occasion(overrides: Partial<Occasion> = {}): Occasion {
  return {
    id: OCCASION_ID,
    title: 'Paryushan 2026',
    description: null,
    occasionDate: '2026-09-10',
    status: 'Upcoming',
    typeCount: 1,
    boliCount: 2,
    ...overrides,
  };
}

function pending(overrides: Partial<PendingResult> = {}): PendingResult {
  return {
    boliId: 'b1',
    boliTitle: 'Mangal Deep',
    occasionId: OCCASION_ID,
    amount: 1_840_000,
    recordedBy: 'm1',
    recordedAt: '2026-08-15T17:12:00Z',
    ...overrides,
  };
}

function lot(overrides: Partial<Boli> = {}): Boli {
  return {
    id: 'b1',
    occasionId: OCCASION_ID,
    boliTypeId: 't1',
    boliTypeName: 'Mangal Deep',
    title: 'Mangal Deep — first day',
    startAt: '2026-09-10T09:00:00Z',
    endAt: '2026-09-10T12:00:00Z',
    startingAmount: 100_000,
    minIncrement: 50_000,
    autoExtendSeconds: 0,
    eligibilityRule: 'One per family.',
    status: 'Open',
    acceptsBids: true,
    highestAmount: null,
    minimumNextBid: 100_000,
    highestBidderIsMe: false,
    bidCount: 0,
    ...overrides,
  };
}

function detail(overrides: Partial<OccasionDetail> = {}): OccasionDetail {
  return {
    id: OCCASION_ID,
    title: 'Paryushan 2026',
    description: null,
    occasionDate: '2026-09-10',
    status: 'Upcoming',
    types: [{ id: 't1', name: 'Mangal Deep', description: null }],
    boli: [],
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
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(BoliListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(occasions: Occasion[] = [], queue: PendingResult[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/boli/occasions').flush(occasions);
    http.expectOne('/v1/boli/results/pending').flush(queue);
    fixture.detectChanges();
  }

  it('reads a 404 as the module being off, not as an error', () => {
    // The gateway answers 404 for a Samaaj that has switched `boli` off, so a
    // Samaaj without the module is indistinguishable from a platform with no
    // such feature. Reporting it as an error sends an administrator hunting a
    // bug that is a setting.
    fixture.detectChanges();
    http.expectOne('/v1/boli/occasions').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('does not run the Boli module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('distinguishes an empty queue from a queue you may not see', () => {
    // Boli.PublishResults is a separate permission from Boli.Manage. Telling a
    // manager who cannot publish that nothing is waiting would be telling them
    // something false about their own Samaaj.
    fixture.detectChanges();
    http.expectOne('/v1/boli/occasions').flush([occasion()]);
    http.expectOne('/v1/boli/results/pending').flush({}, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(text()).toContain('belongs to somebody else');
    expect(text()).not.toContain('Nothing is waiting');
  });

  it('says nothing is waiting when the queue is genuinely empty', () => {
    load([occasion()]);

    expect(text()).toContain('Nothing is waiting');
  });

  it('shows the winning bid in rupees, not paise', () => {
    load([occasion()], [pending()]);

    // 1,840,000 paise is Rs 18,400 - the wireframe's number. A screen printing
    // the paise would be out by a factor of a hundred in an amount the Samaaj
    // collects against.
    expect(text()).toContain('18,400');
  });

  it('will not publish on one click', () => {
    // Announcing names the winner to the whole Samaaj and cannot be undone
    // here. The wireframe made this a screen of its own for that reason; the
    // deliberate second click is what that screen was buying.
    load([occasion()], [pending()]);

    expect(text()).not.toContain('irreversible');

    component.confirming.set('b1');
    fixture.detectChanges();

    expect(text()).toContain('irreversible');
    http.expectNone('/v1/boli/boli/b1/result/publish');
  });

  it('keeps the button that opened the confirmation, and announces the panel', () => {
    // The confirmation used to replace its own trigger. Removing the focused
    // element drops keyboard focus to the body, so a keyboard user loses their
    // place the moment they ask to confirm something (WCAG 2.4.3) - and
    // disabling it instead does the same, because a disabled control is blurred.
    // The panel is a live region so a screen reader hears the warning at all.
    load([occasion()], [pending()]);

    const trigger = () =>
      [...fixture.nativeElement.querySelectorAll('tbody button')].find(
        (b) => (b as HTMLElement).textContent?.trim() === 'Review and publish',
      ) as HTMLButtonElement | undefined;

    expect(trigger()).toBeTruthy();
    expect(trigger()!.getAttribute('aria-expanded')).toBe('false');

    component.confirming.set('b1');
    fixture.detectChanges();

    expect(trigger()).toBeTruthy();
    expect(trigger()!.disabled).toBe(false);
    expect(trigger()!.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelector('.notice[role="status"]')).toBeTruthy();
  });

  it('names every table it draws', () => {
    // Two tables on this screen, and a screen reader listing them gets the
    // caption rather than "table, table".
    load([occasion()], [pending()]);

    const captions = [...fixture.nativeElement.querySelectorAll('table caption')].map((c) =>
      (c as HTMLElement).textContent?.trim(),
    );

    expect(captions).toEqual([
      'Results recorded and waiting to be announced',
      'Boli occasions',
    ]);
  });

  it('publishes once confirmed', () => {
    load([occasion()], [pending()]);

    component.publish(pending());

    const call = http.expectOne('/v1/boli/boli/b1/result/publish');

    expect(call.request.method).toBe('POST');

    call.flush({});
    reload();

    expect(text()).toContain('announced at');
  });

  it('never names a winner in the queue', () => {
    // boli-service names the winner in exactly one shape and only once it is
    // published. The wireframe drew "Member ID 1042" on this screen; nothing is
    // lost by its absence, because the winner is read from the highest bid and
    // is not something the publisher chooses.
    load([occasion()], [pending()]);

    expect(text()).not.toContain('m1');
    expect(JSON.stringify(component.pending())).not.toContain('winningMemberId');
  });

  it('will not announce an occasion without a title and a date', () => {
    load();

    component.title = 'Paryushan 2026';
    component.occasionDate = '';
    component.create();

    http.expectNone('/v1/boli/occasions');
  });

  it('announces an occasion, sending no description rather than an empty one', () => {
    load();

    component.title = ' Paryushan 2026 ';
    component.description = '   ';
    component.occasionDate = '2026-09-10';
    component.create();

    const call = http.expectOne('/v1/boli/occasions');

    expect(call.request.body).toEqual({
      title: 'Paryushan 2026',
      description: null,
      occasionDate: '2026-09-10',
    });

    call.flush(occasion());
    reload();
  });

  function reload() {
    http.expectOne('/v1/boli/occasions').flush([occasion()]);
    http.expectOne('/v1/boli/results/pending').flush([]);
    fixture.detectChanges();
  }
});

describe('OccasionDetailComponent', () => {
  let fixture: ComponentFixture<OccasionDetailComponent>;
  let component: OccasionDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OccasionDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => OCCASION_ID } } },
        },
      ],
    });

    fixture = TestBed.createComponent(OccasionDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(event: OccasionDetail = detail()) {
    fixture.detectChanges();
    http.expectOne(`/v1/boli/occasions/${OCCASION_ID}`).flush(event);
    fixture.detectChanges();
  }

  it('offers the one move the occasion can make', () => {
    // Forward only. Offering three buttons and letting the server refuse two
    // would be offering choices that were never there.
    load(detail({ status: 'Upcoming' }));
    expect(component.nextStatus()).toBe('Active');

    component.move('Active');
    http.expectOne(`/v1/boli/occasions/${OCCASION_ID}/status`).flush({});
    http.expectOne(`/v1/boli/occasions/${OCCASION_ID}`).flush(detail({ status: 'Active' }));
    fixture.detectChanges();

    expect(component.nextStatus()).toBe('Closed');
  });

  it('offers no move once the occasion is closed', () => {
    load(detail({ status: 'Closed' }));

    expect(component.nextStatus()).toBeNull();
    expect(text()).toContain('Nothing moves it further');
  });

  it('refuses to open a Boli until there is a type to open it against', () => {
    load(detail({ types: [] }));

    expect(text()).toContain('Define a Boli type first');
    expect(text()).not.toContain('Open a Boli');
  });

  it('sends paise, from rupees typed', () => {
    // The one conversion on this screen, and the one that would be wrong by a
    // factor of a hundred if it were skipped.
    load();

    component.boliTypeId = 't1';
    component.boliTitle = 'Mangal Deep — first day';
    component.startAt = '2026-09-10T09:00';
    component.endAt = '2026-09-10T12:00';
    component.startingAmount = '1,000';
    component.minIncrement = '500.50';
    component.eligibilityRule = '';
    component.open();

    const call = http.expectOne(`/v1/boli/occasions/${OCCASION_ID}/boli`);
    const body = call.request.body as { startingAmount: number; minIncrement: number; eligibilityRule: null };

    expect(body.startingAmount).toBe(100_000);
    expect(body.minIncrement).toBe(50_050);
    expect(body.eligibilityRule).toBeNull();

    call.flush({});
    http.expectOne(`/v1/boli/occasions/${OCCASION_ID}`).flush(detail());
    fixture.detectChanges();
  });

  it('opens a Boli with anti-sniping off unless somebody asks for it', () => {
    // Zero is off, and every Boli opened before the setting existed reads back
    // as zero. Defaulting the form to anything else would switch it on under a
    // Samaaj that never chose it.
    load();

    expect(component.autoExtendSeconds).toBe(0);

    component.boliTypeId = 't1';
    component.boliTitle = 'Mangal Deep';
    component.startAt = '2026-09-10T09:00';
    component.endAt = '2026-09-10T12:00';
    component.startingAmount = '1000';
    component.minIncrement = '500';
    component.open();

    const call = http.expectOne(`/v1/boli/occasions/${OCCASION_ID}/boli`);

    expect((call.request.body as { autoExtendSeconds: number }).autoExtendSeconds).toBe(0);

    call.flush({});
    http.expectOne(`/v1/boli/occasions/${OCCASION_ID}`).flush(detail());
    fixture.detectChanges();
  });

  it('sends the window a Samaaj chose', () => {
    load();

    component.boliTypeId = 't1';
    component.boliTitle = 'Mangal Deep';
    component.startAt = '2026-09-10T09:00';
    component.endAt = '2026-09-10T12:00';
    component.startingAmount = '1000';
    component.minIncrement = '500';
    component.autoExtendSeconds = 120;
    component.open();

    const call = http.expectOne(`/v1/boli/occasions/${OCCASION_ID}/boli`);

    expect((call.request.body as { autoExtendSeconds: number }).autoExtendSeconds).toBe(120);

    call.flush({});
    http.expectOne(`/v1/boli/occasions/${OCCASION_ID}`).flush(detail());
    fixture.detectChanges();
  });

  it('says on a Boli that its closing time can move', () => {
    // A close that shifts on its own is surprising unless the screen warns that
    // it can - the manager is watching that column to know when to expect a
    // result.
    load(detail({ boli: [lot({ autoExtendSeconds: 120, acceptsBids: true })] }));

    expect(text()).toContain('A bid in the last 120s pushes this out');
  });

  it('says nothing about extending on a Boli that does not', () => {
    load(detail({ boli: [lot({ autoExtendSeconds: 0 })] }));

    expect(text()).not.toContain('pushes this out');
  });

  it('will not open a Boli on an amount it could not parse', () => {
    // parseRupees returns null for "12abc" rather than 12, which is what stops
    // a floor nobody typed from being sent.
    load();

    component.boliTypeId = 't1';
    component.boliTitle = 'Mangal Deep';
    component.startAt = '2026-09-10T09:00';
    component.endAt = '2026-09-10T12:00';
    component.startingAmount = '1000 rupees';
    component.minIncrement = '500';
    component.open();

    http.expectNone(`/v1/boli/occasions/${OCCASION_ID}/boli`);
  });

  it('still offers Close on an open Boli whose window has passed', () => {
    // Status and clock are two facts. A Boli left Open past its closing time
    // stops taking bids but is still Open, and only a closed Boli can have its
    // result recorded - so hiding the button here would strand exactly the Boli
    // that most needs finishing.
    load(detail({ boli: [lot({ status: 'Open', acceptsBids: false, bidCount: 4 })] }));

    expect(text()).toContain('Window has passed');
    expect(text()).toContain('Close');
  });

  it('offers Record only on a closed Boli that somebody bid on', () => {
    load(detail({ boli: [lot({ status: 'Closed', acceptsBids: false, bidCount: 0 })] }));

    expect(text()).toContain('Nobody bid');
    expect(text()).not.toContain('Record result');
  });

  it('records a result without naming a winner', () => {
    load(detail({ boli: [lot({ status: 'Closed', acceptsBids: false, bidCount: 3, highestAmount: 500_000 })] }));

    component.record(lot({ status: 'Closed' }));

    const call = http.expectOne('/v1/boli/boli/b1/result');

    // No winner in the body, and there must never be one: the service reads the
    // highest bid, so a recorded result cannot name somebody the append-only
    // bid history contradicts.
    expect(call.request.body).toEqual({});

    call.flush({});
    http.expectOne(`/v1/boli/occasions/${OCCASION_ID}`).flush(detail());
    fixture.detectChanges();

    expect(text()).toContain('waiting to be announced');
  });

  it('shows a Boli with no bids as its floor rather than as zero', () => {
    // Zero is a claim, and the wrong one: nobody has offered anything.
    load(detail({ boli: [lot({ highestAmount: null, startingAmount: 100_000 })] }));

    expect(text()).toContain('No bids');
    expect(text()).toContain('1,000');
  });

  it('explains a 404 as no such occasion rather than as an error', () => {
    fixture.detectChanges();
    http
      .expectOne(`/v1/boli/occasions/${OCCASION_ID}`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such occasion');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
