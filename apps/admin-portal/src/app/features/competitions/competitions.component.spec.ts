import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { CompetitionDetailComponent } from './competition-detail.component';
import { CompetitionDialogComponent } from './competition-dialog.component';
import { CompetitionListComponent } from './competition-list.component';
import { Competition, CompetitionDetail, Round, Scoresheet } from '../../core/admin.models';

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
    localityId: null,
    venue: null,
    cityId: null,
    photoUrl: null,
    roundCount: 2,
    maxMarks: 10,
    auditionRequired: false,
    auditionDate: null,
    finalistCount: null,
    ...overrides,
  };
}

/** Answers every outstanding city and competition-type list - the dialog and the detail page each ask for their own. */
function settleLists(http: HttpTestingController) {
  for (const request of http.match((r) => r.url === '/v1/master-data/cities')) {
    request.flush([{ id: 'udr', countryId: 'in', stateId: null, name: 'Udaipur', isActive: true }]);
  }

  for (const request of http.match((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'CompetitionType')) {
    request.flush([{ id: 'type1', category: 'CompetitionType', code: 'RANG', label: 'Rangoli', sortOrder: 1, isActive: true }]);
  }
}

function round(overrides: Partial<Round> = {}): Round {
  return {
    id: 'round1',
    name: 'Round 1',
    sequenceNumber: 1,
    status: 'Open',
    createdAt: '2026-08-21T00:00:00Z',
    closedAt: null,
    entryCount: 0,
    hasPublishedResult: false,
    kind: 'Main',
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
    hasPublishedResult: false,
    ...overrides,
  };
}

const MEMBERS = [
  { id: 'm1', fullName: 'Diya Jain' },
  { id: 'm2', fullName: 'Aarav Jain' },
  { id: 'm3', fullName: 'Kavya Shah' },
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

  const click = { currentTarget: null } as unknown as MouseEvent;

  function settleLocalities() {
    http.expectOne((r) => r.url === '/v1/master-data/localities')
      .flush([{ id: 'loc1', cityId: 'udr', name: 'Hiran Magri', isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/cities')
      .flush([{ id: 'udr', countryId: 'in', stateId: null, name: 'Udaipur', isActive: true }]);
  }

  function load(competitions: Competition[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/competitions').flush(competitions);
    settleLocalities();
    fixture.detectChanges();
  }

  function dialog(): CompetitionDialogComponent {
    settleLists(http);

    return fixture.debugElement.query((el) => el.componentInstance instanceof CompetitionDialogComponent)
      .componentInstance as CompetitionDialogComponent;
  }

  function openCreate(): CompetitionDialogComponent {
    component.openCreate(click);
    fixture.detectChanges();

    const form = dialog();
    form.title = 'Rangoli Contest 2026';
    form.registrationStartAt = '2026-08-01T00:00';
    form.registrationEndAt = '2026-08-20T00:00';

    return form;
  }

  it('reads a 404 as the module being off', () => {
    fixture.detectChanges();
    http.expectOne('/v1/competitions').flush({}, { status: 404, statusText: 'Not Found' });
    settleLocalities();
    fixture.detectChanges();

    expect(text()).toContain('does not run the competitions module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('refuses a registration window that closes before it opens', () => {
    load();

    const form = openCreate();
    form.registrationStartAt = '2026-08-20T00:00';
    form.registrationEndAt = '2026-08-01T00:00';

    expect(form.windowInvalid()).toBe(true);
    expect(form.canSave()).toBe(false);

    form.save();
    http.expectNone('/v1/competitions');
  });

  it('sets a competition up in a popup, as a draft, with its type and how it is judged', () => {
    load();

    const form = openCreate();
    form.localityId = 'loc1';
    form.venue = ' Mahavir Bhawan, Hall 2 ';
    form.cityId = 'udr';
    form.competitionTypeId = 'type1';
    form.roundCount = 3;
    form.maxMarks = 10;
    form.save();

    const call = http.expectOne('/v1/competitions');
    const body = call.request.body as Record<string, unknown>;

    expect(call.request.method).toBe('POST');
    expect(body['title']).toBe('Rangoli Contest 2026');
    expect(body['description']).toBeNull();
    expect(body['localityId']).toBe('loc1');
    expect(body['venue']).toBe('Mahavir Bhawan, Hall 2');
    expect(body['cityId']).toBe('udr');
    expect(body['competitionTypeId']).toBe('type1');
    expect(body['roundCount']).toBe(3);
    expect(body['maxMarks']).toBe(10);
    expect(body['auditionRequired']).toBe(false);
    expect(body['auditionDate']).toBeNull();
    expect(body['finalistCount']).toBeNull();

    call.flush(competition());
    http.expectOne('/v1/competitions').flush([competition({ localityId: 'loc1' })]);
    fixture.detectChanges();

    expect(text()).toContain('created as a draft');
    expect(text()).toContain('Hiran Magri, Udaipur');
  });

  it('asks for the audition date and how many go through, and sends them', () => {
    load();

    const form = openCreate();
    form.auditionRequired = true;

    expect(form.canSave()).toBe(false);
    expect(form.formatProblem()).toContain('audition date');

    form.auditionDate = '2026-08-25';
    form.finalistCount = 5;

    expect(form.canSave()).toBe(true);

    form.save();

    const body = http.expectOne('/v1/competitions').request.body as Record<string, unknown>;

    expect(body['auditionRequired']).toBe(true);
    expect(body['auditionDate']).toBe('2026-08-25');
    expect(body['finalistCount']).toBe(5);

    http.match('/v1/competitions');
  });

  it('refuses an audition before registration opens, and max marks of nothing', () => {
    load();

    const form = openCreate();
    form.auditionRequired = true;
    form.auditionDate = '2026-07-30';
    form.finalistCount = 5;

    expect(form.formatProblem()).toContain('before registration opens');

    form.auditionDate = '2026-08-25';
    form.maxMarks = 0;

    expect(form.formatProblem()).toContain('Max marks');
    expect(form.canSave()).toBe(false);
  });

  it('offers Edit only while the competition may still be corrected', () => {
    load([
      competition({ id: 'draft', status: 'Draft' }),
      competition({ id: 'open', status: 'RegistrationOpen' }),
      competition({ id: 'closed', status: 'RegistrationClosed' }),
    ]);

    const edits = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .filter((b) => b.textContent?.trim() === 'Edit');

    expect(edits).toHaveLength(2);
  });

  it('corrects a competition in the same popup, starting from what it holds', () => {
    const existing = competition({
      status: 'RegistrationOpen',
      description: 'Theme: Diwali',
      competitionTypeId: 'type1',
      roundCount: 4,
      auditionRequired: true,
      auditionDate: '2026-08-25',
      finalistCount: 6,
    });

    load([existing]);

    component.openEdit(existing, click);
    fixture.detectChanges();

    const form = dialog();

    expect(form.title).toBe('Rangoli Contest 2026');
    expect(form.description).toBe('Theme: Diwali');
    expect(form.registrationStartAt).toMatch(/^2026-0[78]-\d\dT\d\d:\d\d$/);
    expect(form.competitionTypeId).toBe('type1');
    expect(form.roundCount).toBe(4);
    expect(form.finalistCount).toBe(6);

    form.title = 'Rangoli Contest 2026 - Diwali';
    form.save();

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}`);

    expect(call.request.method).toBe('PATCH');
    expect((call.request.body as Record<string, unknown>)['title']).toBe('Rangoli Contest 2026 - Diwali');

    call.flush(competition());
    http.expectOne('/v1/competitions').flush([competition()]);
    fixture.detectChanges();

    expect(text()).toContain('is saved');
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

    URL.createObjectURL = () => 'blob:test';
    URL.revokeObjectURL = () => undefined;

    fixture = TestBed.createComponent(CompetitionDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function dialog(): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('[role="dialog"]');
  }

  function load(subject: CompetitionDetail = detail()) {
    fixture.detectChanges();
    http.expectOne(`/v1/competitions/${COMPETITION_ID}`).flush(subject);
    http.expectOne('/v1/members?take=100').flush({ items: MEMBERS, hasMore: false });
    http.expectOne((r) => r.url === '/v1/master-data/localities')
      .flush([{ id: 'loc1', cityId: 'udr', name: 'Hiran Magri', isActive: true }]);
    settleLists(http);
    fixture.detectChanges();

    for (const request of http.match((r) => r.url.includes('/photo') && r.method === 'GET')) {
      request.flush(new Blob(['x']));
    }

    fixture.detectChanges();
  }

  /** After an action: the competition again. The directory is read once per page. */
  function reload(subject: CompetitionDetail = detail()) {
    http.expectOne(`/v1/competitions/${COMPETITION_ID}`).flush(subject);
    fixture.detectChanges();
  }

  function tab(value: string) {
    component.showTab(value as Parameters<CompetitionDetailComponent['showTab']>[0]);
    fixture.detectChanges();
  }

  // ---- The head ---------------------------------------------------------------

  it('names where it is held and its type, and shows the cover members will see', () => {
    load(detail({ competition: competition({
      venue: 'Mahavir Bhawan, Hall 2',
      localityId: 'loc1',
      cityId: 'udr',
      competitionTypeId: 'type1',
      photoUrl: '/v1/competitions/comp1/photo?v=1',
    }) }));

    expect(text()).toContain('Mahavir Bhawan, Hall 2 · Hiran Magri, Udaipur');
    expect(text()).toContain('Rangoli');
    expect(fixture.nativeElement.querySelector('img.competition-cover')).not.toBeNull();
  });

  it('names the city or village alone when no locality is picked', () => {
    load(detail({ competition: competition({ cityId: 'udr' }) }));

    expect(component.whereHeld(component.competition()!)).toBe('Udaipur');
  });

  it('opens on Details, saying how it is judged', () => {
    load(detail({ competition: competition({ auditionRequired: true, auditionDate: '2026-08-25', finalistCount: 6 }) }));

    expect(component.tab()).toBe('details');
    expect(text()).toContain('10 per judge');
    expect(text()).toContain('the top 6 go through');
  });

  it('offers Edit, in a popup, only until registration closes', () => {
    load(detail({ competition: competition({ status: 'RegistrationOpen' }) }));

    component.openPopup('edit');
    fixture.detectChanges();
    settleLists(http);

    expect(fixture.debugElement.query((el) => el.componentInstance instanceof CompetitionDialogComponent)).not.toBeNull();
  });

  it('uploads and removes the photo, then re-reads the competition', () => {
    load();

    const file = new File(['x'], 'cover.png', { type: 'image/png' });
    component.uploadPhoto({ target: { files: [file], value: 'cover.png' } } as unknown as Event);

    const upload = http.expectOne(`/v1/competitions/${COMPETITION_ID}/photo`);

    expect(upload.request.method).toBe('POST');
    expect((upload.request.body as FormData).get('file')).toBe(file);
    upload.flush(competition());
    reload();

    component.removePhoto();
    const removal = http.expectOne(`/v1/competitions/${COMPETITION_ID}/photo`);

    expect(removal.request.method).toBe('DELETE');
    removal.flush(competition());
    reload();

    expect(text()).toContain('Photo removed.');
  });

  // ---- The stage --------------------------------------------------------------

  it('offers the one stage the competition can move to', () => {
    load(detail({ competition: competition({ status: 'Draft' }) }));
    expect(component.nextStatus()).toBe('RegistrationOpen');
  });

  it('moves the competition on', () => {
    load(detail({ competition: competition({ status: 'Draft' }) }));

    component.askToMove('RegistrationOpen');

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/status`);
    expect(call.request.body).toEqual({ status: 'RegistrationOpen' });

    call.flush({});
    reload();
  });

  it('asks before starting judging, saying what it makes and who it enters', () => {
    load(detail({ competition: competition({ status: 'RegistrationClosed', registrationCount: 7, auditionRequired: true, finalistCount: 3, auditionDate: '2026-08-25' }) }));

    component.askToMove('InProgress');
    fixture.detectChanges();

    expect(dialog()?.textContent).toContain('the audition, then Round 1 to 2');
    expect(dialog()?.textContent).toContain('all 7 who applied in the audition');

    // http.verify() in afterEach: asking sent nothing.
    component.confirm();

    http.expectOne(`/v1/competitions/${COMPETITION_ID}/status`).flush({});
    reload();

    expect(dialog()).toBeNull();
    expect(text()).toContain('Everybody who applied is entered');
  });

  // ---- Judges -----------------------------------------------------------------

  it('assigns a judge from a popup, on the main panel when there is no audition', () => {
    load();

    component.openJudge(null);
    fixture.detectChanges();

    expect(dialog()?.querySelector('#judge-panel')).toBeNull();

    component.judgeMemberId = 'm2';
    component.assignJudge();

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/judges`);
    expect(call.request.body).toEqual({ memberId: 'm2', assign: true, panel: 'Main' });

    call.flush({});
    reload();

    expect(dialog()).toBeNull();
  });

  it('assigns an audition judge, and moves a judge to another panel', () => {
    load(detail({
      competition: competition({ auditionRequired: true, auditionDate: '2026-08-25', finalistCount: 3 }),
      judges: [{ id: 'j1', memberId: 'm1', assignedAt: '2026-08-21T00:00:00Z', panel: 'Main' }],
    }));

    component.openJudge(null);
    fixture.detectChanges();

    expect(component.judgeCandidates().map((m) => m.id)).toEqual(['m2', 'm3']);

    component.judgeMemberId = 'm2';
    component.judgePanel = 'Audition';
    component.assignJudge();

    expect(http.expectOne(`/v1/competitions/${COMPETITION_ID}/judges`).request.body)
      .toEqual({ memberId: 'm2', assign: true, panel: 'Audition' });
    http.match(`/v1/competitions/${COMPETITION_ID}/judges`);
  });

  it('removes a judge', () => {
    load(detail({ judges: [{ id: 'j1', memberId: 'm2', assignedAt: '2026-08-21T00:00:00Z', panel: 'Main' }] }));

    component.removeJudge('m2');

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/judges`);
    expect(call.request.body).toEqual({ memberId: 'm2', assign: false, panel: 'Main' });

    call.flush({});
    reload();
  });

  // ---- Rounds -----------------------------------------------------------------

  it('says the rounds are made when judging starts', () => {
    load(detail({ competition: competition({ roundCount: 3 }) }));
    tab('rounds');

    expect(text()).toContain('Round 1 to 3');
  });

  it('shows a round in a popup, offering only applicants not yet in it', () => {
    load(detail({ rounds: [round()], registeredMemberIds: ['m1', 'm2'] }));
    tab('rounds');

    component.showRound(round());

    http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1`).flush({
      round: round(),
      entries: [{ id: 'e1', participantMemberId: 'm1', createdAt: '2026-08-21T00:00:00Z', averageScore: 7.5, myScore: null }],
      canSeeScores: true,
    });
    fixture.detectChanges();

    expect(dialog()?.textContent).toContain('Diya Jain');
    expect(dialog()?.textContent).toContain('7.5');
    expect(component.unenteredRegistrants()).toEqual(['m2']);
  });

  it('adds somebody who applied to a round', () => {
    load(detail({ rounds: [round()], registeredMemberIds: ['m2'] }));

    component.showRound(round());
    http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1`).flush({ round: round(), entries: [], canSeeScores: true });
    fixture.detectChanges();

    component.entryMemberId = 'm2';
    component.createEntry('round1');

    const call = http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1/entries`);
    expect(call.request.body).toEqual({ participantMemberId: 'm2' });

    call.flush({});
    reload(detail({ rounds: [round({ entryCount: 1 })], registeredMemberIds: ['m2'] }));
    http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1`).flush({ round: round({ entryCount: 1 }), entries: [], canSeeScores: true });
  });

  it('closes a main round at once', () => {
    load(detail({ rounds: [round()] }));

    component.askToClose(round());

    http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1/close`).flush(round({ status: 'Closed' }));
    reload(detail({ rounds: [round({ status: 'Closed' })] }));
  });

  it('asks before closing the audition, because closing it picks the finalists', () => {
    const audition = round({ id: 'aud', name: 'Audition', kind: 'Audition' });

    load(detail({
      competition: competition({ status: 'InProgress', auditionRequired: true, auditionDate: '2026-08-25', finalistCount: 3 }),
      rounds: [audition, round()],
    }));

    component.askToClose(audition);
    fixture.detectChanges();

    expect(dialog()?.textContent).toContain('The top 3 by average go through');

    component.confirm();

    http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/aud/close`).flush(round({ status: 'Closed' }));
    reload();

    expect(text()).toContain('the finalists are in every round');
  });

  it('publishes a closed round\'s result', () => {
    load(detail({ rounds: [round({ status: 'Closed', hasPublishedResult: false })] }));

    component.publish('round1');

    http.expectOne(`/v1/competitions/${COMPETITION_ID}/rounds/round1/results`).flush({
      roundId: 'round1',
      ranking: [],
      publishedBy: 'admin',
      publishedAt: '2026-08-22T00:00:00Z',
    });

    reload(detail({ rounds: [round({ status: 'Closed', hasPublishedResult: true })] }));
    tab('rounds');

    const buttons = [...fixture.nativeElement.querySelectorAll('button')].map((b) =>
      (b as HTMLElement).textContent?.trim(),
    );

    expect(buttons.some((label) => label?.startsWith('Publish round result'))).toBe(false);
  });

  // ---- Scores: every judge's marks ------------------------------------------------

  const scoresheet: Scoresheet = {
    competitionId: COMPETITION_ID,
    maxMarks: 10,
    judges: [
      { id: 'j1', memberId: 'm3', assignedAt: '2026-08-21T00:00:00Z', panel: 'Main' },
    ],
    rounds: [
      {
        round: round({ entryCount: 2 }),
        entries: [
          {
            entryId: 'e1',
            participantMemberId: 'm1',
            marks: [{ judgeMemberId: 'm3', value: 9, comments: 'Bold colours', updatedAt: '2026-08-21T00:00:00Z' }],
            average: 9,
            wentThrough: null,
          },
          { entryId: 'e2', participantMemberId: 'm2', marks: [], average: null, wentThrough: null },
        ],
      },
    ],
    standings: [
      { rank: 1, memberId: 'm1', total: 9 },
      { rank: 2, memberId: 'm2', total: 0 },
    ],
    hasPublishedResult: false,
  };

  it('shows every judge\'s mark on every participant, and the standings they add up to', () => {
    load(detail({ rounds: [round()] }));
    tab('scores');

    http.expectOne(`/v1/competitions/${COMPETITION_ID}/scoresheet`).flush(scoresheet);
    fixture.detectChanges();

    const table = fixture.nativeElement.querySelectorAll('table')[1] as HTMLTableElement;

    expect(table.textContent).toContain('Kavya Shah');
    expect(table.textContent).toContain('9');
    expect(table.textContent).toContain('—');
    expect(text()).toContain('Standings now');
    expect(fixture.nativeElement.querySelectorAll('tr.podium').length).toBe(2);
  });

  // ---- Results ------------------------------------------------------------------

  it('waits for every round to close before offering to publish', () => {
    load(detail({ rounds: [round(), round({ id: 'r2', name: 'Round 2', status: 'Closed' })] }));
    tab('results');

    expect(text()).toContain('1 round(s) still open');
  });

  it('publishes the winners after asking, and shows them', () => {
    load(detail({ rounds: [round({ status: 'Closed' })] }));
    tab('results');

    component.openPopup('publish');
    fixture.detectChanges();

    expect(dialog()?.textContent).toContain('first, second and third');

    component.publishResult();

    http.expectOne(`/v1/competitions/${COMPETITION_ID}/result`).flush({});
    reload(detail({ rounds: [round({ status: 'Closed' })], hasPublishedResult: true }));

    http.expectOne(`/v1/competitions/${COMPETITION_ID}/result`).flush({
      competitionId: COMPETITION_ID,
      winners: [{ rank: 1, memberId: 'm1', total: 18 }, { rank: 2, memberId: 'm2', total: 15 }],
      standings: [{ rank: 1, memberId: 'm1', total: 18 }, { rank: 2, memberId: 'm2', total: 15 }],
      roundCount: 2,
      maxMarks: 10,
      publishedBy: 'admin',
      publishedAt: '2026-08-30T00:00:00Z',
    });
    fixture.detectChanges();

    expect(text()).toContain('First');
    expect(text()).toContain('Diya Jain');
  });

  it('names the member from the directory rather than printing an id', () => {
    load(detail({ judges: [{ id: 'j1', memberId: 'm1', assignedAt: '2026-08-21T00:00:00Z', panel: 'Main' }] }));
    tab('judges');

    expect(text()).toContain('Diya Jain');
  });

  it('explains a 404 as no such competition rather than as an error', () => {
    fixture.detectChanges();
    http
      .expectOne(`/v1/competitions/${COMPETITION_ID}`)
      .flush({}, { status: 404, statusText: 'Not Found' });
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
    settleLists(http);
    fixture.detectChanges();

    expect(text()).toContain('No such competition');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
