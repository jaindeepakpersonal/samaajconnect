import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { CampaignDetailComponent } from './campaign-detail.component';
import { CampaignListComponent } from './campaign-list.component';
import { Campaign, CampaignDetail, CampaignResult, Candidate } from '../../core/admin.models';

const CAMPAIGN_ID = 'c1';

function campaign(overrides: Partial<Campaign> = {}): Campaign {
  return {
    id: CAMPAIGN_ID,
    title: '2026 Samaaj Celebrity',
    description: null,
    nominationStartAt: '2026-08-01T00:00:00Z',
    nominationEndAt: '2026-08-20T00:00:00Z',
    votingStartAt: '2026-09-01T00:00:00Z',
    votingEndAt: '2026-09-20T00:00:00Z',
    topN: 10,
    resultsVisibility: 'HiddenUntilClose',
    status: 'Draft',
    acceptsNominations: false,
    acceptsVotes: false,
    myVoteCandidateId: null,
    candidateCount: 0,
    createdAt: '2026-07-01T00:00:00Z',
    ...overrides,
  };
}

function candidate(overrides: Partial<Candidate> = {}): Candidate {
  return {
    id: 'cand1',
    memberId: 'm1',
    category: null,
    status: 'Nominated',
    nominatedBy: 'm2',
    votes: null,
    ...overrides,
  };
}

function detail(overrides: Partial<CampaignDetail> = {}): CampaignDetail {
  return {
    campaign: campaign(),
    candidates: [],
    tallyVisible: true,
    ...overrides,
  };
}

const MEMBERS = [
  { id: 'm1', fullName: 'Diya Jain' },
  { id: 'm2', fullName: 'Aarav Jain' },
  { id: 'm3', fullName: 'Meera Jain' },
];

