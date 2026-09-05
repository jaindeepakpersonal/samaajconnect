import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { EventDetailComponent } from './event-detail.component';
import { EventsListComponent } from './events-list.component';
import { SamaajEvent } from './events.models';

/** Two days out, so it is upcoming whenever the suite runs. */
const SOON = new Date(Date.now() + 2 * 86400000).toISOString();

/** A fortnight ago, so it has definitely passed. */
const GONE = new Date(Date.now() - 14 * 86400000).toISOString();

function event(overrides: Partial<SamaajEvent> = {}): SamaajEvent {
  return {
    id: 'e1',
    title: 'Paryushan Lecture',
    description: 'An evening lecture on Jain philosophy.',
    startAt: SOON,
    endAt: null,
    venue: 'Community Hall',
    organizerType: 'Samaaj',
    organizerId: null,
    status: 'Published',
    registrationEnabled: true,
    capacity: 200,
    registeredCount: 186,
    waitlistedCount: 0,
    isFull: false,
    myRegistrationStatus: null,
    cancelledAt: null,
    cancellationReason: null,
    createdAt: GONE,
    ...overrides,
  };
}

function providers() {
  return [
    provideRouter([]),
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
  ];
}

describe('EventsListComponent', () => {
  let fixture: ComponentFixture<EventsListComponent>;
  let component: EventsListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [EventsListComponent], providers: providers() });

    fixture = TestBed.createComponent(EventsListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(events: SamaajEvent[]): void {
    fixture.detectChanges();
    http.expectOne('/v1/events').flush(events);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  // ---- The two states the wireframe drew --------------------------------

  it('offers an RSVP on an open event', () => {
    load([event()]);

    expect(component.status(component.events()[0]!)).toBe('Open');
    expect(component.action(component.events()[0]!)).toBe('View / RSVP');
  });

  it('offers the waitlist on a full one', () => {
    load([event({ isFull: true, registeredCount: 200 })]);

    expect(component.status(component.events()[0]!)).toBe('Full — waitlist');
    expect(component.action(component.events()[0]!)).toBe('Join waitlist');
    expect(text()).toContain('Full — waitlist');
  });

  // ---- The states it did not ---------------------------------------------

  it('says where the member stands, in preference to whether it is full', () => {
    // A member who is already going does not need to be told the event is
    // full; they need to be told they have a place.
    load([event({ isFull: true, myRegistrationStatus: 'Registered' })]);

    expect(component.status(component.events()[0]!)).toBe('You are going');
  });

  it('reports a waitlisted member as waiting', () => {
    load([event({ isFull: true, myRegistrationStatus: 'Waitlisted' })]);

    expect(component.status(component.events()[0]!)).toBe('You are on the waitlist');
  });

  it('marks a cancelled event cancelled whatever else is true of it', () => {
    load([event({ status: 'Cancelled', myRegistrationStatus: 'Registered' })]);

    expect(component.status(component.events()[0]!)).toBe('Cancelled');
    expect(component.pillClass(component.events()[0]!)).toBe('danger');
  });

  it('does not claim an event with no limit is ever full', () => {
    // Null capacity is no limit, which is not a limit of zero.
    load([event({ capacity: null, isFull: false, registeredCount: 900 })]);

    expect(component.status(component.events()[0]!)).toBe('Open');
  });

  it('says an event needs no RSVP when registration is switched off', () => {
    load([event({ registrationEnabled: false })]);

    expect(component.status(component.events()[0]!)).toBe('No RSVP needed');
    expect(component.action(component.events()[0]!)).toBe('View');
  });

  // ---- Ordering -----------------------------------------------------------

  it('separates what is coming from what has already happened', () => {
    load([
      event({ id: 'past', title: 'Last month', startAt: GONE }),
      event({ id: 'next', title: 'Next week', startAt: SOON }),
    ]);

    expect(component.upcoming().map((e) => e.id)).toEqual(['next']);
    expect(component.past().map((e) => e.id)).toEqual(['past']);
    expect(text()).toContain('Already happened');
  });

  it('puts the soonest event first and the most recent past event first', () => {
    const later = new Date(Date.now() + 9 * 86400000).toISOString();
    const longAgo = new Date(Date.now() - 60 * 86400000).toISOString();

    load([
      event({ id: 'later', startAt: later }),
      event({ id: 'sooner', startAt: SOON }),
      event({ id: 'long-ago', startAt: longAgo }),
      event({ id: 'recent', startAt: GONE }),
    ]);

    expect(component.upcoming().map((e) => e.id)).toEqual(['sooner', 'later']);
    expect(component.past().map((e) => e.id)).toEqual(['recent', 'long-ago']);
  });

  it('names the kind of organiser rather than inventing a name', () => {
    // Group names live in volunteer-groups-service; the list carries an id.
    load([event({ organizerType: 'VolunteerGroup', organizerId: 'g1' })]);

    expect(component.organiser(component.events()[0]!)).toBe('A volunteer group');
    expect(text()).not.toContain('g1');
  });

  it('says the Samaaj has nothing on rather than showing an empty table', () => {
    load([]);

    expect(text()).toContain('no events coming up');
  });

  it('offers a retry when the list cannot be loaded', () => {
    fixture.detectChanges();
    http.expectOne('/v1/events').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });
});

