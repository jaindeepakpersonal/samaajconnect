import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { ElectionComponent } from './election.component';
import { PanchComponent } from './panch.component';
import { Candidate, Election, ElectionDetail, ElectionResult, Panel } from './panch.models';

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

const NAMES: Record<string, string> = { m1: 'Diya Jain', m2: 'Aarav Jain', m3: 'Meera Jain' };

function election(overrides: Partial<Election> = {}): Election {
  return {
    id: 'e1',
    title: 'Samaaj Panch 2026',
    description: null,
    nominationStartAt: SOON,
    nominationEndAt: SOON,
    votingStartAt: SOON,
    votingEndAt: SOON,
    seats: 2,
    resultsVisibility: 'Live',
    status: 'VotingOpen',
    acceptsNominations: false,
    acceptsVotes: true,
    myChoices: null,
    candidateCount: 3,
    createdAt: SOON,
    ...overrides,
  };
}

function candidate(overrides: Partial<Candidate> = {}): Candidate {
  return { id: 'c1', memberId: 'm1', status: 'Approved', nominatedBy: 'm9', votes: 3, ...overrides };
}

function detail(
  electionOverrides: Partial<Election> = {},
  candidates: Candidate[] = [candidate()],
  tallyVisible = true,
): ElectionDetail {
  return { election: election(electionOverrides), candidates, tallyVisible };
}

function providers() {
  return [
    provideRouter([]),
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
  ];
}

/** Answers every directory lookup the screen made for a name. */
function answerNames(http: HttpTestingController): void {
  for (const request of http.match((r) => r.url.startsWith('/v1/members/'))) {
    const id = request.request.url.split('/').pop() ?? '';
    const name = NAMES[id];

    if (name === undefined) {
      request.flush({}, { status: 404, statusText: 'Not Found' });
    } else {
      request.flush({ id, fullName: name });
    }
  }
}

describe('PanchComponent', () => {
  let fixture: ComponentFixture<PanchComponent>;
  let component: PanchComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [PanchComponent], providers: providers() });

    fixture = TestBed.createComponent(PanchComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  function panel(memberIds: string[], viewerIsMember = false): Panel {
    return {
      members: memberIds.map((memberId) => ({
        memberId,
        electionId: 'e1',
        seatedAt: '2026-09-21T00:00:00Z',
      })),
      seats: 10,
      viewerIsMember,
    };
  }

  function load(seated: Panel, elections: Election[] = []): void {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne('/v1/panch/panel').flush(seated);
    http.expectOne('/v1/panch/elections').flush(elections);
    answerNames(http);
    fixture.detectChanges();
  }

  it('says what happens while nobody is seated', () => {
    load(panel([]));

    expect(text()).toContain('No Panch is seated yet');
    expect(text()).toContain('administrators');
  });

  it('names the seated members and counts the seats', () => {
    load(panel(['m1', 'm2']));

    expect(text()).toContain('Diya Jain');
    expect(text()).toContain('Aarav Jain');
    expect(text()).toContain('2 of 10 seats filled');
  });

  it('shows the reader as "You" without a directory call', () => {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne('/v1/panch/panel').flush(panel([ME], true));
    http.expectOne('/v1/panch/elections').flush([]);
    http.expectNone(`/v1/members/${ME}`);
    fixture.detectChanges();

    expect(text()).toContain('You');
    expect(text()).toContain('You sit on the Panch');
  });

  it('says "A member" rather than printing an id it could not resolve', () => {
    load(panel(['stranger']));

    expect(text()).toContain('A member');
    expect(text()).not.toContain('stranger');
  });

  it('says nothing has been held when there are no elections', () => {
    load(panel([]));

    expect(text()).toContain('has not held a Panch election yet');
  });

  it('offers the ballot on an election open for voting the member has not voted in', () => {
    load(panel([]), [election()]);

    expect(component.action(election())).toBe('Cast your ballot');
    expect(text()).toContain('Voting open');
  });

  it('offers nominating while nominations are open, and shows that a member has voted', () => {
    expect(
      component.action(election({ acceptsVotes: false, acceptsNominations: true })),
    ).toBe('Put somebody forward');

    load(panel([]), [election({ myChoices: ['c1'] })]);

    expect(text()).toContain('You have voted');
    expect(component.action(election({ myChoices: ['c1'] }))).toBe('View');
  });

  it('reads the flags, not the status, for whether an election is open', () => {
    // A status left on VotingOpen after the window closed must not offer a ballot.
    const stale = election({ status: 'VotingOpen', acceptsVotes: false });

    expect(component.stage(stale)).toBe('Voting closed');
    expect(component.action(stale)).toBe('View');
  });

  it('shows a load failure with a way to retry', () => {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne('/v1/panch/panel').flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(text()).toContain('Try again');
  });
});

