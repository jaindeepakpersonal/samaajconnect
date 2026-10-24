import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { CompetitionDetailComponent } from './competition-detail.component';
import { CompetitionsListComponent } from './competitions-list.component';
import {
  Competition,
  CompetitionDetail,
  CompetitionEntry,
  CompetitionResult,
  RoundDetail,
} from './competitions.models';

const ME = 'u1';

const currentUser: CurrentUser = {
  userId: ME,
  tenantId: 't1',
  tenantSlug: 'mahavir-samaj',
  mobileOrEmail: 'ravi@example.com',
  fullName: 'Ravi Shah',
  status: 'Active',
  isContactVerified: true,
  lastLoginAt: null,
  roles: ['Member'],
  permissions: ['Members.Read'],
};

const SOON = new Date(Date.now() + 7 * 86400000).toISOString();

function competition(overrides: Partial<Competition> = {}): Competition {
  return {
    id: 'comp1',
    title: 'Rangoli Contest 2026',
    description: null,
    competitionTypeId: null,
    registrationStartAt: SOON,
    registrationEndAt: SOON,
    status: 'RegistrationOpen',
    acceptsRegistrations: true,
    registrationCount: 2,
    myRegistrationStatus: null,
    isJudge: false,
    createdAt: SOON,
    ...overrides,
  };
}

function entry(overrides: Partial<CompetitionEntry> = {}): CompetitionEntry {
  return {
    id: 'entry1',
    participantMemberId: 'm1',
    createdAt: SOON,
    averageScore: null,
    myScore: null,
    ...overrides,
  };
}

function detail(competitionOverrides: Partial<Competition> = {}): CompetitionDetail {
  return {
    competition: competition(competitionOverrides),
    rounds: [
      {
        id: 'round1',
        name: 'Final',
        sequenceNumber: 1,
        status: 'Open',
        createdAt: SOON,
        closedAt: null,
        entryCount: 1,
        hasPublishedResult: false,
      },
    ],
    judges: [],
  };
}

