import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import {
  Election,
  ElectionCandidate,
  ElectionDetail,
  ElectionResult,
  Panel,
} from '../../core/admin.models';
import { ElectionDetailComponent } from './election-detail.component';
import { PanchComponent } from './panch.component';

const ELECTION_ID = 'e1';

function election(overrides: Partial<Election> = {}): Election {
  return {
    id: ELECTION_ID,
    title: 'Samaaj Panch 2026',
    description: null,
    nominationStartAt: '2026-08-01T00:00:00Z',
    nominationEndAt: '2026-08-20T00:00:00Z',
    votingStartAt: '2026-09-01T00:00:00Z',
    votingEndAt: '2026-09-20T00:00:00Z',
    seats: 10,
    resultsVisibility: 'HiddenUntilClose',
    status: 'Draft',
    acceptsNominations: false,
    acceptsVotes: false,
    myChoices: null,
    candidateCount: 0,
    createdAt: '2026-07-01T00:00:00Z',
    ...overrides,
  };
}

function candidate(overrides: Partial<ElectionCandidate> = {}): ElectionCandidate {
  return {
    id: 'cand1',
    memberId: 'm1',
    status: 'Nominated',
    nominatedBy: 'm2',
    votes: null,
    ...overrides,
  };
}

function detail(overrides: Partial<ElectionDetail> = {}): ElectionDetail {
  return { election: election(), candidates: [], tallyVisible: true, ...overrides };
}

function panel(memberIds: string[] = []): Panel {
  return {
    members: memberIds.map((memberId) => ({
      memberId,
      electionId: ELECTION_ID,
      seatedAt: '2026-09-21T00:00:00Z',
    })),
    seats: 10,
    viewerIsMember: false,
  };
}

const MEMBERS = [
  { id: 'm1', fullName: 'Diya Jain' },
  { id: 'm2', fullName: 'Aarav Jain' },
];

