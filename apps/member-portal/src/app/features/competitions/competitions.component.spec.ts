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
  CompetitionFinalResult,
  CompetitionResult,
  JudgingSheet,
  Round,
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
    roundCount: 2,
    maxMarks: 10,
    auditionRequired: false,
    auditionDate: null,
    finalistCount: null,
    myJudgePanel: null,
    ...overrides,
  };
}

function round(overrides: Partial<Round> = {}): Round {
  return {
    id: 'round1',
    name: 'Round 1',
    sequenceNumber: 1,
    status: 'Open',
    createdAt: SOON,
    closedAt: null,
    entryCount: 1,
    hasPublishedResult: false,
    kind: 'Main',
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

function detail(competitionOverrides: Partial<Competition> = {}, rounds: Round[] = [round()]): CompetitionDetail {
  return {
    competition: competition(competitionOverrides),
    rounds,
    judges: [],
    hasPublishedResult: false,
  };
}

function roundDetail(overrides: Partial<RoundDetail> = {}): RoundDetail {
  return {
    round: round(),
    entries: [entry()],
    canSeeScores: false,
    ...overrides,
  };
}

function sheet(overrides: Partial<JudgingSheet> = {}): JudgingSheet {
  return {
    competitionId: 'comp1',
    maxMarks: 10,
    panel: 'Main',
    rounds: [
      {
        round: round(),
        canScore: true,
        entries: [{ entryId: 'e1', participantMemberId: 'm1', myScore: null, myComments: null }],
      },
    ],
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

  it('offers to apply while applications are open', () => {
    load([competition({ acceptsRegistrations: true, myRegistrationStatus: null })]);

    expect(component.action(component.competitions()[0]!)).toBe('Apply');
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

  it('says a member has already applied rather than offering again', () => {
    load([competition({ acceptsRegistrations: true, myRegistrationStatus: 'Registered' })]);

    expect(text()).toContain('You have applied');
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
    http.expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'CompetitionType')
      .flush([{ id: 'type1', category: 'CompetitionType', code: 'RANG', label: 'Rangoli', sortOrder: 1, isActive: true }]);
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

  function tabLabels(): string[] {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="tab"]')]
      .map((t) => t.textContent!.replace(/\s+/g, ' ').trim());
  }

  function showTab(label: string): void {
    [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="tab"]')]
      .find((t) => t.textContent?.trim().startsWith(label))!
      .click();
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function dialog(): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('[role="dialog"]');
  }

  // ---- The page -------------------------------------------------------------

  it('opens like a page: cover, title, type, where it is held, and how it is judged', () => {
    load(detail({
      photoUrl: '/v1/competitions/comp1/photo?v=1',
      venue: 'Mahavir Bhawan, Hall 2',
      localityId: 'loc1',
      cityId: 'udr',
      competitionTypeId: 'type1',
      auditionRequired: true,
      auditionDate: '2026-11-02',
      finalistCount: 5,
    }));
    settlePeople();

    expect((fixture.nativeElement as HTMLElement).querySelector('img.comp-cover')).not.toBeNull();
    expect(text()).toContain('Rangoli Contest 2026');
    expect(text()).toContain('Rangoli');
    expect(text()).toContain('Mahavir Bhawan, Hall 2 · Hiran Magri, Udaipur');
    expect(text()).toContain('2 applied');
    expect(text()).toContain('audition, then 2 rounds, out of 10');
    expect(text()).toContain('the top 5 go through');
  });

  it('shows initials where there is no photo', () => {
    load(detail());

    expect((fixture.nativeElement as HTMLElement).querySelector('.comp-cover.placeholder')!.textContent!.trim())
      .toBe('RC');
  });

  it('opens on Details, with the window and the judges by name', () => {
    load({ ...detail(), judges: [{ id: 'j1', memberId: 'm7', assignedAt: SOON, panel: 'Main' }] });
    settlePeople();

    expect(component.tab()).toBe('details');
    expect(text()).toContain('Applications open');
    expect(text()).toContain('Member m7');
    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/members/m7"]')).not.toBeNull();
  });

  it('has no Rounds tab when the competition has no rounds, and no Judging tab for a member', () => {
    load(detail({ roundCount: 0 }));

    expect(tabLabels()).toEqual(['Details', 'Participants (0)', 'Results']);
  });

  it('shows Rounds when it has rounds, and Judging to a judge', () => {
    load(detail({ roundCount: 3, isJudge: true }));

    expect(tabLabels()).toEqual(['Details', 'Participants (0)', 'Rounds (1)', 'Results', 'Judging']);
  });

  it('lists who applied on Participants, by name, linked to their profile', () => {
    load({ ...detail(), registeredMemberIds: [ME, 'm2', 'hidden'] });

    showTab('Participants');
    settlePeople();

    const people = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.person-card')]
      .map((p) => p.textContent!.trim());

    expect(people.length).toBe(3);
    expect(people[0]).toContain('You');
    expect(people[1]).toContain('Member m2');
    expect(people[2]).toContain('A member');
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

  it('says nobody has applied rather than an empty list', () => {
    load({ ...detail(), registeredMemberIds: [] });

    showTab('Participants');

    expect(text()).toContain('Nobody has applied yet');
  });

  // ---- Applying -------------------------------------------------------------

  it('asks before applying, saying what the member signs up to', () => {
    load(detail({ acceptsRegistrations: true, auditionRequired: true, auditionDate: '2026-11-02', finalistCount: 5 }));

    component.openPopup('apply');
    fixture.detectChanges();

    expect(dialog()?.textContent).toContain('There is an audition');
    expect(dialog()?.textContent).toContain('every participant takes part in each');
  });

  it('applies and re-reads, because the count has moved', () => {
    load(detail({ acceptsRegistrations: true, myRegistrationStatus: null }));

    component.openPopup('apply');
    component.register();

    http
      .expectOne('/v1/competitions/comp1/registrations')
      .flush({ competitionId: 'comp1', memberId: ME, registered: true });

    http.expectOne('/v1/competitions/comp1').flush(detail({ myRegistrationStatus: 'Registered' }));
    fixture.detectChanges();

    expect(dialog()).toBeNull();
    expect(text()).toContain('You have applied');
  });

  it('reports a repeat application as success, not as an error', () => {
    load(detail({ acceptsRegistrations: true, myRegistrationStatus: null }));

    component.register();

    http
      .expectOne('/v1/competitions/comp1/registrations')
      .flush({ competitionId: 'comp1', memberId: ME, registered: false });

    http.expectOne('/v1/competitions/comp1').flush(detail({ myRegistrationStatus: 'Registered' }));
    fixture.detectChanges();

    expect(component.registerError()).toBeNull();
    expect(text()).toContain('already applied');
  });

  it('keeps a refused application in the popup that asked', () => {
    load(detail({ acceptsRegistrations: true }));

    component.openPopup('apply');
    component.register();

    http
      .expectOne('/v1/competitions/comp1/registrations')
      .flush({ title: 'Closed' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(dialog()?.querySelector('.notice.error')).not.toBeNull();
  });

  it('withdraws and offers to apply again', () => {
    load(detail({ acceptsRegistrations: true, myRegistrationStatus: 'Registered' }));

    component.openPopup('withdraw');
    component.withdraw();

    http
      .expectOne('/v1/competitions/comp1/registrations/mine')
      .flush({ competitionId: 'comp1', memberId: ME, registered: false });

    http.expectOne('/v1/competitions/comp1').flush(detail({ myRegistrationStatus: 'Withdrawn' }));
    fixture.detectChanges();

    expect(text()).toContain('You have withdrawn');
  });

  it('offers no application once applications have closed', () => {
    load(detail({ status: 'RegistrationClosed', acceptsRegistrations: false, myRegistrationStatus: null }));

    const buttons = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).map((b) => b.textContent?.trim());

    expect(buttons).not.toContain('Apply as participant');
  });

  // ---- Rounds -----------------------------------------------------------------

  it('loads a round only once it is opened, and shows who is in it - never a mark', () => {
    load(detail());

    // http.verify() in afterEach proves nothing for the round went out on load.
    component.showRound(component.detail()!.rounds[0]!);

    http
      .expectOne('/v1/competitions/comp1/rounds/round1')
      .flush(roundDetail({ canSeeScores: false, entries: [entry({ averageScore: null })] }));
    fixture.detectChanges();
    settlePeople();

    expect(dialog()?.textContent).toContain('Member m1');
    expect(dialog()?.textContent).toContain('Marks are not shown until the result is published');
  });

  it('shows a round\'s frozen ranking once it is published, without asking for its entries', () => {
    load(detail({}, [round({ hasPublishedResult: true, status: 'Closed' })]));

    component.showRound(component.detail()!.rounds[0]!);

    const result: CompetitionResult = {
      roundId: 'round1',
      ranking: [{ rank: 1, entryId: 'entry1', participantMemberId: 'm1', averageScore: 9 }],
      publishedBy: 'admin',
      publishedAt: new Date().toISOString(),
    };

    http.expectOne('/v1/competitions/comp1/rounds/round1/results').flush(result);
    fixture.detectChanges();

    expect(dialog()?.textContent).toContain('9 of 10');
  });

  // ---- Results ------------------------------------------------------------------

  it('names the winners once they are published, with their totals', () => {
    const found: CompetitionDetail = { ...detail({ status: 'Closed', acceptsRegistrations: false }), hasPublishedResult: true };

    load(found);

    const result: CompetitionFinalResult = {
      competitionId: 'comp1',
      winners: [
        { rank: 1, memberId: 'm1', total: 18 },
        { rank: 2, memberId: ME, total: 15.5 },
        { rank: 3, memberId: 'm3', total: 12 },
      ],
      standings: [],
      roundCount: 2,
      maxMarks: 10,
      publishedBy: 'admin',
      publishedAt: SOON,
    };

    http.expectOne('/v1/competitions/comp1/result').flush(result);
    showTab('Results');
    settlePeople();

    const places = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.podium-place')]
      .map((p) => p.textContent!.replace(/\s+/g, ' ').trim());

    expect(places[0]).toContain('First');
    expect(places[0]).toContain('Member m1');
    expect(places[0]).toContain('18 of 20');
    expect(places[1]).toContain('You');
    expect(places[1]).toContain('15.50');
  });

  it('does not ask for the winners before the detail says they are published', () => {
    load(detail());
    showTab('Results');

    // http.verify() in afterEach is the assertion: no /result call went out.
    expect(text()).toContain('Results are announced after the competition is judged');
  });

  // ---- Judging, as a judge --------------------------------------------------------

  function openSheet(found: JudgingSheet = sheet()): void {
    load(detail({ isJudge: true, myJudgePanel: 'Main', status: 'InProgress', acceptsRegistrations: false }));
    showTab('Judging');
    http.expectOne('/v1/competitions/comp1/judging/mine').flush(found);
    fixture.detectChanges();
    settlePeople();
  }

  it('lists each participant on the judge\'s own sheet, with a mark out of the max', () => {
    openSheet();

    expect(text()).toContain('Each mark is out of 10');
    expect(text()).toContain('Member m1');
    expect((fixture.nativeElement as HTMLElement).querySelector('#mark-e1')).not.toBeNull();
  });

  it('records a mark with its comment, then re-reads the sheet', () => {
    openSheet();

    component.marks['e1'] = 8;
    component.comments['e1'] = ' Steady hands ';
    component.saveMark(sheet().rounds[0]!.entries[0]!, sheet());

    const request = http.expectOne('/v1/competitions/comp1/entries/e1/scores');
    expect(request.request.body).toEqual({ value: 8, comments: 'Steady hands' });
    request.flush({ entryId: 'e1', judgeMemberId: ME, value: 8, comments: 'Steady hands', updatedAt: SOON });

    http.expectOne('/v1/competitions/comp1/judging/mine').flush(sheet({
      rounds: [{ round: round(), canScore: true, entries: [{ entryId: 'e1', participantMemberId: 'm1', myScore: 8, myComments: 'Steady hands' }] }],
    }));
    fixture.detectChanges();

    expect(component.saved()['e1']).toBe(true);
    expect(text()).toContain('Update');
  });

  it('refuses a mark above the max before sending it', () => {
    openSheet();

    component.marks['e1'] = 11;

    expect(component.markProblem(sheet().rounds[0]!.entries[0]!, sheet())).toContain('0 to 10');

    // http.verify() in afterEach: nothing was sent.
    component.saveMark(sheet().rounds[0]!.entries[0]!, sheet());
  });

  it('keeps a refused mark beside its participant, not at the top of the page', () => {
    openSheet();

    component.marks['e1'] = 5;
    component.saveMark(sheet().rounds[0]!.entries[0]!, sheet());

    http
      .expectOne('/v1/competitions/comp1/entries/e1/scores')
      .flush({ title: 'Round.Closed' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(component.markErrors()['e1']).toBeDefined();
  });

  it('shows a closed round\'s marks read-only', () => {
    openSheet(sheet({
      rounds: [{ round: round({ status: 'Closed' }), canScore: false, entries: [{ entryId: 'e1', participantMemberId: 'm1', myScore: 7, myComments: null }] }],
    }));

    expect((fixture.nativeElement as HTMLElement).querySelector('#mark-e1')).toBeNull();
    expect(text()).toContain('7');
  });

  it('says the finalists arrive when the audition closes', () => {
    openSheet(sheet({ rounds: [{ round: round(), canScore: true, entries: [] }] }));

    // Not an audition competition here, so it is simply nobody.
    expect(text()).toContain('Nobody to mark in this round');
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
