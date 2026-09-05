import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { CampaignDetailComponent } from './campaign-detail.component';
import { CampaignsListComponent } from './campaigns-list.component';
import { Campaign, CampaignDetail, CampaignResult, Candidate } from './voting.models';

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

function campaign(overrides: Partial<Campaign> = {}): Campaign {
  return {
    id: 'c1',
    title: 'Celebrities of Samaaj 2026',
    description: null,
    nominationStartAt: SOON,
    nominationEndAt: SOON,
    votingStartAt: SOON,
    votingEndAt: SOON,
    topN: 10,
    resultsVisibility: 'Live',
    status: 'VotingOpen',
    acceptsNominations: false,
    acceptsVotes: true,
    myVoteCandidateId: null,
    candidateCount: 2,
    createdAt: SOON,
    ...overrides,
  };
}

function candidate(overrides: Partial<Candidate> = {}): Candidate {
  return {
    id: 'cand1',
    memberId: 'm1',
    category: 'Community service',
    status: 'Approved',
    nominatedBy: 'm9',
    votes: 3,
    ...overrides,
  };
}

function detail(
  campaignOverrides: Partial<Campaign> = {},
  candidates: Candidate[] = [candidate()],
  tallyVisible = true,
): CampaignDetail {
  return { campaign: campaign(campaignOverrides), candidates, tallyVisible };
}

function providers() {
  return [
    provideRouter([]),
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
  ];
}

