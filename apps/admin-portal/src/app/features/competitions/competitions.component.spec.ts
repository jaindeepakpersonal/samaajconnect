import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { CompetitionDetailComponent } from './competition-detail.component';
import { CompetitionListComponent } from './competition-list.component';
import { Competition, CompetitionDetail, Round } from '../../core/admin.models';

const COMPETITION_ID = 'comp1';

function competition(overrides: Partial<Competition> = {}): Competition {
  return {
    id: COMPETITION_ID,
    title: 'Rangoli Contest 2026',
    description: null,
    competitionTypeId: null,
    registrationStartAt: '2026-08-01T00:00:00Z',
    registrationEndAt: '2026-08-20T00:00:00Z',
    status: 'Draft',
    acceptsRegistrations: false,
    registrationCount: 0,
    myRegistrationStatus: null,
    isJudge: false,
    createdAt: '2026-07-01T00:00:00Z',
    ...overrides,
  };
}

function round(overrides: Partial<Round> = {}): Round {
  return {
    id: 'round1',
    name: 'Final',
    sequenceNumber: 1,
    status: 'Open',
    createdAt: '2026-08-21T00:00:00Z',
    closedAt: null,
    entryCount: 0,
    hasPublishedResult: false,
    ...overrides,
  };
}

function detail(overrides: Partial<CompetitionDetail> = {}): CompetitionDetail {
  return {
    competition: competition(),
    rounds: [],
    judges: [],
    registeredMemberIds: [],
    canManage: true,
    ...overrides,
  };
}

const MEMBERS = [
  { id: 'm1', fullName: 'Diya Jain' },
  { id: 'm2', fullName: 'Aarav Jain' },
];

