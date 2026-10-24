import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { BusinessDetailComponent } from './business-detail.component';
import { Business, Product } from './businesses.models';

function product(overrides: Partial<Product> = {}): Product {
  return {
    id: 'p1',
    name: 'Kaju Katli',
    description: '500g box',
    priceInPaise: 45000,
    isAvailable: true,
    photoUrl: null,
    ...overrides,
  };
}

function business(overrides: Partial<Business> = {}): Business {
  return {
    id: 'b1',
    ownerMemberId: 'owner',
    name: 'Shah Sweets',
    categoryId: 'cat1',
    cityId: 'city1',
    localityId: null,
    addressLine: 'Shop 4, Bapu Bazaar',
    photoUrl: null,
    description: 'Traditional sweets.',
    contactPhone: '9876543210',
    contactEmail: 'shop@example.com',
    isActive: true,
    products: [product()],
    createdAt: new Date().toISOString(),
    updatedAt: null,
    ...overrides,
  };
}

function authAs(userId: string) {
  return { user: () => ({ userId }) } as unknown as AuthService;
}

describe('BusinessDetailComponent', () => {
  let fixture: ComponentFixture<BusinessDetailComponent>;
  let component: BusinessDetailComponent;
  let http: HttpTestingController;

  function configure(currentMemberId: string): void {
    TestBed.configureTestingModule({
      imports: [BusinessDetailComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'b1' } } } },
        { provide: AuthService, useValue: authAs(currentMemberId) },
      ],
    });

    URL.createObjectURL = () => 'blob:test';
    URL.revokeObjectURL = () => undefined;

    fixture = TestBed.createComponent(BusinessDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  }

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  const buttonSaying = (label: string) =>
    [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((b) =>
      b.textContent?.trim().startsWith(label),
    );

  function load(found: Business = business()): void {
    fixture.detectChanges();
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'BusinessCategory')
      .flush([{ id: 'cat1', category: 'BusinessCategory', code: 'SWEETS', label: 'Sweets', sortOrder: 1, isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush([{ id: 'city1', countryId: 'in', name: 'Udaipur', isActive: true }]);
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
    http.expectOne('/v1/businesses/b1').flush(found);
    http.expectOne('/v1/members/owner').flush({ id: 'owner', fullName: 'Mahendra Shah', photoUrl: null });
    fixture.detectChanges();

    for (const request of http.match((r) => r.url.includes('/photo') && r.method === 'GET')) {
      request.flush(new Blob(['x']));
    }

    fixture.detectChanges();
  }

  it('shows the shop front: photo, category, where it is, who runs it and how to reach them', () => {
    configure('buyer');
    load(business({ photoUrl: '/v1/businesses/b1/photo?v=1' }));

    expect(fixture.nativeElement.querySelector('img.biz-hero-photo')).not.toBeNull();
    expect(text()).toContain('Sweets');
    expect(text()).toContain('Shop 4, Bapu Bazaar · Udaipur');
    expect(text()).toContain('Run by Mahendra Shah');
    expect(fixture.nativeElement.querySelector('a[href="tel:9876543210"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="mailto:shop@example.com"]')).not.toBeNull();
  });

  it('lists products as tiles with their image and price', () => {
    configure('buyer');
    load(business({ products: [product({ photoUrl: '/v1/businesses/b1/products/p1/photo?v=1' })] }));

    const tile = fixture.nativeElement.querySelector('.product-card') as HTMLElement;

    expect(tile.querySelector('img.product-image')).not.toBeNull();
    expect(tile.textContent).toContain('Kaju Katli');
    expect(tile.textContent).toContain('₹450');
  });

  it('turns Add into a stepper and keeps the order panel in step', () => {
    configure('buyer');
    load();

    expect(text()).toContain('Nothing yet');

    buttonSaying('Add')!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.stepper-count').textContent.trim()).toBe('1');

    buttonSaying('+')!.click();
    fixture.detectChanges();

    expect(component.runningTotal()).toBe(90000);
    expect(fixture.nativeElement.querySelector('.cart-lines').textContent).toContain('Kaju Katli × 2');
    expect(fixture.nativeElement.querySelector('.cart-total').textContent).toContain('₹900');

    buttonSaying('−')!.click();
    buttonSaying('−')!.click();
    fixture.detectChanges();

    expect(component.lines()).toEqual([]);
    expect(buttonSaying('Add')).toBeDefined();
  });

  it('hides the order panel and the Add buttons from the business own owner', () => {
    configure('owner');
    load();

    expect(text()).toContain('your own business');
    expect(component.isOwner()).toBe(true);
    expect(fixture.nativeElement.querySelector('.cart')).toBeNull();
    expect(buttonSaying('Add')).toBeUndefined();
  });

  it('places an order with only the products given a quantity', () => {
    configure('buyer');
    load(business({ products: [product(), product({ id: 'p2', name: 'Barfi', priceInPaise: 30000 })] }));

    component.setQuantity('p1', 2);
    component.note = '  Please pack well.  ';
    component.place('b1');

    const request = http.expectOne('/v1/businesses/orders');

    expect(request.request.body).toEqual({
      businessId: 'b1',
      items: [{ productId: 'p1', quantity: 2 }],
      note: 'Please pack well.',
    });
  });

  it('confirms the order was sent', () => {
    configure('buyer');
    load();

    component.setQuantity('p1', 1);
    component.place('b1');

    http.expectOne('/v1/businesses/orders').flush({
      id: 'o1',
      businessId: 'b1',
      businessName: 'Shah Sweets',
      buyerMemberId: 'buyer',
      status: 'Pending',
      items: [],
      totalAmountInPaise: 45000,
      note: null,
      placedAt: new Date().toISOString(),
      respondedAt: null,
      closedAt: null,
    });
    fixture.detectChanges();

    expect(text()).toContain('Your order has been sent');
  });

  it('shows the refusal rather than pretending it worked', () => {
    configure('buyer');
    load();

    component.setQuantity('p1', 1);
    component.place('b1');

    http
      .expectOne('/v1/businesses/orders')
      .flush({ title: 'Conflict', detail: 'One or more products in this order are no longer available.' }, {
        status: 409,
        statusText: 'Conflict',
      });
    fixture.detectChanges();

    expect(text()).toContain('no longer available');
  });
});