describe('EventDetailComponent', () => {
  let fixture: ComponentFixture<EventDetailComponent>;
  let component: EventDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [EventDetailComponent],
      providers: [
        ...providers(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'e1' } } },
        },
      ],
    });

    fixture = TestBed.createComponent(EventDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(found: SamaajEvent): void {
    fixture.detectChanges();
    http.expectOne('/v1/events/e1').flush(found);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function buttonSaying(label: string): HTMLButtonElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((button) => button.textContent?.trim().startsWith(label)) as
      | HTMLButtonElement
      | undefined;
  }

  // ---- The capacity bar ---------------------------------------------------

  it('shows the real capacity and registered count, not the prototype numbers', () => {
    load(event({ capacity: 200, registeredCount: 186 }));

    expect(text()).toContain('200');
    expect(text()).toContain('186');
    expect(component.fillPercent(component.event()!)).toBe(93);
  });

  it('never draws a bar wider than its track', () => {
    // An event can hold more registrations than its capacity if the capacity
    // was lowered afterwards.
    load(event({ capacity: 100, registeredCount: 140 }));

    expect(component.fillPercent(component.event()!)).toBe(100);
  });

  it('says there is no limit rather than drawing an empty bar', () => {
    load(event({ capacity: null, registeredCount: 12 }));

    expect(text()).toContain('No limit on places');
    expect((fixture.nativeElement as HTMLElement).querySelector('.progress')).toBeNull();
  });

  // ---- The one button that does four things -------------------------------

  it('offers an RSVP when there is room', () => {
    load(event());

    expect(buttonSaying('RSVP')).toBeDefined();
  });

  it('offers the waitlist when there is not', () => {
    load(event({ isFull: true }));

    expect(buttonSaying('Join the waitlist')).toBeDefined();
  });

  it('offers a way out to somebody who is going', () => {
    load(event({ myRegistrationStatus: 'Registered' }));

    expect(buttonSaying('Cannot make it')).toBeDefined();
    expect(text()).toContain('You are going');
  });

  it('offers a way out of the queue to somebody waiting', () => {
    load(event({ myRegistrationStatus: 'Waitlisted', isFull: true }));

    expect(buttonSaying('Leave the waitlist')).toBeDefined();
    expect(text()).toContain('On the waitlist');
  });

  it('offers nothing to RSVP to on a cancelled event, and says why', () => {
    load(
      event({
        status: 'Cancelled',
        cancellationReason: 'The hall flooded.',
        myRegistrationStatus: 'Registered',
      }),
    );

    expect(text()).toContain('The hall flooded.');
    expect(buttonSaying('Cannot make it')).toBeUndefined();
  });

  it('closes RSVPs on an event that has already happened', () => {
    load(event({ startAt: GONE }));

    expect(text()).toContain('already happened');
    expect(buttonSaying('RSVP')).toBeUndefined();
  });

  it('says nothing is needed when registration is switched off', () => {
    load(event({ registrationEnabled: false }));

    expect(text()).toContain('does not need an RSVP');
  });

  // ---- Registering --------------------------------------------------------

  it('takes the queue position from the server rather than guessing it', () => {
    // One call covers both outcomes, and which one happened is the server's to
    // decide - the portal cannot see the current count.
    load(event({ isFull: true }));

    component.register();

    http
      .expectOne('/v1/events/e1/registration')
      .flush({ eventId: 'e1', status: 'Waitlisted', position: 4 });

    // The event is re-read, because the counts have moved.
    http.expectOne('/v1/events/e1').flush(event({ isFull: true, myRegistrationStatus: 'Waitlisted' }));
    fixture.detectChanges();

    expect(component.position()).toBe(4);
    expect(text()).toContain('number 4 in the queue');
  });

  it('keeps no stale queue position when the RSVP got a place', () => {
    load(event());

    component.register();

    http
      .expectOne('/v1/events/e1/registration')
      .flush({ eventId: 'e1', status: 'Registered', position: 0 });

    http.expectOne('/v1/events/e1').flush(event({ myRegistrationStatus: 'Registered' }));
    fixture.detectChanges();

    expect(component.position()).toBeNull();
    expect(text()).toContain('Your place is held');
  });

  it('says the general thing when it does not know the queue position', () => {
    // A member who was already waiting before this page load: the event
    // carries the waitlist size, not their place in it.
    load(event({ myRegistrationStatus: 'Waitlisted', isFull: true }));

    expect(component.position()).toBeNull();
    expect(text()).toContain('waited longest');
    expect(text()).not.toContain('number');
  });

  it('tells a member who withdrew that their place went to somebody', () => {
    load(event({ myRegistrationStatus: 'Registered' }));

    component.withdraw();

    http
      .expectOne('/v1/events/e1/registration')
      .flush({ eventId: 'e1', cancelled: true, promotedMemberId: 'm9' });

    http.expectOne('/v1/events/e1').flush(event({ myRegistrationStatus: 'Cancelled' }));
    fixture.detectChanges();

    expect(text()).toContain('went to somebody who was waiting');

    // Without naming them: who is going is not this member's business once
    // they have left.
    expect(text()).not.toContain('m9');
  });

  it('says nothing about a promotion when nobody was waiting', () => {
    load(event({ myRegistrationStatus: 'Registered' }));

    component.withdraw();

    http
      .expectOne('/v1/events/e1/registration')
      .flush({ eventId: 'e1', cancelled: true, promotedMemberId: null });

    http.expectOne('/v1/events/e1').flush(event({ myRegistrationStatus: 'Cancelled' }));
    fixture.detectChanges();

    expect(text()).not.toContain('went to somebody');
    expect(text()).toContain('Not registered');
  });

  it('keeps a failed RSVP off the page-level error', () => {
    load(event());

    component.register();

    http
      .expectOne('/v1/events/e1/registration')
      .flush({ title: 'Event.Full', detail: 'No room.' }, { status: 409, statusText: 'Conflict' });

    fixture.detectChanges();

    // The event is still on screen; only the action reports a problem.
    expect(component.error()).toBeNull();
    expect(component.actionError()).not.toBeNull();
    expect(text()).toContain('Paryushan Lecture');
  });

  // ---- What is not built --------------------------------------------------

  it('does not promise a reminder nothing sends', () => {
    // The wireframe said "You'll receive a notification reminder 24 hours
    // before". There is no notification channel on this platform yet.
    load(event());

    expect(text()).not.toContain('24 hours');
    expect(text()).not.toContain('reminder');
  });
});
