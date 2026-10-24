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
    cityId: 'city1',
    localityId: 'loc1',
    addressLine: 'Shop 4, Bapu Bazaar',
    photoUrl: null,
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

    URL.createObjectURL = () => 'blob:test';
    URL.revokeObjectURL = () => undefined;

    fixture = TestBed.createComponent(BusinessesDirectoryComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** The page's text with the template's own line breaks collapsed. */
  const text = () => (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ');

  function settleFilters(): void {
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'BusinessCategory')
      .flush([{ id: 'cat1', category: 'BusinessCategory', code: 'SWEETS', label: 'Sweets', sortOrder: 1, isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush([
      { id: 'city1', countryId: 'in', name: 'Udaipur', isActive: true },
      { id: 'city2', countryId: 'in', name: 'Bhinder', isActive: true },
    ]);
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([
      { id: 'loc1', cityId: 'city1', name: 'Hiran Magri', isActive: true },
      { id: 'loc2', cityId: 'city2', name: 'Station Road', isActive: true },
    ]);
  }

  /** Answers each owner lookup, and the photo fetches `scAuthedSrc` makes. */
  function settleOwnersAndPhotos(): void {
    for (const request of http.match((r) => r.url.startsWith('/v1/members/'))) {
      const id = request.request.url.split('/').pop()!;

      if (id === 'hidden') {
        request.flush({}, { status: 404, statusText: 'Not Found' });
      } else {
        request.flush({ id, fullName: 'Mahendra Shah', photoUrl: `/v1/members/${id}/photo` });
      }
    }

    fixture.detectChanges();

    for (const request of http.match((r) => r.url.includes('/photo') && r.method === 'GET')) {
      request.flush(new Blob(['x']));
    }

    fixture.detectChanges();
  }

  function load(results: Business[]): void {
    fixture.detectChanges();
    settleFilters();
    http.expectOne((r) => r.url === '/v1/businesses').flush(results);
    fixture.detectChanges();
    settleOwnersAndPhotos();
  }

  it('lists a business with its photo, category, where it is and who runs it', () => {
    load([business({ photoUrl: '/v1/businesses/b1/photo?v=1' })]);

    const card = fixture.nativeElement.querySelector('.biz-card') as HTMLElement;

    expect(card.querySelector('img.biz-cover')).not.toBeNull();
    expect(card.textContent).toContain('Sweets');
    expect(card.textContent).toContain('Shop 4, Bapu Bazaar · Hiran Magri, Udaipur');
    expect(card.textContent!.replace(/\s+/g, ' ')).toContain('Run by Mahendra Shah');
    expect(card.querySelector('a[href="/members/owner"]')).not.toBeNull();
  });

  it('shows initials for a business with no photo', () => {
    load([business()]);

    expect(fixture.nativeElement.querySelector('.biz-cover.placeholder').textContent.trim()).toBe('SS');
  });

  it('asks once per owner, however many businesses they run', () => {
    fixture.detectChanges();
    settleFilters();
    http.expectOne((r) => r.url === '/v1/businesses').flush([
      business(),
      business({ id: 'b2', name: 'Shah Namkeen' }),
    ]);
    fixture.detectChanges();

    expect(http.match((r) => r.url === '/v1/members/owner').length).toBe(1);
    settleOwnersAndPhotos();
  });

  it('says "a member" for an owner this reader may not see, rather than an id', () => {
    load([business({ ownerMemberId: 'hidden' })]);

    expect(text()).toContain('Run by a member');
    expect(text()).not.toContain('hidden');
  });

  it('says nothing matches rather than an empty grid', () => {
    load([]);

    expect(text()).toContain('No businesses match');
  });

  it('narrows the locality choices to the chosen city, and drops a locality from another', () => {
    load([]);

    component.filters.localityId = 'loc2';
    component.chooseCity('city1');

    expect(component.localitiesFor('city1').map((l) => l.id)).toEqual(['loc1']);
    expect(component.filters.localityId).toBe('');
  });

  it('sends only the filters actually set', () => {
    load([]);

    component.filters.categoryId = 'cat1';
    component.filters.cityId = 'city1';
    component.filters.term = 'sweets';
    component.load();

    const request = http.expectOne((r) => r.url === '/v1/businesses');

    expect(request.request.params.get('categoryId')).toBe('cat1');
    expect(request.request.params.get('cityId')).toBe('city1');
    expect(request.request.params.get('term')).toBe('sweets');
    expect(request.request.params.has('localityId')).toBe(false);
    request.flush([]);
  });

  it('offers a retry when the search fails', () => {
    fixture.detectChanges();
    settleFilters();
    http.expectOne((r) => r.url === '/v1/businesses').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();

    expect(text()).toContain('Try again');
  });
});