function roundDetail(overrides: Partial<RoundDetail> = {}): RoundDetail {
  return {
    round: {
      id: 'round1',
      name: 'Final',
      sequenceNumber: 1,
      status: 'Open',
      createdAt: SOON,
      closedAt: null,
      entryCount: 1,
      hasPublishedResult: false,
    },
    entries: [entry()],
    canSeeScores: false,
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

describe('CompetitionsListComponent', () => {
  let fixture: ComponentFixture<CompetitionsListComponent>;
  let component: CompetitionsListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionsListComponent],
      providers: providers(),
    });

    fixture = TestBed.createComponent(CompetitionsListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(competitions: Competition[]): void {
    fixture.detectChanges();
    http.expectOne('/v1/competitions').flush(competitions);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('offers to register while registration is open', () => {
    load([competition({ acceptsRegistrations: true, myRegistrationStatus: null })]);

    expect(component.action(component.competitions()[0]!)).toBe('Register');
  });

  it('does not offer registration on a competition whose window has passed', () => {
    // The status still says RegistrationOpen; the clock does not - only the
    // server knows the time it is deciding against.
    load([competition({ status: 'RegistrationOpen', acceptsRegistrations: false })]);

    expect(component.action(component.competitions()[0]!)).toBe('View');
    expect(component.describe(component.competitions()[0]!)).toContain('Registration has closed');
  });

  it('separates finished competitions from live ones', () => {
    load([
      competition({ id: 'live' }),
      competition({ id: 'done', status: 'Closed', acceptsRegistrations: false }),
    ]);

    expect(component.current().map((c) => c.id)).toEqual(['live']);
    expect(component.past().map((c) => c.id)).toEqual(['done']);
    expect(text()).toContain('Past competitions');
  });

  it('says a member is already registered rather than offering again', () => {
    load([competition({ acceptsRegistrations: true, myRegistrationStatus: 'Registered' })]);

    expect(text()).toContain('You are registered');
  });

  it('marks a competition this member is judging', () => {
    load([competition({ isJudge: true })]);

    expect(text()).toContain('You are judging');
  });

  it('says the Samaaj has run none rather than showing an empty grid', () => {
    load([]);

    expect(text()).toContain('has not run a competition');
  });

  it('offers a retry when the list cannot be loaded', () => {
    fixture.detectChanges();
    http.expectOne('/v1/competitions').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });
});

describe('CompetitionDetailComponent', () => {
  let fixture: ComponentFixture<CompetitionDetailComponent>;
  let component: CompetitionDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionDetailComponent],
      providers: [
        ...providers(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'comp1' } } } },
      ],
    });

    URL.createObjectURL = () => 'blob:test';
    URL.revokeObjectURL = () => undefined;

    fixture = TestBed.createComponent(CompetitionDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    settlePeople();
    http.verify();
  });

  function load(found: CompetitionDetail): void {
    fixture.detectChanges();

    http.expectOne((r) => r.url === '/v1/master-data/cities')
      .flush([{ id: 'udr', countryId: 'in', name: 'Udaipur', isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/localities')
      .flush([{ id: 'loc1', cityId: 'udr', name: 'Hiran Magri', isActive: true }]);
    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne('/v1/competitions/comp1').flush(found);

    fixture.detectChanges();
  }

  /**
   * Answers every member lookup - "hidden" as one this reader may not see,
   * anybody else as a named member with a photo - and the photo fetches
   * `scAuthedSrc` then makes.
   */
  function settlePeople(): void {
    for (const request of http.match((r) => r.url.startsWith('/v1/members/'))) {
      const id = request.request.url.split('/').pop()!;

      if (id === 'hidden') {
        request.flush({}, { status: 404, statusText: 'Not Found' });
      } else {
        request.flush({ id, fullName: `Member ${id}`, photoUrl: `/v1/members/${id}/photo` });
      }
    }

    fixture.detectChanges();

    for (const request of http.match((r) => r.url.includes('/photo') && r.method === 'GET')) {
      request.flush(new Blob(['x']));
    }

    fixture.detectChanges();
  }

  function showTab(label: string): void {
    [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="tab"]')]
      .find((t) => t.textContent?.trim().startsWith(label))!
      .click();
    fixture.detectChanges();
  }

  // ---- The page -------------------------------------------------------------

  it('opens like a page: cover, title, where it is held, and its stage', () => {
    load(detail({
      photoUrl: '/v1/competitions/comp1/photo?v=1',
      venue: 'Mahavir Bhawan, Hall 2',
      localityId: 'loc1',
      cityId: 'udr',
    }));
    settlePeople();

    expect((fixture.nativeElement as HTMLElement).querySelector('img.comp-cover')).not.toBeNull();
    expect(text()).toContain('Rangoli Contest 2026');
    expect(text()).toContain('Mahavir Bhawan, Hall 2 · Hiran Magri, Udaipur');
    expect(text()).toContain('2 registered');
  });

  it('shows initials where there is no photo', () => {
    load(detail());

    expect((fixture.nativeElement as HTMLElement).querySelector('.comp-cover.placeholder')!.textContent!.trim())
      .toBe('RC');
  });

  it('opens on Details, with the window and the judges by name', () => {
    load({ ...detail(), judges: [{ id: 'j1', memberId: 'm7', assignedAt: SOON }] });
    settlePeople();

    expect(component.tab()).toBe('details');
    expect(text()).toContain('Registration opens');
    expect(text()).toContain('Member m7');
    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/members/m7"]')).not.toBeNull();
  });

  it('lists who is registered on Participants, by name, linked to their profile', () => {
    load({ ...detail(), registeredMemberIds: [ME, 'm2', 'hidden'] });

    showTab('Participants');
    settlePeople();

    const people = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.person-card')]
      .map((p) => p.textContent!.trim());

    expect(people.length).toBe(3);
    expect(people[0]).toContain('You');
    expect(people[1]).toContain('Member m2');
    expect(people[2]).toContain('A member');
    const tab = (fixture.nativeElement as HTMLElement).querySelector('#competition-tab-participants')!;
    expect(tab.textContent!.replace(/\s+/g, ' ').trim()).toBe('Participants (3)');
  });

  it('looks participants up a page at a time', () => {
    const many = Array.from({ length: 60 }, (_, i) => `p${i}`);
    load({ ...detail(), registeredMemberIds: many });

    showTab('Participants');

    expect(http.match((r) => r.url.startsWith('/v1/members/')).length).toBe(48);
    expect(text()).toContain('Show more (12 more)');

    component.showMoreParticipants();

    expect(http.match((r) => r.url.startsWith('/v1/members/')).length).toBe(12);
  });

  it('says nobody has registered rather than an empty list', () => {
    load({ ...detail(), registeredMemberIds: [] });

    showTab('Participants');

    expect(text()).toContain('Nobody has registered yet');
  });

  it('opens the Rounds tab when a round is chosen', () => {
    load(detail());

    component.selectRound(component.detail()!.rounds[0]!);
    http.expectOne('/v1/competitions/comp1/rounds/round1').flush(roundDetail());
    fixture.detectChanges();

    expect(component.tab()).toBe('rounds');
    expect(text()).toContain('Final');
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  // ---- Registering ----------------------------------------------------------

  it('registers and re-reads, because the registered count has moved', () => {
    load(detail({ acceptsRegistrations: true, myRegistrationStatus: null }));

    component.register();

    http
      .expectOne('/v1/competitions/comp1/registrations')
      .flush({ competitionId: 'comp1', memberId: ME, registered: true });

    http.expectOne('/v1/competitions/comp1').flush(detail({ myRegistrationStatus: 'Registered' }));
    fixture.detectChanges();

    expect(text()).toContain('You are registered');
  });

  it('reports a repeat registration as success, not as an error', () => {
    load(detail({ acceptsRegistrations: true, myRegistrationStatus: null }));

    component.register();

    http
      .expectOne('/v1/competitions/comp1/registrations')
      .flush({ competitionId: 'comp1', memberId: ME, registered: false });

    http.expectOne('/v1/competitions/comp1').flush(detail({ myRegistrationStatus: 'Registered' }));
    fixture.detectChanges();

    expect(component.registerError()).toBeNull();
    expect(text()).toContain('already registered');
  });

  it('withdraws and offers to register again', () => {
    load(detail({ acceptsRegistrations: true, myRegistrationStatus: 'Registered' }));

    component.withdraw();

    http
      .expectOne('/v1/competitions/comp1/registrations/mine')
      .flush({ competitionId: 'comp1', memberId: ME, registered: false });

    http.expectOne('/v1/competitions/comp1').flush(detail({ myRegistrationStatus: 'Withdrawn' }));
    fixture.detectChanges();

    expect(text()).toContain('You have withdrawn');
  });

  it('offers no registration once registration has closed', () => {
    load(detail({ status: 'RegistrationClosed', acceptsRegistrations: false, myRegistrationStatus: null }));

    const buttons = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).map((b) => b.textContent?.trim());

    expect(buttons).not.toContain('Register');
  });

  // ---- Rounds and entries -----------------------------------------------------

  it('loads a round only once it is selected, not on page load', () => {
    load(detail());

    // http.verify() in afterEach is the assertion: nothing for /rounds/round1
    // has gone out yet.
    expect(component.selectedRoundId()).toBeNull();
  });

  it('never shows a mark to a caller the round says may not see one', () => {
    load(detail());

    component.selectRound(component.detail()!.rounds[0]!);

    http
      .expectOne('/v1/competitions/comp1/rounds/round1')
      .flush(roundDetail({ canSeeScores: false, entries: [entry({ averageScore: null })] }));
    fixture.detectChanges();

    expect(text()).toContain('Marks are visible only to the organiser');
    expect((fixture.nativeElement as HTMLElement).querySelector('.stat')).toBeNull();
  });

  it('shows the average to the organiser or an assigned judge', () => {
    load(detail({ isJudge: true }));

    component.selectRound(component.detail()!.rounds[0]!);

    http
      .expectOne('/v1/competitions/comp1/rounds/round1')
      .flush(roundDetail({ canSeeScores: true, entries: [entry({ averageScore: 82 })] }));
    fixture.detectChanges();

    expect(text()).toContain('82');
  });

  // ---- Scoring, as a judge ----------------------------------------------------

  it('records a mark and reloads the round', () => {
    load(detail({ isJudge: true }));

    component.selectRound(component.detail()!.rounds[0]!);

    http
      .expectOne('/v1/competitions/comp1/rounds/round1')
      .flush(roundDetail({ canSeeScores: true, entries: [entry({ id: 'e1', myScore: null })] }));
    fixture.detectChanges();

    component.scoreDrafts['e1'] = 88;
    component.recordScore({ ...entry({ id: 'e1' }) });

    const request = http.expectOne('/v1/competitions/comp1/entries/e1/scores');
    expect(request.request.body).toEqual({ value: 88, comments: null });

    request.flush({ entryId: 'e1', judgeMemberId: ME, value: 88, comments: null, updatedAt: SOON });

    http
      .expectOne('/v1/competitions/comp1/rounds/round1')
      .flush(roundDetail({ canSeeScores: true, entries: [entry({ id: 'e1', myScore: 88 })] }));
    fixture.detectChanges();

    expect(text()).toContain('Your mark: 88');
  });

  it('correcting a mark is an upsert, not a refused second write', () => {
    // Unlike celebrity-voting's one-shot vote, a second call here updates the
    // same row rather than being refused.
    load(detail({ isJudge: true }));

    component.selectRound(component.detail()!.rounds[0]!);

    http
      .expectOne('/v1/competitions/comp1/rounds/round1')
      .flush(roundDetail({ canSeeScores: true, entries: [entry({ id: 'e1', myScore: 60 })] }));
    fixture.detectChanges();

    component.scoreDrafts['e1'] = 95;
    component.recordScore({ ...entry({ id: 'e1' }) });

    const request = http.expectOne('/v1/competitions/comp1/entries/e1/scores');
    request.flush({ entryId: 'e1', judgeMemberId: ME, value: 95, comments: null, updatedAt: SOON });

    http
      .expectOne('/v1/competitions/comp1/rounds/round1')
      .flush(roundDetail({ canSeeScores: true, entries: [entry({ id: 'e1', myScore: 95 })] }));
    fixture.detectChanges();

    expect(text()).toContain('Your mark: 95');
  });

  it('keeps a refused score off the page-level error', () => {
    load(detail({ isJudge: true }));

    component.selectRound(component.detail()!.rounds[0]!);

    http
      .expectOne('/v1/competitions/comp1/rounds/round1')
      .flush(roundDetail({ canSeeScores: true, entries: [entry({ id: 'e1' })] }));
    fixture.detectChanges();

    component.scoreDrafts['e1'] = 50;
    component.recordScore({ ...entry({ id: 'e1' }) });

    http
      .expectOne('/v1/competitions/comp1/entries/e1/scores')
      .flush({ title: 'Round.Closed' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(component.scoreError()['e1']).toBeDefined();
  });

  // ---- The published result ---------------------------------------------------

  it('asks for the result only once the round says it has one', () => {
    load(detail());

    component.selectRound(component.detail()!.rounds[0]!);

    http
      .expectOne('/v1/competitions/comp1/rounds/round1')
      .flush(roundDetail({ round: { ...roundDetail().round, hasPublishedResult: false } }));
    fixture.detectChanges();

    // http.verify() in afterEach is the assertion: no /results call went out.
    expect(component.result()).toBeNull();
  });

  it('shows the frozen ranking, and says it is locked', () => {
    load(detail());

    component.selectRound(component.detail()!.rounds[0]!);

    const published = roundDetail({
      round: { ...roundDetail().round, hasPublishedResult: true },
      canSeeScores: false,
    });

    http.expectOne('/v1/competitions/comp1/rounds/round1').flush(published);

    const result: CompetitionResult = {
      roundId: 'round1',
      ranking: [{ rank: 1, entryId: 'entry1', participantMemberId: 'm1', averageScore: 91 }],
      publishedBy: 'admin',
      publishedAt: new Date().toISOString(),
    };

    http.expectOne('/v1/competitions/comp1/rounds/round1/results').flush(result);
    fixture.detectChanges();

    expect(text()).toContain('locked after publication');
    expect(text()).toContain('91');
  });

  // ---- Names it cannot resolve -------------------------------------------------

  it('names the reader "You", a resolved member by name, and anybody else "A member"', () => {
    load({ ...detail(), registeredMemberIds: ['m2', 'hidden'] });
    showTab('Participants');
    settlePeople();

    expect(component.nameFor(ME)).toBe('You');
    expect(component.nameFor('m2')).toBe('Member m2');
    expect(component.nameFor('hidden')).toBe('A member');
    expect(component.nameFor('never-asked')).toBe('A member');
  });
});