describe('ElectionComponent', () => {
  let fixture: ComponentFixture<ElectionComponent>;
  let component: ElectionComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ElectionComponent],
      providers: [
        ...providers(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'e1' } } } },
      ],
    });

    fixture = TestBed.createComponent(ElectionComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  function buttonSaying(label: string): HTMLButtonElement | undefined {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) =>
      b.textContent?.trim().startsWith(label),
    ) as HTMLButtonElement | undefined;
  }

  function load(found: ElectionDetail, result: ElectionResult | null = null): void {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne('/v1/panch/elections/e1').flush(found);

    if (found.election.status === 'Published') {
      const request = http.expectOne('/v1/panch/elections/e1/results');

      if (result === null) {
        request.flush({}, { status: 404, statusText: 'Not Found' });
      } else {
        request.flush(result);
      }
    }

    answerNames(http);
    fixture.detectChanges();
  }

  it('shows only approved candidates, not every nomination', () => {
    load(
      detail({}, [
        candidate({ id: 'on', status: 'Approved' }),
        candidate({ id: 'waiting', status: 'Nominated' }),
      ]),
    );

    expect(component.ballot(component.detail()!).map((c) => c.id)).toEqual(['on']);
  });

  it('never prints zero for a count it is not allowed to see', () => {
    load(detail({}, [candidate({ votes: null })], false));

    expect(text()).toContain('Vote counts are hidden until voting closes');
    expect(text()).not.toContain('0 votes');
  });

  it('stops ticking at the seat count, but always lets a tick be removed', () => {
    load(
      detail({ seats: 2 }, [
        candidate({ id: 'a', memberId: 'm1' }),
        candidate({ id: 'b', memberId: 'm2' }),
        candidate({ id: 'c', memberId: 'm3' }),
      ]),
    );

    const [a, b, c] = component.detail()!.candidates as [Candidate, Candidate, Candidate];

    component.toggle(a);
    component.toggle(b);
    component.toggle(c);

    expect(component.picked()).toEqual(['a', 'b']);

    component.toggle(a);

    expect(component.picked()).toEqual(['b']);
  });

  it('does not offer a tick box against the reader’s own name', () => {
    load(detail({}, [candidate({ id: 'mine', memberId: ME }), candidate({ id: 'theirs' })]));

    expect(text()).toContain('You cannot back yourself');
    expect(fixture.nativeElement.querySelectorAll('input[type="checkbox"]').length).toBe(1);
  });

  it('sends the ticked candidates as one ballot, and reloads', () => {
    load(detail({}, [candidate({ id: 'a' }), candidate({ id: 'b', memberId: 'm2' })]));

    component.toggle(component.detail()!.candidates[1]!);
    fixture.detectChanges();

    buttonSaying('Cast my ballot')!.click();

    const request = http.expectOne('/v1/panch/elections/e1/ballots');

    expect(request.request.body).toEqual({ candidateIds: ['b'] });
    request.flush({ electionId: 'e1', choices: ['b'], accepted: true });

    http
      .expectOne('/v1/panch/elections/e1')
      .flush(detail({ myChoices: ['b'] }, [candidate({ id: 'a' }), candidate({ id: 'b', memberId: 'm2' })]));
    answerNames(http);
    fixture.detectChanges();

    expect(text()).toContain('You have cast your ballot');
    expect(component.picked()).toEqual([]);
  });

  it('will not cast an empty ballot', () => {
    load(detail());

    expect(buttonSaying('Cast my ballot')!.disabled).toBe(true);
    component.cast(component.detail()!.election);
    http.expectNone('/v1/panch/elections/e1/ballots');
  });

  it('marks the ballot the member cast and offers no further ticks', () => {
    load(detail({ myChoices: ['c1'] }));

    expect(text()).toContain('Your ballot');
    expect(fixture.nativeElement.querySelectorAll('input[type="checkbox"]').length).toBe(0);
    expect(buttonSaying('Cast my ballot')).toBeUndefined();
  });

  it('offers no ballot when the election is not open for voting right now', () => {
    load(detail({ status: 'VotingOpen', acceptsVotes: false }));

    expect(fixture.nativeElement.querySelectorAll('input[type="checkbox"]').length).toBe(0);
    expect(text()).toContain('Nothing is open on this election right now');
  });

  it('shows the nomination form only while nominations are open', () => {
    load(detail({ acceptsVotes: false, acceptsNominations: true, status: 'NominationsOpen' }));

    expect(text()).toContain('Put somebody forward');
    expect(text()).toContain('An administrator decides whether a nomination reaches the ballot');
  });

  it('nominates only somebody picked from the search, never a typed id', () => {
    load(detail({ acceptsVotes: false, acceptsNominations: true, status: 'NominationsOpen' }, []));

    component.nominate(component.detail()!.election);
    http.expectNone('/v1/panch/elections/e1/candidates');

    component.selectNominee({ id: 'm2', fullName: 'Aarav Jain' } as never);
    component.nominate(component.detail()!.election);

    const request = http.expectOne('/v1/panch/elections/e1/candidates');

    expect(request.request.body).toEqual({ memberId: 'm2' });
    request.flush({ electionId: 'e1', candidateId: 'c9', memberId: 'm2', nominated: true });

    http
      .expectOne('/v1/panch/elections/e1')
      .flush(detail({ acceptsVotes: false, acceptsNominations: true }, []));
    fixture.detectChanges();

    expect(text()).toContain('Aarav Jain put forward. An administrator decides');
  });

  it('clears the last outcome as soon as the next name is typed', () => {
    load(detail({ acceptsVotes: false, acceptsNominations: true, status: 'NominationsOpen' }, []));

    component.selectNominee({ id: 'm2', fullName: 'Aarav Jain' } as never);
    component.nominate(component.detail()!.election);
    http
      .expectOne('/v1/panch/elections/e1/candidates')
      .flush({ electionId: 'e1', candidateId: 'c9', memberId: 'm2', nominated: true });
    http
      .expectOne('/v1/panch/elections/e1')
      .flush(detail({ acceptsVotes: false, acceptsNominations: true }, []));

    component.onNomineeQueryChange('A');
    fixture.detectChanges();

    expect(component.nominateMessage()).toBeNull();
    expect(text()).not.toContain('put forward. An administrator decides');
  });

  it('says a missing election is gone, without offering a retry that cannot help', () => {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne('/v1/panch/elections/e1').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('There is no such election in our Samaaj');
    expect(text()).not.toContain('Try again');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('says a repeat nomination is not a failure', () => {
    load(detail({ acceptsVotes: false, acceptsNominations: true, status: 'NominationsOpen' }, []));

    component.selectNominee({ id: 'm2', fullName: 'Aarav Jain' } as never);
    component.nominate(component.detail()!.election);
    http
      .expectOne('/v1/panch/elections/e1/candidates')
      .flush({ electionId: 'e1', candidateId: 'c9', memberId: 'm2', nominated: false });
    http
      .expectOne('/v1/panch/elections/e1')
      .flush(detail({ acceptsVotes: false, acceptsNominations: true }, []));
    fixture.detectChanges();

    expect(text()).toContain('Aarav Jain had already been put forward');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('reads the frozen ranking once published, and marks who was seated', () => {
    load(detail({ status: 'Published', acceptsVotes: false }, [candidate({ memberId: 'm1' })]), {
      electionId: 'e1',
      ranking: [
        { rank: 1, candidateId: 'c1', memberId: 'm1', votes: 5 },
        { rank: 2, candidateId: 'c2', memberId: 'm2', votes: 1 },
      ],
      seatedMemberIds: ['m1'],
      publishedBy: 'admin',
      publishedAt: '2026-09-21T10:00:00Z',
    });

    expect(text()).toContain('Result');
    expect(text()).toContain('Seated');
    expect(text()).toContain('locked after publication');
  });

  it('does not ask for a result before there is one', () => {
    load(detail({ status: 'Closed', acceptsVotes: false }));

    http.expectNone('/v1/panch/elections/e1/results');
  });

  it('shows a refusal from the service and keeps the ballot', () => {
    load(detail());
    component.toggle(component.detail()!.candidates[0]!);

    component.cast(component.detail()!.election);
    http
      .expectOne('/v1/panch/elections/e1/ballots')
      .flush({ title: 'Voting is not open.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    expect(component.picked().length).toBe(1);
  });
});
