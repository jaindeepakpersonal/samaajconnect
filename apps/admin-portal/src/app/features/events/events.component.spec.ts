import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { EventDetailComponent } from './event-detail.component';
import { EventsListComponent } from './events-list.component';
import { Attendee, OrganizerGroup, SamaajEvent } from '../../core/admin.models';

const EVENT_ID = 'e1';

function anEvent(overrides: Partial<SamaajEvent> = {}): SamaajEvent {
  return {
    id: EVENT_ID,
    title: 'Paryushan Lecture',
    description: null,
    startAt: '2026-09-05T10:00:00Z',
    endAt: null,
    venue: 'Jain Bhavan',
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
      ],
    });

    fixture = TestBed.createComponent(EventsListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(events: SamaajEvent[] = [], groups: OrganizerGroup[] = GROUPS, past = false) {
    fixture.detectChanges();
    http
      .expectOne(`/v1/events?includeDrafts=true&includePast=${past}`)
      .flush(events);
    http.expectOne('/v1/volunteer-groups/groups').flush(groups);
    fixture.detectChanges();
  }

  it('asks for drafts, because showing them is the point of the screen', () => {
    // Creating and publishing are separate commands precisely so an event can
    // exist before the Samaaj is told about it. A list without drafts would
    // show the half an administrator does not need to act on.
    load();

    expect(text()).toContain('starts as a draft');
  });

  it('reads a 404 as the community module being off', () => {
    fixture.detectChanges();
    http
      .expectOne('/v1/events?includeDrafts=true&includePast=false')
      .flush({}, { status: 404, statusText: 'Not Found' });
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

  it('offers Publish only on a draft, and Cancel on anything not already off', () => {
    load([anEvent({ status: 'Draft' })]);
    expect(rowButtons()).toEqual(['Publish', 'Cancel']);
  });

  it('offers only Cancel once an event is published', () => {
    load([anEvent({ status: 'Published' })]);
    expect(rowButtons()).toEqual(['Cancel']);
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

  const text = () => fixture.nativeElement.textContent as string;

  function load(
    subject: SamaajEvent = anEvent(),
    list: Attendee[] = [],
    members: { id: string; fullName: string }[] = [],
  ) {
    fixture.detectChanges();
    http.expectOne(`/v1/events/${EVENT_ID}`).flush(subject);
    http.expectOne(`/v1/events/${EVENT_ID}/attendees`).flush(list);
    http.expectOne('/v1/members?limit=100').flush(members);
    fixture.detectChanges();
  }

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

    expect(component.waitlisted()[0]!.memberId).toBe('m2');
    expect(text()).toContain('Aarav Jain');
  });

  it('shows nothing about a waitlist that does not exist', () => {
    load(anEvent(), [attendee()], [{ id: 'm1', fullName: 'Diya Jain' }]);

    expect(text()).toContain('Nobody is waiting');
  });

  it('says "A member" rather than printing an id it could not resolve', () => {
    load(anEvent(), [attendee({ memberId: 'm9' })], []);

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

    expect(text()).toContain('The hall is unavailable.');
    expect(text()).toContain('Diya Jain');
  });

  it('explains a 404 as no such event rather than as an error', () => {
    fixture.detectChanges();
    http.expectOne(`/v1/events/${EVENT_ID}`).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such event');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
