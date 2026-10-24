import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { MyBusinessesComponent } from './my-businesses.component';
import { Business } from './businesses.models';

function business(overrides: Partial<Business> = {}): Business {
  return {
    id: 'b1',
    ownerMemberId: 'owner',
    name: 'Shah Sweets',
    categoryId: 'cat1',
    cityId: null,
    localityId: null,
    addressLine: null,
    photoUrl: null,
    description: null,
    contactPhone: null,
    contactEmail: null,
    isActive: true,
    products: [],
    createdAt: new Date().toISOString(),
    updatedAt: null,
    ...overrides,
  };
}

describe('MyBusinessesComponent', () => {
  let fixture: ComponentFixture<MyBusinessesComponent>;
  let component: MyBusinessesComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MyBusinessesComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    URL.createObjectURL = () => 'blob:test';
    URL.revokeObjectURL = () => undefined;

    fixture = TestBed.createComponent(MyBusinessesComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function settleLookups(): void {
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'BusinessCategory')
      .flush([{ id: 'cat1', category: 'BusinessCategory', code: 'SWEETS', label: 'Sweets', sortOrder: 1, isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush([
      { id: 'city1', countryId: 'in', name: 'Udaipur', isActive: true },
    ]);
  }

  function settlePhotos(): void {
    for (const request of http.match((r) => r.url.includes('/photo') && r.method === 'GET')) {
      request.flush(new Blob(['x']));
    }

    fixture.detectChanges();
  }

  function load(found: Business[]): void {
    fixture.detectChanges();
    settleLookups();
    http.expectOne('/v1/businesses/mine').flush(found);
    fixture.detectChanges();
    settlePhotos();
  }

  it('lists the caller own businesses as tiles, with a photo or their initials', () => {
    load([business({ photoUrl: '/v1/businesses/b1/photo?v=1' }), business({ id: 'b2', name: 'Shah Namkeen' })]);

    expect(text()).toContain('Shah Sweets');
    expect(fixture.nativeElement.querySelectorAll('img.biz-cover').length).toBe(1);
    expect(fixture.nativeElement.querySelector('.biz-cover.placeholder').textContent.trim()).toBe('SN');
  });

  it('says nothing is registered yet, rather than an empty list', () => {
    load([]);

    expect(text()).toContain('not registered a business yet');
  });

  it('will not submit the create form without a name and category', () => {
    load([]);

    component.form.name = '';
    component.form.categoryId = '';

    expect(component.canCreate()).toBe(false);
  });

  it('asks for the city or village, then only that city’s localities', async () => {
    load([]);

    const city = fixture.nativeElement.querySelector('#new-business-city') as HTMLSelectElement;
    const locality = fixture.nativeElement.querySelector('#new-business-locality') as HTMLSelectElement;

    expect(city).not.toBeNull();

    // ngModel applies [disabled] a microtask after change detection.
    await fixture.whenStable();
    expect(locality.disabled).toBe(true);

    city.value = 'city1';
    city.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    http.expectOne((r) => r.url === '/v1/master-data/localities' && r.params.get('cityId') === 'city1').flush([
      { id: 'loc1', cityId: 'city1', name: 'Hiran Magri', isActive: true },
    ]);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.form.cityId).toBe('city1');
    expect(locality.disabled).toBe(false);
    expect(locality.textContent).toContain('Hiran Magri');
  });

  it('registers a business with its location, then uploads its photo against it', () => {
    load([]);

    component.form.name = 'Shah Sweets';
    component.form.categoryId = 'cat1';
    component.form.cityId = 'city1';
    component.form.addressLine = 'Shop 4, Bapu Bazaar';
    const photo = new File(['x'], 'shop.png', { type: 'image/png' });
    component.choosePhoto({ target: { files: [photo] } } as unknown as Event);
    component.create();

    const request = http.expectOne('/v1/businesses/mine');

    expect(request.request.body).toEqual(expect.objectContaining({
      name: 'Shah Sweets',
      cityId: 'city1',
      addressLine: 'Shop 4, Bapu Bazaar',
    }));

    request.flush(business({ id: 'b9' }));

    const upload = http.expectOne('/v1/businesses/mine/b9/photo');

    expect((upload.request.body as FormData).get('file')).toBe(photo);
    upload.flush(business({ id: 'b9' }));

    http.expectOne('/v1/businesses/mine').flush([business({ id: 'b9' })]);
    fixture.detectChanges();

    expect(text()).toContain('Shah Sweets is registered, with its photo.');
    expect(component.photo).toBeNull();
  });

  it('keeps the business when only its photo fails, and says where to add it', () => {
    load([]);

    component.form.name = 'Shah Sweets';
    component.form.categoryId = 'cat1';
    component.choosePhoto({ target: { files: [new File(['x'], 'big.png')] } } as unknown as Event);
    component.create();

    http.expectOne('/v1/businesses/mine').flush(business());
    http
      .expectOne('/v1/businesses/mine/b1/photo')
      .flush({ title: 'A photo has to be 2 MB or smaller.' }, { status: 413, statusText: 'Payload Too Large' });
    http.expectOne('/v1/businesses/mine').flush([business()]);
    fixture.detectChanges();

    expect(text()).toContain('Shah Sweets is registered.');
    expect(text()).toContain('Add it from Manage');
  });

  it('shows the refusal rather than pretending it worked', () => {
    load([]);

    component.form.name = 'Shah Sweets';
    component.form.categoryId = 'cat1';
    component.create();

    http
      .expectOne('/v1/businesses/mine')
      .flush({ title: 'Validation', detail: 'One or more validation errors occurred.' }, {
        status: 400,
        statusText: 'Bad Request',
      });
    fixture.detectChanges();

    expect(text()).toContain('validation errors');
  });
});
