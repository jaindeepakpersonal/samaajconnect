import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { MatrimonySearchComponent } from './candidates-search.component';
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

describe('MatrimonySearchComponent', () => {
  let fixture: ComponentFixture<MatrimonySearchComponent>;
  let component: MatrimonySearchComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MatrimonySearchComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(MatrimonySearchComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function settleFilters(): void {
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'MaritalStatus')
      .flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
  }

  function load(results: Biodata[]): void {
    fixture.detectChanges();
    settleFilters();
    http.expectOne((r) => r.url === '/v1/matrimony/biodata/search').flush(results);
    fixture.detectChanges();
  }

  it('shows candidates found in the caller own Samaaj', () => {
    load([candidate()]);

    expect(text()).toContain('Priya Mehta');
    expect(text()).toContain('27 years');
  });

  it('says nothing matches rather than an empty grid', () => {
    load([]);

    expect(text()).toContain('No candidates match');
  });

  it('sends only the filters actually set', () => {
    fixture.detectChanges();
    settleFilters();
    http.expectOne((r) => r.url === '/v1/matrimony/biodata/search').flush([]);
    fixture.detectChanges();

    component.filters.gender = 'Female';
    component.filters.minAge = 24;
    component.filters.excludeOwnGautra = true;
    component.load();

    const request = http.expectOne((r) => r.url === '/v1/matrimony/biodata/search');

    expect(request.request.params.get('gender')).toBe('Female');
    expect(request.request.params.get('minAge')).toBe('24');
    expect(request.request.params.get('excludeOwnGautra')).toBe('true');
    expect(request.request.params.has('maxAge')).toBe(false);
    expect(request.request.params.has('diet')).toBe(false);
  });

  it('offers a retry when the search fails', () => {
    fixture.detectChanges();
    settleFilters();
    http
      .expectOne((r) => r.url === '/v1/matrimony/biodata/search')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });
});