describe('CampaignListComponent', () => {
  let fixture: ComponentFixture<CampaignListComponent>;
  let component: CampaignListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CampaignListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(CampaignListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(campaigns: Campaign[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/celebrity-voting/campaigns').flush(campaigns);
    fixture.detectChanges();
  }

  it('reads a 404 as the module being off', () => {
    fixture.detectChanges();
    http
      .expectOne('/v1/celebrity-voting/campaigns')
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('does not run the celebrity voting module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('refuses a voting window that starts before nominations close', () => {
    // The rule the service enforces, duplicated here on purpose: members who
    // vote early have to see the same ballot as members who vote late. A form
    // that let somebody build the overlap and discovered it on submit would be
    // offering a shape the platform does not have.
    load();

    component.title = '2026 Samaaj Celebrity';
    component.nominationStartAt = '2026-08-01T00:00';
    component.nominationEndAt = '2026-08-20T00:00';
    component.votingStartAt = '2026-08-15T00:00';
    component.votingEndAt = '2026-09-20T00:00';

    expect(component.windowsOverlap()).toBe(true);
    expect(component.canCreate()).toBe(false);

    component.create();
    http.expectNone('/v1/celebrity-voting/campaigns');
  });

  it('accepts a voting window that starts after nominations close', () => {
    load();

    component.title = '2026 Samaaj Celebrity';
    component.nominationStartAt = '2026-08-01T00:00';
    component.nominationEndAt = '2026-08-20T00:00';
    component.votingStartAt = '2026-09-01T00:00';
    component.votingEndAt = '2026-09-20T00:00';
    component.topN = 10;

    expect(component.windowsOverlap()).toBe(false);

    component.create();

    const body = http.expectOne('/v1/celebrity-voting/campaigns').request
      .body as Record<string, unknown>;

    expect(body['title']).toBe('2026 Samaaj Celebrity');
    expect(body['topN']).toBe(10);
    expect(body['resultsVisibility']).toBe('HiddenUntilClose');
    expect(body['description']).toBeNull();
  });

  it('says nothing about overlap until both dates are filled in', () => {
    // An empty form is not an invalid one, and telling somebody their windows
    // overlap before they have typed either is noise.
    load();

    component.nominationEndAt = '2026-08-20T00:00';
    component.votingStartAt = '';

    expect(component.windowsOverlap()).toBe(false);
    expect(text()).not.toContain('Voting cannot start before');
  });

  it('defaults to hiding the running count', () => {
    // Members who can see who is winning vote differently. Hidden is the
    // conservative default, and the form makes it a decision either way.
    expect(component.resultsVisibility).toBe('HiddenUntilClose');
  });

  it('says which campaigns are actually open, not just what their status says', () => {
    load([campaign({ status: 'VotingOpen', acceptsVotes: true })]);

    expect(text()).toContain('Open now');
    expect(text()).toContain('the clock agrees');
  });
});

describe('CampaignDetailComponent', () => {
  let fixture: ComponentFixture<CampaignDetailComponent>;
  let component: CampaignDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CampaignDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => CAMPAIGN_ID } } },
        },
      ],
    });

    fixture = TestBed.createComponent(CampaignDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(subject: CampaignDetail = detail(), ranking?: CampaignResult) {
    fixture.detectChanges();
    http.expectOne(`/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}`).flush(subject);
    http.expectOne('/v1/members?limit=100').flush(MEMBERS);

    if (subject.campaign.status === 'Published') {
      const call = http.expectOne(`/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}/results`);

      if (ranking) {
        call.flush(ranking);
      } else {
        call.flush({}, { status: 404, statusText: 'Not Found' });
      }
    }

    fixture.detectChanges();
  }

  it('offers the one stage the campaign can move to', () => {
    // Strictly forward, and the sequence has no branches. Four buttons with
    // three refusals would be offering choices that were never there.
    load(detail({ campaign: campaign({ status: 'Draft' }) }));
    expect(component.nextStatus()).toBe('NominationsOpen');

    load2(detail({ campaign: campaign({ status: 'NominationsOpen' }) }));
    expect(component.nextStatus()).toBe('VotingOpen');
  });

  it('will not open voting on an empty ballot', () => {
    // The service refuses it, and a campaign that reached its voting window
    // with nobody to vote for is the state this whole screen exists to prevent.
    load(detail({ campaign: campaign({ status: 'NominationsOpen' }), candidates: [] }));

    expect(text()).toContain('Voting cannot open on an empty ballot');

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;

    expect(button.disabled).toBe(true);
  });

  it('opens voting once somebody is on the ballot', () => {
    load(
      detail({
        campaign: campaign({ status: 'NominationsOpen' }),
        candidates: [candidate({ status: 'Approved' })],
      }),
    );

    component.move('VotingOpen');

    const call = http.expectOne(`/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}/status`);

    expect(call.request.body).toEqual({ status: 'VotingOpen' });

    call.flush({});
    reload();
  });

  it('splits nominations waiting for a decision from the ballot', () => {
    load(
      detail({
        campaign: campaign({ status: 'NominationsOpen' }),
        candidates: [
          candidate({ id: 'cand1', memberId: 'm1', status: 'Nominated' }),
          candidate({ id: 'cand2', memberId: 'm3', status: 'Approved' }),
        ],
      }),
    );

    expect(component.nominated().map((c) => c.id)).toEqual(['cand1']);
    expect(component.approved().map((c) => c.id)).toEqual(['cand2']);
  });

  it('approves a nomination onto the ballot', () => {
    load(
      detail({
        campaign: campaign({ status: 'NominationsOpen' }),
        candidates: [candidate()],
      }),
    );

    component.decide(candidate(), true);

    const call = http.expectOne(
      `/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}/candidates/cand1/decide`,
    );

    // `approve` is always sent explicitly: a decision endpoint whose safest
    // value is implicit is one where a mistyped request puts somebody on a
    // ballot.
    expect(call.request.body).toEqual({ approve: true });

    call.flush({});
    reload();
  });

  it('stops offering removal once voting has opened', () => {
    // Removing a candidate then would discard the votes already cast for them.
    // The service refuses it; the screen must not offer a button that always
    // answers 409.
    load(
      detail({
        campaign: campaign({ status: 'VotingOpen' }),
        candidates: [candidate({ status: 'Approved', votes: 12 })],
      }),
    );

    expect(component.canRemove()).toBe(false);
    expect(text()).toContain('Voting has opened');
    expect(text()).not.toContain('Take off the ballot');
  });

  it('still offers removal while nominations are open', () => {
    load(
      detail({
        campaign: campaign({ status: 'NominationsOpen' }),
        candidates: [candidate({ status: 'Approved' })],
      }),
    );

    expect(component.canRemove()).toBe(true);
    expect(text()).toContain('Take off the ballot');
  });

  it('shows an administrator the tally even when members cannot see it', () => {
    load(
      detail({
        campaign: campaign({ status: 'VotingOpen', resultsVisibility: 'HiddenUntilClose' }),
        candidates: [
          candidate({ id: 'cand1', memberId: 'm1', status: 'Approved', votes: 12 }),
          candidate({ id: 'cand2', memberId: 'm3', status: 'Approved', votes: 7 }),
        ],
        tallyVisible: true,
      }),
    );

    expect(component.totalVotes()).toBe(19);
    expect(text()).toContain('Members cannot see these counts');
  });

  it('distinguishes a count of zero from a count it may not see', () => {
    // Null rather than zero, because zero is a claim and the wrong one.
    load(
      detail({
        campaign: campaign({ status: 'VotingOpen' }),
        candidates: [candidate({ status: 'Approved', votes: null })],
        tallyVisible: false,
      }),
    );

    expect(text()).toContain('Not visible');
    expect(text()).not.toContain('0 votes');
  });

  it('will not publish on one click', () => {
    // A second publish would compute a second ranking, and two rankings leave
    // "the result" with no referent. Unlike a Boli result, this is not
    // idempotent, so the confirmation is not ceremony.
    load(detail({ campaign: campaign({ status: 'Closed' }) }));

    expect(text()).not.toContain('cannot be undone or recomputed');

    component.confirmingPublish.set(true);
    fixture.detectChanges();

    expect(text()).toContain('cannot be undone or recomputed');
    http.expectNone(`/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}/results`);
  });

  it('keeps the button that opened the confirmation, and announces the panel', () => {
    // Replacing the trigger destroys the focused element and drops a keyboard
    // user to the body (WCAG 2.4.3); disabling it does the same. The panel is a
    // live region so the warning is heard rather than only seen.
    load(detail({ campaign: campaign({ status: 'Closed' }) }));

    const trigger = () =>
      [...fixture.nativeElement.querySelectorAll('button')].find(
        (b) => (b as HTMLElement).textContent?.trim() === 'Publish the result',
      ) as HTMLButtonElement | undefined;

    expect(trigger()!.getAttribute('aria-expanded')).toBe('false');

    component.confirmingPublish.set(true);
    fixture.detectChanges();

    expect(trigger()).toBeTruthy();
    expect(trigger()!.disabled).toBe(false);
    expect(trigger()!.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelector('.notice[role="status"]')).toBeTruthy();
  });

  it('names the three tables it can draw', () => {
    load(
      detail({
        campaign: campaign({ status: 'NominationsOpen' }),
        candidates: [
          candidate({ id: 'cand1', status: 'Approved' }),
          candidate({ id: 'cand2', memberId: 'm3', status: 'Nominated' }),
        ],
      }),
    );

    const captions = [...fixture.nativeElement.querySelectorAll('table caption')].map((c) =>
      (c as HTMLElement).textContent?.trim(),
    );

    expect(captions).toEqual([
      'Candidates on the ballot',
      'Nominations waiting for a decision',
    ]);
  });

  it('publishes once confirmed', () => {
    load(detail({ campaign: campaign({ status: 'Closed' }) }));

    component.publish();

    const call = http.expectOne(`/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}/results`);

    expect(call.request.method).toBe('POST');

    call.flush({});
    reload();

    expect(text()).toContain('is now fixed');
  });

  it('reads the frozen ranking once a campaign is published', () => {
    load(
      detail({ campaign: campaign({ status: 'Published' }) }),
      {
        campaignId: CAMPAIGN_ID,
        ranking: [
          { rank: 1, candidateId: 'cand1', memberId: 'm1', votes: 12 },
          { rank: 2, candidateId: 'cand2', memberId: 'm3', votes: 7 },
        ],
        publishedBy: 'm2',
        publishedAt: '2026-09-21T10:00:00Z',
      },
    );

    expect(text()).toContain('Diya Jain');
    expect(text()).toContain('never recalculated');
  });

  it('does not ask for a result before there is one', () => {
    // A 404 before publication is the normal state, not an error, so the
    // request is not made at all rather than made and forgiven.
    load(detail({ campaign: campaign({ status: 'VotingOpen' }) }));

    http.expectNone(`/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}/results`);
  });

  it('says "A member" rather than printing an id it could not resolve', () => {
    fixture.detectChanges();
    http
      .expectOne(`/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}`)
      .flush(detail({ candidates: [candidate({ memberId: 'm9', nominatedBy: 'm9' })] }));
    http.expectOne('/v1/members?limit=100').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('A member');
    expect(text()).not.toContain('m9');
  });

  it('explains a 404 as no such campaign rather than as an error', () => {
    fixture.detectChanges();
    http
      .expectOne(`/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such campaign');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  /** A second load in the same test, for checking a state transition. */
  function load2(subject: CampaignDetail) {
    component.ngOnInit();
    http.expectOne(`/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}`).flush(subject);
    http.expectOne('/v1/members?limit=100').flush(MEMBERS);
    fixture.detectChanges();
  }

  /** Answers the re-read every action fires. */
  function reload() {
    http.expectOne(`/v1/celebrity-voting/campaigns/${CAMPAIGN_ID}`).flush(detail());
    http.expectOne('/v1/members?limit=100').flush(MEMBERS);
    fixture.detectChanges();
  }
});