describe('CampaignsListComponent', () => {
  let fixture: ComponentFixture<CampaignsListComponent>;
  let component: CampaignsListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [CampaignsListComponent], providers: providers() });

    fixture = TestBed.createComponent(CampaignsListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(campaigns: Campaign[]): void {
    fixture.detectChanges();
    http.expectOne('/v1/celebrity-voting/campaigns').flush(campaigns);
    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('offers a vote while voting is open', () => {
    load([campaign({ acceptsVotes: true, myVoteCandidateId: null })]);

    expect(component.action(component.campaigns()[0]!)).toBe('Vote');
  });

  it('does not offer a vote on a campaign whose window has passed', () => {
    // The status still says VotingOpen; the clock does not. Only the server
    // knows the time it is deciding against, which is why the screen reads
    // acceptsVotes rather than the status.
    load([campaign({ status: 'VotingOpen', acceptsVotes: false })]);

    expect(component.action(component.campaigns()[0]!)).toBe('View');
    expect(component.describe(component.campaigns()[0]!)).toContain('Voting has closed');
  });

  it('does not label a campaign open when nothing on it is open', () => {
    // Shipped saying "Nominations open" in the pill directly above
    // "Nominations have closed" in the body, because the pill read the status
    // and every other line read the clock. The label follows the same two
    // flags as the rest of the card.
    load([
      campaign({ status: 'NominationsOpen', acceptsNominations: false, acceptsVotes: false }),
    ]);

    const only = component.campaigns()[0]!;

    expect(component.stage(only)).toBe('Nominations closed');
    expect(component.stage(only)).not.toBe('Nominations open');
    expect(text()).not.toContain('Nominations open');
  });

  it('labels a campaign open while it actually is', () => {
    load([campaign({ status: 'NominationsOpen', acceptsNominations: true, acceptsVotes: false })]);

    expect(component.stage(component.campaigns()[0]!)).toBe('Nominations open');
  });

  it('says a member has voted rather than offering again', () => {
    load([campaign({ acceptsVotes: true, myVoteCandidateId: 'cand1' })]);

    expect(component.action(component.campaigns()[0]!)).toBe('View the ballot');
    expect(text()).toContain('You have voted');
  });

  it('offers a nomination while nominations are open', () => {
    load([campaign({ status: 'NominationsOpen', acceptsNominations: true, acceptsVotes: false })]);

    expect(component.action(component.campaigns()[0]!)).toBe('Nominate');
    expect(component.describe(component.campaigns()[0]!)).toContain('Nominations close');
  });

  it('separates finished campaigns from live ones', () => {
    load([
      campaign({ id: 'live' }),
      campaign({ id: 'done', status: 'Published', acceptsVotes: false }),
    ]);

    expect(component.current().map((c) => c.id)).toEqual(['live']);
    expect(component.past().map((c) => c.id)).toEqual(['done']);
    expect(text()).toContain('Past campaigns');
  });

  it('uses the campaign top-N rather than the wireframe hardcoded ten', () => {
    load([campaign({ status: 'Closed', acceptsVotes: false, topN: 3 })]);

    expect(component.describe(component.campaigns()[0]!)).toContain('top 3');
  });

  it('says the Samaaj has run none rather than showing an empty grid', () => {
    load([]);

    expect(text()).toContain('has not run a Celebrities of Samaaj campaign');
  });

  it('offers a retry when the list cannot be loaded', () => {
    fixture.detectChanges();
    http
      .expectOne('/v1/celebrity-voting/campaigns')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
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
        ...providers(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'c1' } } } },
      ],
    });

    fixture = TestBed.createComponent(CampaignDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function load(found: CampaignDetail, result: CampaignResult | null = null): void {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne('/v1/celebrity-voting/campaigns/c1').flush(found);

    if (found.campaign.status === 'Published') {
      const request = http.expectOne('/v1/celebrity-voting/campaigns/c1/results');

      if (result === null) {
        request.flush({}, { status: 404, statusText: 'Not Found' });
      } else {
        request.flush(result);
      }
    }

    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function buttonSaying(label: string): HTMLButtonElement | undefined {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim().startsWith(label)) as HTMLButtonElement | undefined;
  }

  // ---- The ballot ---------------------------------------------------------

  it('shows only approved candidates, not every nomination', () => {
    load(
      detail({}, [
        candidate({ id: 'on', status: 'Approved' }),
        candidate({ id: 'waiting', status: 'Nominated' }),
      ]),
    );

    expect(component.ballot(component.detail()!).map((c) => c.id)).toEqual(['on']);
  });

  it('shows counts when the tally is visible', () => {
    load(detail({}, [candidate({ votes: 7 })], true));

    expect(text()).toContain('7');
  });

  it('never prints zero for a count it is not allowed to see', () => {
    // Null is not zero. Zero is a claim - that nobody voted for them - and it
    // would be the wrong one.
    load(detail({ resultsVisibility: 'HiddenUntilClose' }, [candidate({ votes: null })], false));

    expect(text()).toContain('hidden until voting closes');
    expect((fixture.nativeElement as HTMLElement).querySelector('.stat')).toBeNull();
  });

  // ---- Voting -------------------------------------------------------------

  it('casts the vote and re-reads, because the counts have moved', () => {
    load(detail({ acceptsVotes: true }));

    component.vote(component.detail()!.campaign, component.ballot(component.detail()!)[0]!);

    const request = http.expectOne('/v1/celebrity-voting/campaigns/c1/votes');

    expect(request.request.body).toEqual({ candidateId: 'cand1' });

    request.flush({ campaignId: 'c1', candidateId: 'cand1', accepted: true });
    http
      .expectOne('/v1/celebrity-voting/campaigns/c1')
      .flush(detail({ myVoteCandidateId: 'cand1' }));
    fixture.detectChanges();

    expect(text()).toContain('Your vote');
  });

  it('marks the candidate this member voted for', () => {
    load(detail({ myVoteCandidateId: 'cand1' }));

    expect(component.isMyVote(component.ballot(component.detail()!)[0]!,
      component.detail()!.campaign)).toBe(true);
    expect(buttonSaying('Vote')).toBeUndefined();
  });

  it('says a member has voted rather than offering a second vote', () => {
    load(detail({ myVoteCandidateId: 'other' }, [candidate({ id: 'cand1' })]));

    expect(text()).toContain('already voted');
    expect(buttonSaying('Vote')).toBeUndefined();
  });

  it('says a member cannot vote for themselves rather than letting it 409', () => {
    load(detail({ acceptsVotes: true }, [candidate({ memberId: ME })]));

    expect(text()).toContain('cannot vote for yourself');
    expect(buttonSaying('Vote')).toBeUndefined();
  });

  it('offers no vote once voting has closed', () => {
    load(detail({ status: 'Closed', acceptsVotes: false }));

    expect(buttonSaying('Vote')).toBeUndefined();
  });

  it('keeps a failed vote off the page-level error', () => {
    load(detail({ acceptsVotes: true }));

    component.vote(component.detail()!.campaign, component.ballot(component.detail()!)[0]!);

    http
      .expectOne('/v1/celebrity-voting/campaigns/c1/votes')
      .flush({ title: 'Campaign.VotingClosed' }, { status: 409, statusText: 'Conflict' });

    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(component.voteError()['cand1']).toBeDefined();
  });

  // ---- Nominating ---------------------------------------------------------

  it('offers a nomination form only while nominations are open', () => {
    load(detail({ status: 'VotingOpen', acceptsNominations: false }));

    expect(buttonSaying('Nominate')).toBeUndefined();
  });

  it('reports a repeat nomination as success, not as an error', () => {
    // The second nominator has done nothing wrong, and one candidacy each is
    // what keeps a vote from splitting.
    load(detail({ status: 'NominationsOpen', acceptsNominations: true, acceptsVotes: false }));

    component.memberId = 'm5';
    component.nominate(component.detail()!.campaign);

    http.expectOne('/v1/celebrity-voting/campaigns/c1/candidates').flush({
      campaignId: 'c1', candidateId: 'cand9', memberId: 'm5', nominated: false,
    });

    // Only the campaign is re-read: ensureCurrentUser caches, so /me is asked
    // once per screen rather than once per reload.
    http
      .expectOne('/v1/celebrity-voting/campaigns/c1')
      .flush(detail({ status: 'NominationsOpen', acceptsNominations: true, acceptsVotes: false }));
    fixture.detectChanges();

    expect(component.nominateError()).toBeNull();
    expect(text()).toContain('had already been put forward');
  });

  it('sends no category rather than an empty one', () => {
    load(detail({ status: 'NominationsOpen', acceptsNominations: true, acceptsVotes: false }));

    component.memberId = '  m5  ';
    component.category = '   ';
    component.nominate(component.detail()!.campaign);

    expect(
      http.expectOne('/v1/celebrity-voting/campaigns/c1/candidates').request.body,
    ).toEqual({ memberId: 'm5', category: null });
  });

  // ---- The published result -----------------------------------------------

  it('asks for the result only once the campaign says it is published', () => {
    // Before then the endpoint answers 404 by design; a speculative call would
    // put an expected 404 on every visit. http.verify() is the assertion.
    load(detail({ status: 'VotingOpen' }));

    expect(component.result()).toBeNull();
  });

  it('shows the frozen ranking, and says it is locked', () => {
    load(
      detail({ status: 'Published', acceptsVotes: false }, [candidate({ id: 'cand1' })]),
      {
        campaignId: 'c1',
        ranking: [{ rank: 1, candidateId: 'cand1', memberId: 'm1', votes: 12 }],
        publishedBy: 'admin',
        publishedAt: new Date().toISOString(),
      },
    );

    expect(text()).toContain('locked after publication');
    expect(text()).toContain('12');
  });

  it('still shows the campaign when the result cannot be read', () => {
    load(detail({ status: 'Published', acceptsVotes: false }), null);

    expect(component.error()).toBeNull();
    expect(text()).toContain('Celebrities of Samaaj 2026');
  });

  // ---- Names it cannot resolve --------------------------------------------

  it('names the reader but nobody else', () => {
    load(detail({}, [candidate({ memberId: ME }), candidate({ id: 'c2', memberId: 'other' })]));

    expect(component.nameFor(ME)).toBe('You');
    expect(component.nameFor('other')).toBe('A member');
    expect(text()).not.toContain('other');
  });
});
