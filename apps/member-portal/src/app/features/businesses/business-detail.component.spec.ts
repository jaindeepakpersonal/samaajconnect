import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { BusinessDetailComponent } from './business-detail.component';
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
    products: [
      { id: 'p1', name: 'Kaju Katli', description: '500g box', priceInPaise: 45000, isAvailable: true },
    ],
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

    fixture = TestBed.createComponent(BusinessDetailComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  }

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(found: Business = business()): void {
    fixture.detectChanges();
    http.expectOne('/v1/businesses/b1').flush(found);
    fixture.detectChanges();
  }

  it('shows the business and its products to a buyer', () => {
    configure('buyer');
    load();

    expect(text()).toContain('Shah Sweets');
    expect(text()).toContain('Kaju Katli');
  });

  it('hides the order form from the business own owner', () => {
    configure('owner');
    load();

    expect(text()).toContain('your own business');
    expect(component.isOwner()).toBe(true);
  });

  it('computes the running total from chosen quantities', () => {
    configure('buyer');
    load();

    component.setQuantity('p1', 2);

    expect(component.runningTotal()).toBe(90000);
  });

  it('places an order with only the products given a quantity', () => {
    configure('buyer');
    load(business({
      products: [
        { id: 'p1', name: 'Kaju Katli', description: null, priceInPaise: 45000, isAvailable: true },
        { id: 'p2', name: 'Barfi', description: null, priceInPaise: 30000, isAvailable: true },
      ],
    }));

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
