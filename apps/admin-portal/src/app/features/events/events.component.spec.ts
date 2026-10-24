import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { EventDetailComponent } from './event-detail.component';
import { EventsListComponent } from './events-list.component';
import { Attendee, EventScheduleItem, OrganizerGroup, SamaajEvent } from '../../core/admin.models';
import { LocalityOptions } from '../../core/locality-options';

const EVENT_ID = 'e1';

/** Signs the test in as an Area Admin for 'loc-hm'. Build the component after. */
function signInAsAreaAdmin(http: HttpTestingController) {
  TestBed.inject(AuthService).ensureCurrentUser().subscribe();
  http.expectOne('/v1/identity/me').flush({
    userId: 'u9',
    tenantId: 't1',
    tenantSlug: 'dasha-nagda',
    mobileOrEmail: 'area@example.com',
    fullName: 'Area Admin',
    status: 'Active',
    isContactVerified: true,
    lastLoginAt: null,
    roles: ['Member', 'AreaAdmin'],
    permissions: ['Events.Publish'],
    areaLocalityId: 'loc-hm',
  });
}

function anEvent(overrides: Partial<SamaajEvent> = {}): SamaajEvent {
  return {
    id: EVENT_ID,
    title: 'Paryushan Lecture',
    description: null,
    startAt: '2026-09-05T10:00:00Z',
    endAt: null,
    venue: 'Jain Bhavan',
    eventTypeId: null,
    localityId: null,
    organizerType: 'Samaaj',
    organizerId: null,
    status: 'Draft',
    registrationEnabled: true,
    capacity: 200,
    registeredCount: 186,
    waitlistedCount: 0,
    isFull: false,
    myRegistrationStatus: null,
    cancelledAt: null,
    cancellationReason: null,
    createdAt: '2026-08-01T10:00:00Z',
    ...overrides,
  };
}

function attendee(overrides: Partial<Attendee> = {}): Attendee {
  return {
    memberId: 'm1',
    status: 'Registered',
    registeredAt: '2026-08-10T10:00:00Z',
    ...overrides,
  };
}

const GROUPS: OrganizerGroup[] = [{ id: 'g1', name: 'Seva Group' }];

