import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { CandidateDetailComponent } from './candidate-detail.component';
import { Biodata } from './matrimony.models';

function candidate(overrides: Partial<Biodata> = {}): Biodata {
  return {
    memberId: 'c1',
    fullName: 'Priya Mehta',
    photoUrl: null,
    gender: 'Female',
    age: 27,
    gautraId: null,
    professionId: null,
    qualificationId: null,
    cityId: null,
    localityId: null,
    heightCm: 160,
    maritalStatusId: null,
    diet: 'JainVegetarian',
    familyType: 'Nuclear',
    fatherOccupation: null,
    motherOccupation: null,
    siblingsCount: null,
    manglik: null,
    horoscopeNotes: null,
    aboutMe: 'Loves classical music.',
    preferredAgeMin: null,
    preferredAgeMax: null,
    partnerPreferenceNotes: null,
    ...overrides,
  };
}

describe('CandidateDetailComponent', () => {
  let fixture: ComponentFixture<CandidateDetailComponent>;
  let component: CandidateDetailComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CandidateDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'c1' } } } },
      ],
    });

    fixture = TestBed.createComponent(CandidateDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(found: Biodata = candidate()): void {
    fixture.detectChanges();
    http.expectOne('/v1/matrimony/biodata/c1').flush(found);
    fixture.detectChanges();
  }

  it('shows the candidate biodata', () => {
    load();

    expect(text()).toContain('Priya Mehta');
    expect(text()).toContain('Jain vegetarian');
    expect(text()).toContain('Loves classical music.');
  });

  it('sends a trimmed message, or none when left blank', () => {
    load();

    component.showForm.set(true);
    component.message = '   ';
    component.send('c1');

    expect(http.expectOne('/v1/matrimony/proposals').request.body).toEqual({
      toMemberId: 'c1',
      message: null,
    });
  });

  it('trims the message it sends', () => {
    load();

    component.showForm.set(true);
    component.message = '  Would love to connect.  ';
    component.send('c1');

    expect(http.expectOne('/v1/matrimony/proposals').request.body).toEqual({
      toMemberId: 'c1',
      message: 'Would love to connect.',
    });
  });

  it('confirms the proposal was sent', () => {
    load();

    component.showForm.set(true);
    component.send('c1');

    http.expectOne('/v1/matrimony/proposals').flush({
      id: 'p1',
      fromMemberId: 'me',
      toMemberId: 'c1',
      status: 'Pending',
      message: null,
      sentAt: new Date().toISOString(),
      respondedAt: null,
      otherParty: candidate(),
    });
    fixture.detectChanges();

    expect(text()).toContain('Your proposal has been sent.');
  });

  it('shows the refusal rather than pretending it worked', () => {
    load();

    component.showForm.set(true);
    component.send('c1');

    http
      .expectOne('/v1/matrimony/proposals')
      .flush({ title: 'Conflict', detail: 'A pending proposal already exists.' }, {
        status: 409,
        statusText: 'Conflict',
      });
    fixture.detectChanges();

    expect(text()).toContain('A pending proposal already exists.');
  });
});
