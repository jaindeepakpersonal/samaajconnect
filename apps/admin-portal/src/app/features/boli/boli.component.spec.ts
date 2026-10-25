import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { LocalityOptions } from '../../core/locality-options';
import { BoliListComponent } from './boli-list.component';
import { OccasionDialogComponent } from './occasion-dialog.component';
import { OccasionDetailComponent } from './occasion-detail.component';
import { Boli, Occasion, OccasionDetail, PendingResult } from '../../core/admin.models';

const OCCASION_ID = 'o1';

/** The occasion popup and the occasion page each load the City list once. */
function settleCities(http: HttpTestingController): void {
  for (const request of http.match((r) => r.url === '/v1/master-data/cities')) {
    request.flush([{ id: 'c1', name: 'Udaipur' }]);
  }
}

/** LocalityOptions without its two requests - every screen here only reads it. */
const LOCALITIES = {
  provide: LocalityOptions,
  useValue: {
    load: () => undefined,
    options: () => [{ id: 'loc1', label: 'Hiran Magri, Udaipur' }],
    label: (id: string | null) => (id === 'loc1' ? 'Hiran Magri, Udaipur' : 'Whole Samaaj'),
    place: (id: string | null) => (id === 'loc1' ? 'Hiran Magri, Udaipur' : null),
  },
};