describe('EventsListComponent', () => {
  let fixture: ComponentFixture<EventsListComponent>;
  let component: EventsListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EventsListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: LocalityOptions,
          useValue: {
            load: () => undefined,
            options: () => [{ id: 'loc1', label: 'Hiran Magri, Udaipur' }],
            label: (id: string | null) => (id === 'loc1' ? 'Hiran Magri, Udaipur' : 'Whole Samaaj'),
          },
        },
      ],
    });

    fixture = TestBed.createComponent(EventsListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  /** The Locality picker's names, asked for once on arrival. */
  function flushLocalities() {
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush([]);
  }

  function load(events: SamaajEvent[] = [], groups: OrganizerGroup[] = GROUPS, past = false) {
    fixture.detectChanges();
    flushLocalities();
    http
      .expectOne(`/v1/events?includeDrafts=true&includePast=${past}`)
      .flush(events);
    http.expectOne('/v1/volunteer-groups/groups').flush(groups);
    http
      .expectOne('/v1/master-data/lookup-items?category=EventType&includeInactive=false')
      .flush([]);
    fixture.detectChanges();
  }

  it('offers an Area Admin controls only on their own locality s events', () => {
    signInAsAreaAdmin(http);
    fixture = TestBed.createComponent(EventsListComponent);
    component = fixture.componentInstance;

    load([
      anEvent({ id: 'e1', title: 'Hiran Pravachan', localityId: 'loc-hm' }),
      anEvent({ id: 'e2', title: 'Sector Mela', localityId: 'loc-other', status: 'Published' }),
    ]);

    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr'),
    ) as HTMLElement[];
    const row = (title: string) => rows.find((r) => r.textContent?.includes(title))!;

    expect(row('Hiran Pravachan').textContent).toContain('Publish');
    expect(row('Sector Mela').textContent).toContain('Another locality');
    expect(row('Sector Mela').textContent).not.toContain('Edit');
  });

  it('sets up an Area Admin s event in their own locality', () => {
    signInAsAreaAdmin(http);
    fixture = TestBed.createComponent(EventsListComponent);
    component = fixture.componentInstance;
    load();

    const clickEvent = { currentTarget: document.createElement('button') } as unknown as MouseEvent;
    component.openCreateModal(clickEvent);
    component.title = 'Hiran Pravachan';
    component.startAt = '2026-10-01T18:00';
    component.create();

    const body = http.expectOne('/v1/events').request.body as Record<string, unknown>;
    expect(body['localityId']).toBe('loc-hm');
  });

  it('asks for drafts, because showing them is the point of the screen', () => {
    // Creating and publishing are separate commands precisely so an event can
    // exist before the Samaaj is told about it. A list without drafts would
    // show the half an administrator does not need to act on.
    load();

    expect(text()).toContain('starts as a draft');
  });

  it('reads a 404 as the community module being off', () => {
    fixture.detectChanges();
    flushLocalities();
    http
      .expectOne('/v1/events?includeDrafts=true&includePast=false')
      .flush({}, { status: 404, statusText: 'Not Found' });
    http
      .expectOne('/v1/master-data/lookup-items?category=EventType&includeInactive=false')
      .flush([]);
    fixture.detectChanges();

    expect(text()).toContain('does not run the community module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('shows a capacity as a fraction and no capacity as a bare number', () => {
    // The wireframe's RSVP column: "186 / 200" and "94". No denominator is the
    // unlimited case, not a missing number - "94 / 0" would say something the
    // data does not.
    load([anEvent(), anEvent({ id: 'e2', capacity: null, registeredCount: 94 })]);

    expect(component.rsvp(anEvent())).toBe('186 / 200');
    expect(component.rsvp(anEvent({ capacity: null, registeredCount: 94 }))).toBe('94');
  });

  it('says so when an event takes no registrations at all', () => {
    expect(component.rsvp(anEvent({ registrationEnabled: false }))).toBe('No registration');
  });

  it('names a group organiser and falls back rather than printing an id', () => {
    load([anEvent({ organizerType: 'VolunteerGroup', organizerId: 'g1' })]);

    expect(component.organiser(anEvent({ organizerType: 'VolunteerGroup', organizerId: 'g1' })))
      .toBe('Seva Group');

    expect(component.organiser(anEvent({ organizerType: 'VolunteerGroup', organizerId: 'gX' })))
      .toBe('A volunteer group');

    expect(text()).not.toContain('gX');
  });

  /**
   * The buttons on the row, by their labels.
   *
   * Asserted on rather than the page text, because the status pill renders
   * "Published" and a text search for "Publish" matches it — which is a test
   * that passes for the wrong reason on a draft and fails for the wrong reason
   * on a published event.
   */
  const rowButtons = () =>
    [...fixture.nativeElement.querySelectorAll('tbody button')].map((b) =>
      (b as HTMLElement).textContent?.trim(),
    );

  it('offers Edit and Publish only on a draft, and Cancel on anything not already off', () => {
    load([anEvent({ status: 'Draft' })]);
    expect(rowButtons()).toEqual(['Edit', 'Publish', 'Cancel']);
  });

  it('offers Edit and Cancel once an event is published, but no Publish', () => {
    load([anEvent({ status: 'Published' })]);
    expect(rowButtons()).toEqual(['Edit', 'Cancel']);
  });

  it('offers nothing on a cancelled event', () => {
    // A cancelled event cannot be republished: the people told it was off will
    // not be told again.
    load([anEvent({ status: 'Cancelled', cancellationReason: 'The hall is unavailable.' })]);

    expect(text()).toContain('The hall is unavailable.');
    expect(rowButtons()).toEqual([]);
  });

  it('will not cancel an event without a reason', () => {
    // The service requires one, and somebody who rearranged their day is owed
    // better than "Cancelled".
    load([anEvent({ status: 'Published' })]);

    component.startCancelling(anEvent());
    component.reason = '   ';
    component.cancel(anEvent());

    http.expectNone(`/v1/events/${EVENT_ID}/cancel`);
  });

  it('cancels with the reason, trimmed', () => {
    load([anEvent({ status: 'Published' })]);

    component.reason = '  The hall is unavailable.  ';
    component.cancel(anEvent());

    const call = http.expectOne(`/v1/events/${EVENT_ID}/cancel`);

    expect(call.request.body).toEqual({ reason: 'The hall is unavailable.' });

    call.flush({});
    reload();
  });

  it('creates a Samaaj event when no group is chosen', () => {
    load();

    component.title = ' Paryushan Lecture ';
    component.startAt = '2026-09-05T10:00';
    component.venue = '';
    component.organizerId = '';
    component.registrationEnabled = true;
    component.capacity = 200;
    component.create();

    const body = http.expectOne('/v1/events').request.body as Record<string, unknown>;

    expect(body['title']).toBe('Paryushan Lecture');
    expect(body['organizerType']).toBe('Samaaj');
    expect(body['organizerId']).toBeNull();
    expect(body['venue']).toBeNull();
    expect(body['capacity']).toBe(200);
  });

  it('creates a group event when one is chosen', () => {
    load();

    component.title = 'Community Seva Day';
    component.startAt = '2026-09-12T09:00';
    component.organizerId = 'g1';
    component.create();

    const body = http.expectOne('/v1/events').request.body as Record<string, unknown>;

    expect(body['organizerType']).toBe('VolunteerGroup');
    expect(body['organizerId']).toBe('g1');
  });

  it('sends no capacity rather than zero', () => {
    // Null means no limit; zero would be an event nobody can attend, which the
    // service refuses. A blank box must not become the refused value.
    load();

    component.title = 'Open evening';
    component.startAt = '2026-09-12T09:00';
    component.capacity = null;
    component.create();

    const body = http.expectOne('/v1/events').request.body as Record<string, unknown>;

    expect(body['capacity']).toBeNull();
  });

  it('sends no capacity at all when registration is off', () => {
    load();

    component.title = 'Notice only';
    component.startAt = '2026-09-12T09:00';
    component.registrationEnabled = false;
    component.capacity = 50;
    component.create();

    const body = http.expectOne('/v1/events').request.body as Record<string, unknown>;

    expect(body['registrationEnabled']).toBe(false);
    expect(body['capacity']).toBeNull();
  });

  it('will not create an event with no title or no start', () => {
    load();

    component.title = 'Paryushan Lecture';
    component.startAt = '';
    component.create();

    http.expectNone('/v1/events');
  });

  // ---- Editing --------------------------------------------------------------

  it('opens the edit modal pre-filled from the row, and never shows the Organiser field', () => {
    load([anEvent({ venue: 'Jain Bhavan', capacity: 200 })]);

    const clickEvent = { currentTarget: document.createElement('button') } as unknown as MouseEvent;
    component.openEditModal(clickEvent, anEvent({ venue: 'Jain Bhavan', capacity: 200 }));
    fixture.detectChanges();

    expect(component.title).toBe('Paryushan Lecture');
    expect(component.venue).toBe('Jain Bhavan');
    expect(component.capacity).toBe(200);
    expect(text()).toContain('Organiser:');
    expect(fixture.nativeElement.querySelector('#event-organiser')).toBeNull();
  });

  it('sends a correction with no organiser field at all', () => {
    load();

    const clickEvent = { currentTarget: document.createElement('button') } as unknown as MouseEvent;
    component.openEditModal(clickEvent, anEvent());
    component.title = 'Corrected Title';
    component.update();

    const body = http.expectOne(`/v1/events/${EVENT_ID}`).request.body as Record<string, unknown>;

    expect(body['title']).toBe('Corrected Title');
    expect(body['organizerType']).toBeUndefined();
    expect(body['organizerId']).toBeUndefined();
  });

  it('re-reads the list when past events are asked for', () => {
    load();

    component.setIncludePast(true);

    http.expectOne('/v1/events?includeDrafts=true&includePast=true').flush([]);
    http.expectOne('/v1/volunteer-groups/groups').flush(GROUPS);
    fixture.detectChanges();
  });

  function reload() {
    http.expectOne('/v1/events?includeDrafts=true&includePast=false').flush([anEvent()]);
    http.expectOne('/v1/volunteer-groups/groups').flush(GROUPS);
    fixture.detectChanges();
  }
});

