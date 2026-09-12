import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { ProposalsComponent } from './proposals.component';
import { Biodata, Proposal } from './matrimony.models';

function otherParty(overrides: Partial<Biodata> = {}): Biodata {
  return {
    memberId: 'other',
    fullName: 'Anjali Shah',
    photoUrl: null,
    gender: 'Female',
    age: 26,
    gautraId: null,
    professionId: null,
    qualificationId: null,
    cityId: null,
    localityId: null,
    heightCm: null,
    maritalStatusId: null,
    diet: null,
    familyType: null,
    fatherOccupation: null,
    motherOccupation: null,
    siblingsCount: null,
    manglik: null,
    horoscopeNotes: null,
    aboutMe: null,
    preferredAgeMin: null,
    preferredAgeMax: null,
    partnerPreferenceNotes: null,
    ...overrides,
  };
}

function proposal(overrides: Partial<Proposal> = {}): Proposal {
  return {
    id: 'p1',
    fromMemberId: 'me',
    toMemberId: 'other',
    status: 'Pending',
    message: 'Would love to connect.',
    sentAt: new Date().toISOString(),
    respondedAt: null,
    otherParty: otherParty(),
    ...overrides,
  };
}

describe('ProposalsComponent', () => {
  let fixture: ComponentFixture<ProposalsComponent>;
  let component: ProposalsComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ProposalsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(ProposalsComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(sent: Proposal[], received: Proposal[]): void {
    fixture.detectChanges();
    http.expectOne('/v1/matrimony/proposals/sent').flush(sent);
    http.expectOne('/v1/matrimony/proposals/received').flush(received);
    fixture.detectChanges();
  }

  it('shows the Sent tab first', () => {
    load([proposal()], []);

    expect(component.activeTab()).toBe('sent');
    expect(text()).toContain('Anjali Shah');
  });

  it('switches to Received', () => {
    load([], [proposal({ id: 'p2' })]);

    component.activeTab.set('received');
    fixture.detectChanges();

    expect(text()).toContain('Anjali Shah');
  });

  it('names a member whose account was fully erased rather than crashing', () => {
    load([proposal({ otherParty: null })], []);

    expect(text()).toContain('This member is no longer on the platform');
  });

  it('offers Accept/Decline only on a Received proposal that is still Pending', () => {
    load([], [proposal({ id: 'p2', status: 'Accepted' }), proposal({ id: 'p3', status: 'Pending' })]);

    component.activeTab.set('received');
    fixture.detectChanges();

    component.respond('p3', true);

    const request = http.expectOne('/v1/matrimony/proposals/p3/respond');

    expect(request.request.body).toEqual({ accept: true });

    request.flush(proposal({ id: 'p3', status: 'Accepted' }));

    http.expectOne('/v1/matrimony/proposals/sent').flush([]);
    http.expectOne('/v1/matrimony/proposals/received').flush([
      proposal({ id: 'p2', status: 'Accepted' }),
      proposal({ id: 'p3', status: 'Accepted' }),
    ]);
    fixture.detectChanges();

    expect(text()).not.toContain('Decline');
  });

  it('withdraws a pending sent proposal and re-reads both lists', () => {
    load([proposal()], []);

    component.withdraw('p1');

    http.expectOne('/v1/matrimony/proposals/p1/withdraw').flush({ withdrawn: true });

    http.expectOne('/v1/matrimony/proposals/sent').flush([proposal({ status: 'Withdrawn' })]);
    http.expectOne('/v1/matrimony/proposals/received').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('Withdrawn');
  });

  it('keeps a failed action off the page-level error', () => {
    load([proposal()], []);

    component.withdraw('p1');

    http
      .expectOne('/v1/matrimony/proposals/p1/withdraw')
      .flush({ title: 'Conflict', detail: 'This proposal has already been decided.' }, {
        status: 409,
        statusText: 'Conflict',
      });
    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(text()).toContain('This proposal has already been decided.');
  });
});
