import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, CurrentUser } from '@samaajconnect/shared';
import { Trust, TrustElection, TrustElectionDetail, TrustMember } from './trust.models';
import { TrustDetailComponent } from './trust-detail.component';

const TRUST_ID = 't1';
const ME = 'u1';

const currentUser: CurrentUser = {
  userId: ME,
  tenantId: 'tenant1',
  tenantSlug: 'mahavir-samaj',
  mobileOrEmail: 'ravi@example.com',
  fullName: 'Ravi Shah',
  status: 'Active',
  isContactVerified: true,
  lastLoginAt: null,
  roles: ['Member'],
  permissions: ['Members.Read'],
};

function trust(overrides: Partial<Trust> = {}): Trust {
  return {
    id: TRUST_ID,
    localityId: 'loc1',
    name: 'Hiran Magri Samaaj Trust',
    status: 'Active',
    createdAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

function rosterMember(overrides: Partial<TrustMember> = {}): TrustMember {
  return {
    id: 'tm1',
    trustId: TRUST_ID,
    memberId: ME,
    positionTitle: 'Trustee',
    roleId: 'r0',
    status: 'Active',
    addedBy: 'admin1',
    addedAt: '2026-09-02T00:00:00Z',
    removedBy: null,
    removedAt: null,
    ...overrides,
  };
}

function election(overrides: Partial<TrustElection> = {}): TrustElection {
  return {
    id: 'e1',
    trustId: TRUST_ID,
    positionTitle: 'Treasurer',
    nominationStartAt: '2026-09-01T00:00:00Z',
    nominationEndAt: '2026-09-10T00:00:00Z',
    votingStartAt: '2026-09-11T00:00:00Z',
    votingEndAt: '2026-09-20T00:00:00Z',
    status: 'NominationsOpen',
    acceptsNominations: true,
    acceptsVotes: false,
    myVoteCandidateIds: [],
    candidateCount: 0,
    winnerMemberIds: [],
    publishedAt: null,
    createdAt: '2026-08-25T00:00:00Z',
    roleId: 'r1',
    seats: 1,
    ...overrides,
  };
}

describe('TrustDetailComponent', () => {
  let fixture: ComponentFixture<TrustDetailComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TrustDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', TRUST_ID]]) } },
        },
      ],
    });

    fixture = TestBed.createComponent(TrustDetailComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  function load(members: TrustMember[] = [], elections: TrustElection[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/identity/me').flush(currentUser);
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}`).flush(trust());
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/members`).flush(members);
    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/elections`).flush(elections);
    fixture.detectChanges();
  }

  it('shows the reader as "You" on the roster without a directory call', () => {
    load([rosterMember()]);

    http.expectNone(`/v1/members/${ME}`);
    expect(text()).toContain('You');
    expect(text()).toContain('You are on this Trust');
  });

  it('says when nobody has been added yet', () => {
    load([]);

    expect(text()).toContain('Nobody has been added yet');
  });

  it('lets an active trustee nominate a fellow trustee', () => {
    load([rosterMember(), rosterMember({ id: 'tm2', memberId: 'm2' })], [election()]);

    fixture.componentInstance.toggleElection('e1');

    const detail: TrustElectionDetail = { election: election(), candidates: [] };
    http.expectOne('/v1/trust/elections/e1').flush(detail);
    http.expectOne(`/v1/members/m2`).flush({ id: 'm2', fullName: 'Aarav Jain' });
    fixture.detectChanges();

    expect(text()).toContain('Nominate somebody');

    fixture.componentInstance.setNomineeDraft('e1', 'm2');
    fixture.componentInstance.nominate('e1');

    http.expectOne('/v1/trust/elections/e1/candidates').flush({
      electionId: 'e1',
      candidateId: 'c1',
      memberId: 'm2',
      nominated: true,
    });
    http.expectOne('/v1/trust/elections/e1').flush(detail);
  });

  it('refuses to offer a vote button for yourself', () => {
    const votingElection = election({
      status: 'VotingOpen',
      acceptsNominations: false,
      acceptsVotes: true,
    });

    load([rosterMember()], [votingElection]);

    fixture.componentInstance.toggleElection('e1');

    http.expectOne('/v1/trust/elections/e1').flush({
      election: votingElection,
      candidates: [{ id: 'c1', memberId: ME, status: 'Approved', nominatedBy: ME, votes: 0 }],
    });
    fixture.detectChanges();

    expect(text()).toContain('You cannot vote for yourself');
  });

  it('casts a multi-seat ballot once, capped at the seats', () => {
    const votingElection = election({
      positionTitle: 'Secretary',
      status: 'VotingOpen',
      acceptsNominations: false,
      acceptsVotes: true,
      seats: 2,
    });

    load(
      [rosterMember(), rosterMember({ id: 'tm2', memberId: 'm2' }), rosterMember({ id: 'tm3', memberId: 'm3' }),
        rosterMember({ id: 'tm4', memberId: 'm4' })],
      [votingElection],
    );
    for (const id of ['m2', 'm3', 'm4']) {
      http.expectOne(`/v1/members/${id}`).flush({ id, fullName: 'Member ' + id });
    }

    fixture.componentInstance.toggleElection('e1');

    const candidates = ['m2', 'm3', 'm4'].map((memberId, i) => ({
      id: 'c' + i,
      memberId,
      status: 'Approved' as const,
      nominatedBy: ME,
      votes: 0,
    }));

    http.expectOne('/v1/trust/elections/e1').flush({ election: votingElection, candidates });
    fixture.detectChanges();

    expect(text()).toContain('Pick up to 2');

    const boxes = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];

    expect(boxes()).toHaveLength(3);

    boxes()[0]!.click();
    boxes()[1]!.click();
    fixture.detectChanges();

    expect(boxes()[2]!.disabled).toBe(true);
    expect(text()).toContain('Cast my ballot (2 of 2)');

    fixture.componentInstance.castVote('e1', fixture.componentInstance.picks('e1'));

    const request = http.expectOne('/v1/trust/elections/e1/votes');
    expect(request.request.body).toEqual({ candidateIds: ['c0', 'c1'] });
    request.flush({ electionId: 'e1', candidateIds: ['c0', 'c1'], accepted: true });

    http.expectOne(`/v1/trust/trusts/${TRUST_ID}/elections`).flush([
      { ...votingElection, myVoteCandidateIds: ['c0', 'c1'] },
    ]);
    http.expectOne('/v1/trust/elections/e1').flush({
      election: { ...votingElection, myVoteCandidateIds: ['c0', 'c1'] },
      candidates,
    });
    fixture.detectChanges();

    expect(text().match(/Your vote/g)).toHaveLength(2);
  });

  it('names every winner of a published election', () => {
    load(
      [rosterMember(), rosterMember({ id: 'tm2', memberId: 'm2' })],
      [election({ status: 'Published', acceptsNominations: false, seats: 2, winnerMemberIds: [ME, 'm2'] })],
    );
    http.expectOne('/v1/members/m2').flush({ id: 'm2', fullName: 'Aarav Jain' });
    fixture.detectChanges();

    expect(text()).toContain('Elected:');
    expect(text()).toContain('You');
    expect(text()).toContain('Aarav Jain');
  });
});