describe('CompetitionListComponent', () => {
  let fixture: ComponentFixture<CompetitionListComponent>;
  let component: CompetitionListComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(CompetitionListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(competitions: Competition[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/competitions').flush(competitions);
    fixture.detectChanges();
  }

  it('reads a 404 as the module being off', () => {
    fixture.detectChanges();
    http.expectOne('/v1/competitions').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('does not run the competitions module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('refuses a registration window that closes before it opens', () => {
    load();

    component.title = 'Rangoli Contest 2026';
    component.registrationStartAt = '2026-08-20T00:00';
    component.registrationEndAt = '2026-08-01T00:00';

    expect(component.windowInvalid()).toBe(true);
    expect(component.canCreate()).toBe(false);

    component.create();
    http.expectNone('/v1/competitions');
  });

  it('accepts a valid registration window and creates as a draft', () => {
    load();

    component.title = 'Rangoli Contest 2026';
    component.registrationStartAt = '2026-08-01T00:00';
    component.registrationEndAt = '2026-08-20T00:00';

    expect(component.windowInvalid()).toBe(false);

    component.create();

    const body = http.expectOne('/v1/competitions').request.body as Record<string, unknown>;

    expect(body['title']).toBe('Rangoli Contest 2026');
    expect(body['description']).toBeNull();
    expect(body['competitionTypeId']).toBeNull();
  });

  it('says which competitions are actually open, not just what their status says', () => {
    load([competition({ status: 'RegistrationOpen', acceptsRegistrations: true })]);

    expect(text()).toContain('Open now');
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
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => COMPETITION_ID } } },
        },
      ],
    });

    fixture = TestBed.createComponent(CompetitionDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(subject: CompetitionDetail = detail()) {
    fixture.detectChanges();
    http.expectOne(`/v1/competitions/${COMPETITION_ID}`).flush(subject);
    http.expectOne('/v1/members?limit=100').flush(MEMBERS);
    fixture.detectChanges();
  }

  function reload(subject: CompetitionDetail = detail()) {
    http.expectOne(`/v1/competitions/${COMPETITION_ID}`).flush(subject);
    http.expectOne('/v1/members?limit=100').flush(MEMBERS);
    fixture.detectChanges();
  }

  it('offers the one stage the competition can move to', () => {
    load(detail({ competition: competition({ status: 'Draft' }) }));
    expect(component.nextStatus()).toBe('RegistrationOpen');
  });

  it('moves the competition on', () => {
    load(detail({ competition: competition({ status: 'Draft' }) }));

    component.move('RegistrationOpen');

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/status`);
    expect(call.request.body).toEqual({ status: 'RegistrationOpen' });

    call.flush({});
    reload();
  });

  it('assigns a judge from the member picker', () => {
    load();

    component.judgeMemberId = 'm2';
    component.assignJudge();

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/judges`);
    expect(call.request.body).toEqual({ memberId: 'm2', assign: true });

    call.flush({});
    reload();
  });

  it('removes a judge', () => {
    load(detail({ judges: [{ id: 'j1', memberId: 'm2', assignedAt: '2026-08-21T00:00:00Z' }] }));

    component.removeJudge('m2');

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/judges`);
    expect(call.request.body).toEqual({ memberId: 'm2', assign: false });

    call.flush({});
    reload();
  });

  it('opens a round', () => {
    load();

    component.roundName = 'Final';
    component.createRound();

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds`);
    expect(call.request.body).toEqual({ name: 'Final' });

    call.flush(round());
    reload(detail({ rounds: [round()] }));
  });

  it('offers no entry form once a round is closed', () => {
    load(detail({ rounds: [round({ status: 'Closed' })] }));

    component.selectRound('round1');

    http
      .expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1`)
      .flush({ round: round({ status: 'Closed' }), entries: [], canSeeScores: true });
    fixture.detectChanges();

    expect(text()).not.toContain('Add a participant');
  });

  it('only offers registered members with no entry yet in the picker', () => {
    load(
      detail({
        rounds: [round()],
        registeredMemberIds: ['m1', 'm2'],
      }),
    );

    component.selectRound('round1');

    http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1`).flush({
      round: round(),
      entries: [{ id: 'e1', participantMemberId: 'm1', createdAt: '2026-08-21T00:00:00Z', averageScore: null, myScore: null }],
      canSeeScores: true,
    });
    fixture.detectChanges();

    expect(component.unenteredRegistrants()).toEqual(['m2']);
  });

  it('creates an entry for the picked participant', () => {
    load(detail({ rounds: [round()], registeredMemberIds: ['m2'] }));

    component.selectRound('round1');

    http
      .expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1`)
      .flush({ round: round(), entries: [], canSeeScores: true });
    fixture.detectChanges();

    component.entryMemberId = 'm2';
    component.createEntry('round1');

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1/entries`);
    expect(call.request.body).toEqual({ participantMemberId: 'm2' });

    call.flush({ id: 'e1', participantMemberId: 'm2', createdAt: '2026-08-21T00:00:00Z', averageScore: null, myScore: null });
    reload(detail({ rounds: [round({ entryCount: 1 })], registeredMemberIds: ['m2'] }));

    http
      .expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1`)
      .flush({ round: round({ entryCount: 1 }), entries: [], canSeeScores: true });
    fixture.detectChanges();
  });

  it('closes a round', () => {
    load(detail({ rounds: [round()] }));

    component.closeRound('round1');

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1/close`);

    call.flush(round({ status: 'Closed' }));
    reload(detail({ rounds: [round({ status: 'Closed' })] }));
  });

  it('publishes a closed round only, offering no repeat once published', () => {
    load(detail({ rounds: [round({ status: 'Closed', hasPublishedResult: false })] }));

    component.publish('round1');

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1/results`);

    call.flush({
      roundId: 'round1',
      ranking: [{ rank: 1, entryId: 'e1', participantMemberId: 'm1', averageScore: 90 }],
      publishedBy: 'admin',
      publishedAt: '2026-08-22T00:00:00Z',
    });

    reload(
      detail({ rounds: [round({ status: 'Closed', hasPublishedResult: true })] }),
    );

    const buttons = [...fixture.nativeElement.querySelectorAll('button')].map((b) =>
      (b as HTMLElement).textContent?.trim(),
    );

    expect(buttons).not.toContain('Publish result');
  });

  it('names the member from the directory rather than printing an id', () => {
    load(detail({ judges: [{ id: 'j1', memberId: 'm1', assignedAt: '2026-08-21T00:00:00Z' }] }));

    expect(text()).toContain('Diya Jain');
  });

  it('explains a 404 as no such competition rather than as an error', () => {
    fixture.detectChanges();
    http
      .expectOne(`/v1/competitions/${COMPETITION_ID}`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('No such competition');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
