import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { BusinessesDirectoryComponent } from './directory.component';
import { Business } from './businesses.models';

function business(overrides: Partial<Business> = {}): Business {
  return {
    id: 'b1',
    ownerMemberId: 'owner',
    name: 'Shah Sweets',
    categoryId: 'cat1',
    cityId: null,
    localityId: null,
    description: 'Traditional sweets.',
    contactPhone: '9876543210',
    contactEmail: 'shop@example.com',
    isActive: true,
    products: [],
    createdAt: new Date().toISOString(),
    updatedAt: null,
    ...overrides,
  };
}

describe('BusinessesDirectoryComponent', () => {
  let fixture: ComponentFixture<BusinessesDirectoryComponent>;
  let component: BusinessesDirectoryComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BusinessesDirectoryComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(BusinessesDirectoryComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function settleFilters(): void {
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'BusinessCategory')
      .flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
  }

  function load(results: Business[]): void {
    fixture.detectChanges();
    settleFilters();
    http.expectOne((r) => r.url === '/v1/businesses').flush(results);
    fixture.detectChanges();
  }

  it('shows businesses found in the caller own Samaaj', () => {
    load([business()]);

    expect(text()).toContain('Shah Sweets');
  });

  it('says nothing matches rather than an empty grid', () => {
    load([]);

    expect(text()).toContain('No businesses match');
  });

  it('sends only the filters actually set', () => {
    fixture.detectChanges();
    settleFilters();
    http.expectOne((r) => r.url === '/v1/businesses').flush([]);
    fixture.detectChanges();

    component.filters.categoryId = 'cat1';
    component.filters.term = 'sweets';
    component.load();

    const request = http.expectOne((r) => r.url === '/v1/businesses');

    expect(request.request.params.get('categoryId')).toBe('cat1');
    expect(request.request.params.get('term')).toBe('sweets');
    expect(request.request.params.has('localityId')).toBe(false);
  });

  it('offers a retry when the search fails', () => {
    fixture.detectChanges();
    settleFilters();
    http.expectOne((r) => r.url === '/v1/businesses').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });
});
