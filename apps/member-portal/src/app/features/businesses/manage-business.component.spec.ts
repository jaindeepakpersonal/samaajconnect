import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { ManageBusinessComponent } from './manage-business.component';
import { Business } from './businesses.models';

function business(overrides: Partial<Business> = {}): Business {
  return {
    id: 'b1',
    ownerMemberId: 'owner',
    name: 'Shah Sweets',
    categoryId: 'cat1',
    cityId: null,
    localityId: null,
    description: null,
    contactPhone: null,
    contactEmail: null,
    isActive: true,
    products: [
      { id: 'p1', name: 'Kaju Katli', description: '500g box', priceInPaise: 45000, isAvailable: true },
    ],
    createdAt: new Date().toISOString(),
    updatedAt: null,
    ...overrides,
  };
}

describe('ManageBusinessComponent', () => {
  let fixture: ComponentFixture<ManageBusinessComponent>;
  let component: ManageBusinessComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ManageBusinessComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'b1' } } } },
      ],
    });

    fixture = TestBed.createComponent(ManageBusinessComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function settleLookups(): void {
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'BusinessCategory')
      .flush([{ id: 'cat1', category: 'BusinessCategory', code: 'SWEETS', label: 'Sweets', sortOrder: 1, isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
  }

  function load(found: Business = business()): void {
    fixture.detectChanges();
    settleLookups();
    http.expectOne('/v1/businesses/b1').flush(found);
    fixture.detectChanges();
  }

  it('shows the business and its products', () => {
    load();

    expect(text()).toContain('Shah Sweets');
    expect(text()).toContain('Kaju Katli');
  });

  it('saves an edit to the business fields', () => {
    load();

    component.form.name = 'Renamed Sweets';
    component.save();

    const request = http.expectOne('/v1/businesses/mine/b1');
    expect(request.request.body.name).toBe('Renamed Sweets');

    request.flush(business({ name: 'Renamed Sweets' }));
    fixture.detectChanges();

    expect(text()).toContain('Renamed Sweets');
  });

  it('asks first before deactivating, then keeps the trigger reachable', () => {
    load();

    component.confirmingStatus.set(true);
    fixture.detectChanges();

    expect(text()).toContain('kept, and this can be undone');

    component.setActive(false);

    http.expectOne('/v1/businesses/mine/b1/status').flush(business({ isActive: false }));
    fixture.detectChanges();

    expect(text()).toContain('Inactive');
  });

  it('adds a product from the price typed in rupees', () => {
    load();

    component.productForm.name = 'Barfi';
    component.productPriceInput = '300';
    component.saveProduct('b1');

    const request = http.expectOne('/v1/businesses/mine/b1/products');
    expect(request.request.body).toEqual({ name: 'Barfi', description: null, priceInPaise: 30000 });

    request.flush(business({
      products: [
        ...business().products,
        { id: 'p2', name: 'Barfi', description: null, priceInPaise: 30000, isAvailable: true },
      ],
    }));
    fixture.detectChanges();

    expect(text()).toContain('Barfi');
  });

  it('refuses to submit a product with no price', () => {
    load();

    component.productForm.name = 'Barfi';
    component.productPriceInput = '';
    component.saveProduct('b1');

    expect(component.productError()).not.toBeNull();
  });

  it('toggles a product off sale and back on', () => {
    load();

    component.toggleAvailability(business().products[0]!);

    const request = http.expectOne('/v1/businesses/mine/b1/products/p1/availability');
    expect(request.request.body).toEqual({ available: false });

    request.flush(business({
      products: [{ id: 'p1', name: 'Kaju Katli', description: '500g box', priceInPaise: 45000, isAvailable: false }],
    }));
    fixture.detectChanges();

    expect(text()).toContain('Put back on sale');
  });
});