describe('PanchComponent', () => {
  let fixture: ComponentFixture<PanchComponent>;
  let component: PanchComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PanchComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(PanchComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(seated: Panel = panel(), elections: Election[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/panch/panel').flush(seated);
    http.expectOne('/v1/panch/elections').flush(elections);
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    fixture.detectChanges();
  }

  it('reads a 404 as the module being off', () => {
    fixture.detectChanges();
    http.expectOne('/v1/panch/panel').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('does not run the Samaaj Panch module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('says what happens while nobody is seated', () => {
    load();

    expect(text()).toContain('Nobody is seated');
    expect(text()).toContain('reviewed by administrators');
  });

  it('names the seated members from the directory and counts the seats', () => {
    load(panel(['m1', 'm2']));

    expect(text()).toContain('Diya Jain');
    expect(text()).toContain('Aarav Jain');
    expect(text()).toContain('2 of 10 seats filled');
  });

  it('says "A member" rather than printing an id it cannot resolve', () => {
    load(panel(['stranger']));

    expect(text()).toContain('A member');
    expect(text()).not.toContain('stranger');
  });

  it('removes a member only after a confirmation', () => {
    load(panel(['m1', 'm2']));

    component.confirmingRemoval.set('m1');
    fixture.detectChanges();

    expect(text()).toContain('Confirm removal');
    http.expectNone('/v1/panch/panel/m1');

    component.remove('m1');
    const request = http.expectOne('/v1/panch/panel/m1');

    expect(request.request.method).toBe('DELETE');
    request.flush(panel(['m2']));
    fixture.detectChanges();

    expect(text()).toContain('Diya Jain is no longer on the Panch');
    expect(text()).toContain('1 of 10 seats filled');
    expect(component.confirmingRemoval()).toBeNull();
  });

  it('refuses a voting window that starts before nominations close', () => {
    load();

    component.title = 'Samaaj Panch 2026';
    component.nominationStartAt = '2026-08-01T00:00';
    component.nominationEndAt = '2026-08-20T00:00';
    component.votingStartAt = '2026-08-15T00:00';
    component.votingEndAt = '2026-09-20T00:00';

    expect(component.windowsOverlap()).toBe(true);
    expect(component.canSubmit()).toBe(false);

    component.save();
    http.expectNone('/v1/panch/elections');
  });

  it('refuses a window that closes before it opens', () => {
    load();

    component.title = 'Samaaj Panch 2026';
    component.nominationStartAt = '2026-08-20T00:00';
    component.nominationEndAt = '2026-08-01T00:00';
    component.votingStartAt = '2026-09-01T00:00';
    component.votingEndAt = '2026-09-20T00:00';

    expect(component.canSubmit()).toBe(false);
  });

  it('creates an election as a draft and reloads', () => {
    load();

    component.title = '  Samaaj Panch 2026  ';
    component.nominationStartAt = '2026-08-01T00:00';
    component.nominationEndAt = '2026-08-20T00:00';
    component.votingStartAt = '2026-08-20T00:00';
    component.votingEndAt = '2026-09-20T00:00';

    expect(component.canSubmit()).toBe(true);

    component.save();
    const request = http.expectOne('/v1/panch/elections');

    expect(request.request.method).toBe('POST');
    expect(request.request.body.title).toBe('Samaaj Panch 2026');
    expect(request.request.body.description).toBeNull();
    expect(request.request.body.resultsVisibility).toBe('HiddenUntilClose');
    request.flush(election());

    http.expectOne('/v1/panch/panel').flush(panel());
    http.expectOne('/v1/panch/elections').flush([election()]);
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    fixture.detectChanges();

    expect(text()).toContain('created as a draft');
    expect(text()).toContain('Samaaj Panch 2026');
  });

  function reload(elections: Election[]) {
    http.expectOne('/v1/panch/panel').flush(panel());
    http.expectOne('/v1/panch/elections').flush(elections);
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    fixture.detectChanges();
  }

  const buttons = () =>
    Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>).map(
      (b) => b.textContent?.replace(/\s+/g, ' ').trim() ?? '',
    );

  it('offers Edit and Delete until voting opens, Extend voting while it is open, and nothing after', () => {
    load(panel(), [
      election({ id: 'd', title: 'Draft one', status: 'Draft' }),
      election({ id: 'n', title: 'Nominating', status: 'NominationsOpen' }),
      election({ id: 'v', title: 'Voting', status: 'VotingOpen' }),
      election({ id: 'c', title: 'Closed one', status: 'Closed' }),
    ]);

    const labels = buttons();

    expect(labels).toContain('Edit Draft one');
    expect(labels).toContain('Delete Nominating');
    expect(labels).toContain('Extend voting for Voting');
    expect(labels).not.toContain('Edit Voting');
    expect(labels).not.toContain('Delete Voting');
    expect(labels.some((l) => l.includes('Closed one'))).toBe(false);
  });

  it('edits an election with its own values filled in, and saves the whole of it', () => {
    load(panel(), [election({ status: 'NominationsOpen', description: 'First call' })]);

    component.openEdit(new MouseEvent('click'), election({ status: 'NominationsOpen', description: 'First call' }));
    fixture.detectChanges();

    expect(text()).toContain('Edit Samaaj Panch 2026');
    expect(component.title).toBe('Samaaj Panch 2026');
    expect(component.description).toBe('First call');
    expect(component.nominationEndAt).not.toBe('');

    // Nominations closed too early: push their close out, and voting with it.
    component.nominationEndAt = '2026-08-30T00:00';
    component.votingStartAt = '2026-09-01T00:00';
    component.save();

    const request = http.expectOne(`/v1/panch/elections/${ELECTION_ID}`);
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body.title).toBe('Samaaj Panch 2026');
    expect(request.request.body.nominationEndAt).toBe(new Date('2026-08-30T00:00').toISOString());
    request.flush(election({ status: 'NominationsOpen' }));
    reload([election({ status: 'NominationsOpen' })]);

    expect(text()).toContain('Samaaj Panch 2026 saved.');
    http.expectNone('/v1/panch/elections');
  });

  it('deletes only after saying what goes with it', () => {
    load(panel(), [election({ status: 'NominationsOpen', candidateCount: 3 })]);

    component.confirmingDeletion.set(ELECTION_ID);
    fixture.detectChanges();

    expect(text()).toContain('and its 3 nominations');
    expect(text()).toContain('cannot be undone');
    http.expectNone(`/v1/panch/elections/${ELECTION_ID}`);

    component.deleteElection(election({ status: 'NominationsOpen', candidateCount: 3 }));
    const request = http.expectOne(`/v1/panch/elections/${ELECTION_ID}`);

    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    reload([]);

    expect(text()).toContain('Samaaj Panch 2026 deleted.');
    expect(component.confirmingDeletion()).toBeNull();
  });

  it('extends voting only to a later close', () => {
    const voting = election({ status: 'VotingOpen', votingEndAt: '2026-09-20T00:00:00Z' });
    load(panel(), [voting]);

    component.openExtend(new MouseEvent('click'), voting);
    fixture.detectChanges();

    expect(text()).toContain('can only move later');

    component.votingEndAt = '2026-09-10T00:00';
    fixture.detectChanges();

    expect(component.extendsLater()).toBe(false);
    expect(text()).toContain('Choose a time after the current close');

    component.extend();
    http.expectNone(`/v1/panch/elections/${ELECTION_ID}/extend-voting`);

    component.votingEndAt = '2026-09-27T00:00';
    expect(component.extendsLater()).toBe(true);

    component.extend();
    const request = http.expectOne(`/v1/panch/elections/${ELECTION_ID}/extend-voting`);

    expect(request.request.method).toBe('POST');
    expect(request.request.body.votingEndAt).toBe(new Date('2026-09-27T00:00').toISOString());
    request.flush({ ...voting, votingEndAt: '2026-09-27T00:00:00Z' });
    reload([{ ...voting, votingEndAt: '2026-09-27T00:00:00Z' }]);

    expect(text()).toContain('Voting for Samaaj Panch 2026 now closes');
  });

  it('says which elections are actually open, not just what their status says', () => {
    load(panel(), [election({ status: 'NominationsOpen', acceptsNominations: true })]);

    expect(text()).toContain('Open now');
  });
});

describe('ElectionDetailComponent', () => {
  let fixture: ComponentFixture<ElectionDetailComponent>;
  let component: ElectionDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ElectionDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => ELECTION_ID } } },
        },
      ],
    });

    fixture = TestBed.createComponent(ElectionDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(subject: ElectionDetail = detail(), ranking?: ElectionResult) {
    fixture.detectChanges();
    http.expectOne(`/v1/panch/elections/${ELECTION_ID}`).flush(subject);
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });

    if (subject.election.status === 'Published') {
      const call = http.expectOne(`/v1/panch/elections/${ELECTION_ID}/results`);

      if (ranking) {
        call.flush(ranking);
      } else {
        call.flush({}, { status: 404, statusText: 'Not Found' });
      }
    }

    fixture.detectChanges();
  }

  it('offers the one stage the election can move to', () => {
    load(detail({ election: election({ status: 'Draft' }) }));

    expect(component.nextStatus()).toBe('NominationsOpen');
    expect(text()).toContain('Open nominations');
  });

  it('will not open voting on an empty ballot', () => {
    load(detail({ election: election({ status: 'NominationsOpen' }), candidates: [] }));

    expect(text()).toContain('Voting cannot open on an empty ballot');

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;

    expect(button.disabled).toBe(true);
  });

  it('approves a nomination onto the ballot and reloads', () => {
    load(detail({ election: election({ status: 'NominationsOpen' }), candidates: [candidate()] }));

    expect(text()).toContain('Waiting for a decision');

    component.decide(candidate(), true);
    const request = http.expectOne(`/v1/panch/elections/${ELECTION_ID}/candidates/cand1/decide`);

    expect(request.request.body).toEqual({ approve: true });
    request.flush(detail());

    http.expectOne(`/v1/panch/elections/${ELECTION_ID}`).flush(
      detail({
        election: election({ status: 'NominationsOpen' }),
        candidates: [candidate({ status: 'Approved' })],
      }),
    );
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    fixture.detectChanges();

    expect(text()).toContain('Diya Jain is on the ballot');
    expect(component.approved().length).toBe(1);
  });

  it('stops offering removal once voting has opened', () => {
    load(
      detail({
        election: election({ status: 'VotingOpen' }),
        candidates: [candidate({ status: 'Approved', votes: 3 })],
      }),
    );

    expect(component.canRemove()).toBe(false);
    expect(text()).toContain('Voting has opened');
    expect(text()).not.toContain('Take off the ballot');
  });

  it('says voting has closed, not opened, once it has', () => {
    load(
      detail({
        election: election({ status: 'Closed' }),
        candidates: [candidate({ status: 'Approved', votes: 3 })],
      }),
    );

    expect(text()).toContain('Voting has closed');
    expect(text()).not.toContain('Voting has opened');
  });

  it('shows an administrator the tally even when members cannot see it', () => {
    load(
      detail({
        election: election({ status: 'VotingOpen', resultsVisibility: 'HiddenUntilClose' }),
        candidates: [candidate({ status: 'Approved', votes: 4 })],
      }),
    );

    // Votes, not voters: one ballot backs up to ten candidates.
    expect(text()).toContain('4 votes for these candidates');
    expect(text()).toContain('counts choices, not voters');
    expect(text()).toContain('Members cannot see these counts until voting closes');
  });

  it('distinguishes a count it may not see from a count of zero', () => {
    load(
      detail({
        election: election({ status: 'VotingOpen' }),
        candidates: [candidate({ status: 'Approved', votes: null })],
        tallyVisible: false,
      }),
    );

    expect(text()).toContain('Not visible');
  });

  it('will not publish on one click, and says the Panch is replaced', () => {
    load(detail({ election: election({ status: 'Closed' }) }));

    expect(text()).toContain('replacing whoever sits on it now');
    expect(text()).not.toContain('cannot be undone or recomputed');

    component.confirmingPublish.set(true);
    fixture.detectChanges();

    expect(text()).toContain('unseats the current Panch');
    http.expectNone(`/v1/panch/elections/${ELECTION_ID}/results`);
  });

  it('publishes once confirmed', () => {
    load(detail({ election: election({ status: 'Closed' }) }));

    component.publish();
    const request = http.expectOne(`/v1/panch/elections/${ELECTION_ID}/results`);

    expect(request.request.method).toBe('POST');
    request.flush({});

    http.expectOne(`/v1/panch/elections/${ELECTION_ID}`).flush(
      detail({ election: election({ status: 'Published' }) }),
    );
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    http.expectOne(`/v1/panch/elections/${ELECTION_ID}/results`).flush({});
    fixture.detectChanges();

    expect(text()).toContain('the Panch is seated');
  });

  it('reads the frozen ranking once published, and marks who was seated', () => {
    load(
      detail({ election: election({ status: 'Published' }) }),
      {
        electionId: ELECTION_ID,
        ranking: [
          { rank: 1, candidateId: 'cand1', memberId: 'm1', votes: 5 },
          { rank: 2, candidateId: 'cand2', memberId: 'm2', votes: 2 },
        ],
        seatedMemberIds: ['m1'],
        publishedBy: 'admin',
        publishedAt: '2026-09-21T10:00:00Z',
      },
    );

    expect(text()).toContain('The result');
    expect(text()).toContain('Seated');
    expect(text()).toContain('Diya Jain');
  });

  it('does not ask for a result before there is one', () => {
    load(detail({ election: election({ status: 'Closed' }) }));

    http.expectNone(`/v1/panch/elections/${ELECTION_ID}/results`);
  });

  it('explains a 404 as no such election rather than as an error', () => {
    fixture.detectChanges();
    http
      .expectOne(`/v1/panch/elections/${ELECTION_ID}`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such election');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