describe('EventDetailComponent', () => {
  let fixture: ComponentFixture<EventDetailComponent>;
  let component: EventDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EventDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: LocalityOptions,
          useValue: {
            load: () => undefined,
            options: () => [{ id: 'loc1', label: 'Hiran Magri, Udaipur' }],
            label: (id: string | null) => (id === 'loc1' ? 'Hiran Magri, Udaipur' : 'Whole Samaaj'),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => EVENT_ID } } },
        },
      ],
    });

    fixture = TestBed.createComponent(EventDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** The page is tabs now; a test about one section opens it first. */
  function showTab(name: 'about' | 'schedule' | 'going') {
    component.tab.set(name);
    fixture.detectChanges();
  }

  const text = () => fixture.nativeElement.textContent as string;

  function load(
    subject: SamaajEvent = anEvent(),
    list: Attendee[] = [],
    members: { id: string; fullName: string }[] = [],
  ) {
    fixture.detectChanges();
    http.expectOne(`/v1/events/${EVENT_ID}`).flush(subject);
    http
      .expectOne('/v1/master-data/lookup-items?category=EventType&includeInactive=false')
      .flush([]);
    http.expectOne(`/v1/events/${EVENT_ID}/attendees`).flush(list);
    http.expectOne('/v1/members?take=100').flush({ items: members, hasMore: false });
    fixture.detectChanges();
  }

  it('does not ask an Area Admin for the attendees of an event elsewhere', () => {
    signInAsAreaAdmin(http);
    fixture = TestBed.createComponent(EventDetailComponent);
    component = fixture.componentInstance;

    fixture.detectChanges();
    http
      .expectOne(`/v1/events/${EVENT_ID}`)
      .flush(anEvent({ localityId: 'loc-other', status: 'Published' }));
    http
      .expectOne('/v1/master-data/lookup-items?category=EventType&includeInactive=false')
      .flush([]);
    fixture.detectChanges();

    // http.verify() in afterEach is the other half: no attendee call was made.
    expect(text()).toContain('another locality');
  });

  // ---- The schedule ----------------------------------------------------------

  const programme = (overrides: Partial<EventScheduleItem> = {}): EventScheduleItem => ({
    id: 's1',
    date: '2026-09-05',
    programName: 'Pravachan',
    startTime: '10:00:00',
    endTime: '11:30:00',
    ...overrides,
  });

  it('lists the schedule with its local dates and wall-clock times', () => {
    load(anEvent({ schedule: [programme(), programme({ id: 's2', programName: 'Pratikraman', startTime: '18:30:00', endTime: null })] }));
    showTab('schedule');

    expect(text()).toContain('Pravachan');
    expect(text()).toContain('10:00 AM');
    expect(text()).toContain('11:30 AM');
    expect(text()).toContain('6:30 PM');
    expect(text()).toContain('Sat, 5 Sep 2026');
  });

  it('says there are no programmes yet', () => {
    load();
    showTab('schedule');

    expect(text()).toContain('No programmes yet');
  });

  it('adds a programme from a popup, sending seconds the service expects', () => {
    load();
    showTab('schedule');

    component.openScheduleModal(null);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="dialog"] #programme-name')).not.toBeNull();

    component.programName = '  Prabhat Pheri ';
    component.programDate = '2026-09-05';
    component.programStart = '06:00';
    component.programEnd = '07:00';
    component.saveScheduleItem(component.event()!);

    const request = http.expectOne((r) => r.method === 'POST' && r.url === `/v1/events/${EVENT_ID}/schedule`);
    expect(request.request.body).toEqual({
      date: '2026-09-05',
      programName: 'Prabhat Pheri',
      startTime: '06:00:00',
      endTime: '07:00:00',
    });
    request.flush(anEvent({ schedule: [programme({ programName: 'Prabhat Pheri' })] }));
    fixture.detectChanges();

    expect(component.scheduleModal()).toBeNull();
    expect(text()).toContain('Prabhat Pheri');
  });

  it('refuses a programme that ends before it starts, before asking', () => {
    load();

    component.openScheduleModal(null);
    component.programName = 'Pravachan';
    component.programStart = '11:00';
    component.programEnd = '10:00';

    expect(component.endsBeforeStart()).toBe(true);
    expect(component.canSaveScheduleItem(component.event()!)).toBe(false);

    component.saveScheduleItem(component.event()!);
    http.expectNone((r) => r.method === 'POST');
  });

  it('edits and removes a programme', () => {
    load(anEvent({ schedule: [programme()] }));
    showTab('schedule');

    component.openScheduleModal(programme());
    expect(component.programStart).toBe('10:00');
    component.programName = 'Pravachan by Muni Shri';
    component.saveScheduleItem(component.event()!);

    http
      .expectOne((r) => r.method === 'PUT' && r.url === `/v1/events/${EVENT_ID}/schedule/s1`)
      .flush(anEvent({ schedule: [programme({ programName: 'Pravachan by Muni Shri' })] }));

    component.removeScheduleItem(component.event()!, programme());
    http
      .expectOne((r) => r.method === 'DELETE' && r.url === `/v1/events/${EVENT_ID}/schedule/s1`)
      .flush(anEvent());
    fixture.detectChanges();

    expect(text()).toContain('No programmes yet');
  });

  it('offers no schedule controls on a cancelled event', () => {
    load(anEvent({ status: 'Cancelled', schedule: [programme()] }));
    showTab('schedule');

    expect(component.canEdit(component.event()!)).toBe(false);
    expect(text()).not.toContain('Add a programme');
  });

  it('asks the service for the one event rather than filtering the list', () => {
    // The 404-for-a-draft check belongs to the server. Filtering a list
    // client-side would reach the same rows by asking a different question.
    load();

    expect(text()).toContain('Paryushan Lecture');
  });

  it('separates going, waiting, and gave up a place', () => {
    // The cancelled row is what makes this test mean anything. Without one,
    // "not registered" and "waitlisted" pick out the same people, and a
    // waitlist that quietly included members who had given up their place
    // would pass - somebody who cancelled is not in the queue, and showing
    // them in it would put a position number against a person who is not
    // waiting for anything.
    load(
      anEvent({ capacity: 1, registeredCount: 1, waitlistedCount: 2 }),
      [
        attendee({ memberId: 'm1' }),
        attendee({ memberId: 'm2', status: 'Waitlisted', registeredAt: '2026-08-11T10:00:00Z' }),
        attendee({ memberId: 'm3', status: 'Waitlisted', registeredAt: '2026-08-12T10:00:00Z' }),
        attendee({ memberId: 'm4', status: 'Cancelled', registeredAt: '2026-08-09T10:00:00Z' }),
      ],
      [
        { id: 'm1', fullName: 'Diya Jain' },
        { id: 'm2', fullName: 'Aarav Jain' },
        { id: 'm3', fullName: 'Meera Jain' },
        { id: 'm4', fullName: 'Rohan Jain' },
      ],
    );
    showTab('going');

    expect(component.registered().map((a) => a.memberId)).toEqual(['m1']);
    expect(component.waitlisted().map((a) => a.memberId)).toEqual(['m2', 'm3']);
    expect(component.cancelled().map((a) => a.memberId)).toEqual(['m4']);
  });

  it('keeps the waitlist in the order it arrived in', () => {
    // The order is the substance: the longest wait comes off the queue first
    // when a place is given up, and an organiser is asked where somebody stands.
    load(
      anEvent(),
      [
        attendee({ memberId: 'm2', status: 'Waitlisted', registeredAt: '2026-08-11T10:00:00Z' }),
        attendee({ memberId: 'm3', status: 'Waitlisted', registeredAt: '2026-08-12T10:00:00Z' }),
      ],
      [
        { id: 'm2', fullName: 'Aarav Jain' },
        { id: 'm3', fullName: 'Meera Jain' },
      ],
    );
    showTab('going');

    expect(component.waitlisted()[0]!.memberId).toBe('m2');
    expect(text()).toContain('Aarav Jain');
  });

  it('shows nothing about a waitlist that does not exist', () => {
    load(anEvent(), [attendee()], [{ id: 'm1', fullName: 'Diya Jain' }]);
    showTab('going');

    expect(text()).toContain('Nobody is waiting');
  });

  it('says "A member" rather than printing an id it could not resolve', () => {
    load(anEvent(), [attendee({ memberId: 'm9' })], []);
    showTab('going');

    expect(text()).toContain('A member');
    expect(text()).not.toContain('m9');
  });

  it('explains a cancelled event and keeps its registrations on screen', () => {
    // The attendee list is what makes the people who were coming notifiable,
    // which is exactly why cancelling keeps it.
    load(
      anEvent({ status: 'Cancelled', cancellationReason: 'The hall is unavailable.' }),
      [attendee()],
      [{ id: 'm1', fullName: 'Diya Jain' }],
    );
    showTab('going');

    expect(text()).toContain('The hall is unavailable.');
    expect(text()).toContain('Diya Jain');
  });

  // ---- The page: a cover, tabs and an Edit popup -------------------------------

  it('opens on About, with Schedule and Going as tabs', () => {
    load(anEvent({ schedule: [programme()] }), [attendee()]);

    const tabs = [...fixture.nativeElement.querySelectorAll('[role="tab"]')].map((t) => (t as HTMLElement).textContent?.trim());

    expect(tabs).toEqual(['About', 'Schedule (1)', 'Going (1)']);
    expect(text()).toContain('Jain Bhavan');
    expect(fixture.nativeElement.querySelector('.event-date-badge')).not.toBeNull();
  });

  it('edits the event in a popup and redraws from what the service answers', () => {
    load();

    component.openEditModal(component.event()!);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="dialog"] #edit-title')).not.toBeNull();

    component.title = 'Paryushan Pravachan';
    component.saveDetails(component.event()!);

    const request = http.expectOne((r) => r.method === 'PATCH' && r.url === `/v1/events/${EVENT_ID}`);
    expect(request.request.body.title).toBe('Paryushan Pravachan');
    expect(request.request.body.capacity).toBe(200);
    request.flush(anEvent({ title: 'Paryushan Pravachan' }));
    fixture.detectChanges();

    expect(component.editOpen()).toBe(false);
    expect(text()).toContain('Paryushan Pravachan');
  });

  it('uploads a photo from the popup, and shows it as the cover', () => {
    load();

    component.openEditModal(component.event()!);
    component.editPart.set('photo');
    fixture.detectChanges();

    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'cover.png', { type: 'image/png' });
    component.choosePhoto(component.event()!, { target: { files: [file], value: '' } } as unknown as Event);

    const upload = http.expectOne((r) => r.method === 'POST' && r.url === `/v1/events/${EVENT_ID}/photo`);
    expect(upload.request.body instanceof FormData).toBe(true);
    upload.flush(anEvent({ photoUrl: `/v1/events/${EVENT_ID}/photo` }));
    fixture.detectChanges();

    for (const photo of http.match(`/v1/events/${EVENT_ID}/photo`)) {
      photo.flush(new Blob(['x'], { type: 'image/png' }));
    }
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img.event-cover-img')).not.toBeNull();
  });

  it('refuses a programme outside the event days, before asking', () => {
    // A one-day event with no end takes programmes on its start day only.
    load(anEvent({ startAt: new Date(2026, 8, 5, 10, 0).toISOString(), endAt: null }));

    component.openScheduleModal(null);
    expect(component.programDate).toBe('2026-09-05');

    component.programName = 'Pravachan';
    component.programStart = '10:00';
    component.programDate = '2026-09-08';
    showTab('schedule');

    expect(component.dateOutsideEvent(component.event()!)).toBe(true);
    expect(component.canSaveScheduleItem(component.event()!)).toBe(false);
    expect(text()).toContain("has to fall on one of the event's own days");

    component.saveScheduleItem(component.event()!);
    http.expectNone((r) => r.method === 'POST');
  });

  it('takes a programme on any day of a multi-day event', () => {
    load(anEvent({ startAt: new Date(2026, 8, 5, 10, 0).toISOString(), endAt: new Date(2026, 8, 7, 18, 0).toISOString() }));

    component.openScheduleModal(null);
    component.programDate = '2026-09-07';

    expect(component.dateOutsideEvent(component.event()!)).toBe(false);
    expect(component.dayRange(component.event()!)).toBe('5 Sept 2026 to 7 Sept 2026');
  });

  it('offers no Edit on a cancelled event', () => {
    load(anEvent({ status: 'Cancelled', cancellationReason: 'Off.' }));

    expect(component.canEdit(component.event()!)).toBe(false);
    expect([...fixture.nativeElement.querySelectorAll('button')].some((b) => (b as HTMLElement).textContent?.trim() === 'Edit')).toBe(false);
  });

  it('explains a 404 as no such event rather than as an error', () => {
    fixture.detectChanges();
    http.expectOne(`/v1/events/${EVENT_ID}`).flush({}, { status: 404, statusText: 'Not Found' });
    http
      .expectOne('/v1/master-data/lookup-items?category=EventType&includeInactive=false')
      .flush([]);
    fixture.detectChanges();

    expect(text()).toContain('No such event');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