function occasion(overrides: Partial<Occasion> = {}): Occasion {
  return {
    id: OCCASION_ID,
    title: 'Paryushan 2026',
    description: null,
    occasionDate: '2026-09-10',
    status: 'Upcoming',
    typeCount: 1,
    boliCount: 2,
    localityId: null,
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
        LOCALITIES,
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

  /** The publication queue is the second tab. */
  function showQueue() {
    component.tab.set('pending');
    fixture.detectChanges();
  }

  it('opens on the occasions, and counts what is waiting on the other tab', () => {
    load([occasion()], [pending()]);

    expect(component.tab()).toBe('occasions');
    expect(text()).toContain('Paryushan 2026');
    expect(text()).toContain('Awaiting publication');
    expect(text()).toContain('(1)');
    expect(text()).not.toContain('Review and publish');
  });

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
    showQueue();

    expect(text()).toContain('belongs to somebody else');
    expect(text()).not.toContain('Nothing is waiting');
  });

  it('says nothing is waiting when the queue is genuinely empty', () => {
    load([occasion()]);
    showQueue();

    expect(text()).toContain('Nothing is waiting');
  });

  it('shows the winning bid in rupees, not paise', () => {
    load([occasion()], [pending()]);
    showQueue();

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
    showQueue();

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
    showQueue();

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
    // One table on each tab, and a screen reader listing them gets the
    // caption rather than "table".
    load([occasion()], [pending()]);

    const captions = () =>
      [...fixture.nativeElement.querySelectorAll('table caption')].map((c) => (c as HTMLElement).textContent?.trim());

    expect(captions()).toEqual(['Boli occasions']);

    showQueue();

    expect(captions()).toEqual(['Results recorded and waiting to be announced']);
  });

  it('says where each occasion is held', () => {
    load([occasion({ localityId: 'loc1', venue: 'Shri Mahavir Mandir' })]);

    expect(text()).toContain('Hiran Magri, Udaipur');
    expect(text()).toContain('Shri Mahavir Mandir');
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

  const click = { currentTarget: null } as unknown as MouseEvent;

  function dialog(): OccasionDialogComponent {
    return fixture.debugElement.query((el) => el.componentInstance instanceof OccasionDialogComponent)
      .componentInstance as OccasionDialogComponent;
  }

  it('will not announce an occasion without a title and a date', () => {
    load();

    component.openCreate(click);
    fixture.detectChanges();
    settleCities(http);

    const form = dialog();
    form.title = 'Paryushan 2026';
    form.occasionDate = '';
    form.save();

    http.expectNone('/v1/boli/occasions');
  });

  it('announces an occasion in a popup, sending no description rather than an empty one', () => {
    load();

    component.openCreate(click);
    fixture.detectChanges();
    settleCities(http);

    const form = dialog();
    form.title = ' Paryushan 2026 ';
    form.description = '   ';
    form.occasionDate = '2026-09-10';
    form.localityId = 'loc1';
    form.venue = ' Shri Mahavir Mandir ';
    form.cityId = 'c1';
    form.save();

    const call = http.expectOne('/v1/boli/occasions');

    expect(call.request.body).toEqual({
      localityId: 'loc1',
      title: 'Paryushan 2026',
      description: null,
      occasionDate: '2026-09-10',
      venue: 'Shri Mahavir Mandir',
      cityId: 'c1',
    });

    call.flush(occasion());
    reload();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();
    expect(text()).toContain('Paryushan 2026 announced.');
  });

  it('corrects an occasion in the same popup, and offers no Edit once it is closed', () => {
    load([occasion({ localityId: 'loc1' }), occasion({ id: 'o2', title: 'Old', status: 'Closed' })]);

    const edits = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .filter((b) => b.textContent?.trim() === 'Edit');

    expect(edits).toHaveLength(1);
    expect(text()).toContain('Hiran Magri, Udaipur');

    component.openEdit(occasion({ localityId: 'loc1' }), click);
    fixture.detectChanges();
    settleCities(http);

    const form = dialog();

    expect(form.localityId).toBe('loc1');

    form.occasionDate = '2026-09-12';
    form.save();

    const call = http.expectOne(`/v1/boli/occasions/${OCCASION_ID}`);

    expect(call.request.method).toBe('PATCH');
    expect(call.request.body).toEqual({
      title: 'Paryushan 2026',
      description: null,
      occasionDate: '2026-09-12',
      localityId: 'loc1',
      venue: null,
      cityId: null,
    });

    call.flush(occasion());
    reload();

    expect(text()).toContain('is saved');
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
        LOCALITIES,
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
    settleCities(http);
    http.expectOne(`/v1/boli/occasions/${OCCASION_ID}`).flush(event);
    fixture.detectChanges();
  }

  it('opens on the Boli, with the types on a tab of their own', () => {
    load(detail({ types: [{ id: 't1', name: 'Swapna', description: null }] }));

    expect(component.tab()).toBe('boli');
    expect(text()).not.toContain('Swapna');

    component.tab.set('types');
    fixture.detectChanges();

    expect(text()).toContain('Swapna');
  });

  it('says where the occasion is held', () => {
    load(detail({ venue: 'Shri Mahavir Mandir', localityId: 'loc1' }));

    expect(text()).toContain('Shri Mahavir Mandir · Hiran Magri, Udaipur');
  });

  it('opens its forms in popups', () => {
    load();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();

    component.openPopup('open');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"] #boli-type')).not.toBeNull();

    component.closePopup();
    component.openPopup('type');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"] #type-name')).not.toBeNull();
  });

  it('shows a Boli in a popup with what was entered, who bid and when', () => {
    const open = lot({ status: 'Open', bidCount: 2, highestAmount: 160_000 });

    load(detail({ boli: [open] }));
    component.showBoli(open);
    fixture.detectChanges();

    http.expectOne('/v1/boli/boli/b1/bids/ledger').flush([
      { id: 'x', memberId: 'm1', amount: 160_000, placedAt: '2026-09-10T10:05:00Z' },
      { id: 'y', memberId: 'm2', amount: 150_000, placedAt: '2026-09-10T10:01:00Z' },
    ]);
    http.expectOne('/v1/boli/boli/b1/result').flush({}, { status: 404, statusText: 'Not Found' });

    // One lookup per bidder, however many bids each placed.
    http.expectOne('/v1/members/m1').flush({ id: 'm1', fullName: 'Ravi Jain', mobile: null, email: null, photoUrl: null });
    http.expectOne('/v1/members/m2').flush({ id: 'm2', fullName: 'Meera Shah', mobile: null, email: null, photoUrl: null });
    fixture.detectChanges();

    const popup = (fixture.nativeElement as HTMLElement).querySelector('[role="dialog"]')!;

    expect(popup.textContent).toContain('One per family.');
    expect(popup.textContent).toContain('Ravi Jain');
    expect(popup.textContent).toContain('Meera Shah');
    expect(popup.textContent).toContain('1,600');
    expect(popup.textContent).toContain('No result yet');
  });

  it('names the winner and how to reach them once the result is announced', () => {
    const done = lot({ status: 'ResultPublished', acceptsBids: false, bidCount: 1, highestAmount: 160_000 });

    load(detail({ boli: [done] }));
    component.showBoli(done);

    http.expectOne('/v1/boli/boli/b1/bids/ledger').flush([
      { id: 'x', memberId: 'm1', amount: 160_000, placedAt: '2026-09-10T10:05:00Z' },
    ]);
    http.expectOne('/v1/boli/boli/b1/result').flush({
      boliId: 'b1', boliTitle: done.title, amount: 160_000, winningMemberId: 'm1',
      isPublished: true, recordedAt: '2026-09-10T12:05:00Z', publishedAt: '2026-09-10T12:10:00Z',
    });
    http.expectOne('/v1/members/m1').flush({
      id: 'm1', fullName: 'Ravi Jain', mobile: '+919812345678', email: 'ravi@example.com', photoUrl: null,
    });
    fixture.detectChanges();

    const popup = (fixture.nativeElement as HTMLElement).querySelector('[role="dialog"]')!;

    expect(popup.textContent).toContain('Won by');
    expect(popup.textContent).toContain('Ravi Jain');
    expect(popup.textContent).toContain('+919812345678');
  });

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
    component.tab.set('boli');
    fixture.detectChanges();

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
    settleCities(http);
    http
      .expectOne(`/v1/boli/occasions/${OCCASION_ID}`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such occasion');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
