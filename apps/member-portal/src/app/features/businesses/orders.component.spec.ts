import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { BusinessesOrdersComponent } from './orders.component';
import { Order } from './businesses.models';

function order(overrides: Partial<Order> = {}): Order {
  return {
    id: 'o1',
    businessId: 'b1',
    businessName: 'Shah Sweets',
    buyerMemberId: 'buyer',
    status: 'Pending',
    items: [{ productId: 'p1', productName: 'Kaju Katli', unitPriceInPaise: 45000, quantity: 2 }],
    totalAmountInPaise: 90000,
    note: null,
    placedAt: new Date().toISOString(),
    respondedAt: null,
    closedAt: null,
    ...overrides,
  };
}

describe('BusinessesOrdersComponent', () => {
  let fixture: ComponentFixture<BusinessesOrdersComponent>;
  let component: BusinessesOrdersComponent;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BusinessesOrdersComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(BusinessesOrdersComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  function load(placed: Order[], received: Order[]): void {
    fixture.detectChanges();
    http.expectOne('/v1/businesses/orders/mine').flush(placed);
    http.expectOne('/v1/businesses/orders/received').flush(received);
    fixture.detectChanges();
  }

  it('shows the Placed tab first', () => {
    load([order()], []);

    expect(component.activeTab()).toBe('placed');
    expect(text()).toContain('Shah Sweets');
  });

  it('switches to Received', () => {
    load([], [order({ id: 'o2' })]);

    component.activeTab.set('received');
    fixture.detectChanges();

    expect(text()).toContain('Shah Sweets');
  });

  it('offers Cancel only on a Placed order that is still Pending', () => {
    load([order({ status: 'Fulfilled' })], []);

    expect(text()).not.toContain('Cancel');
  });

  it('offers Accept/Decline on a Received order that is Pending, and accepts', () => {
    load([], [order()]);

    component.activeTab.set('received');
    fixture.detectChanges();

    component.respond('o1', true);

    const request = http.expectOne('/v1/businesses/orders/o1/respond');
    expect(request.request.body).toEqual({ accept: true });

    request.flush(order({ status: 'Accepted' }));
    http.expectOne('/v1/businesses/orders/mine').flush([]);
    http.expectOne('/v1/businesses/orders/received').flush([order({ status: 'Accepted' })]);
    fixture.detectChanges();

    expect(text()).toContain('Accepted');
  });

  it('offers Fulfill/Cancel on a Received order that is Accepted, and fulfills', () => {
    load([], [order({ status: 'Accepted' })]);

    component.activeTab.set('received');
    fixture.detectChanges();

    component.fulfill('o1');

    http.expectOne('/v1/businesses/orders/o1/fulfill').flush(order({ status: 'Fulfilled' }));
    http.expectOne('/v1/businesses/orders/mine').flush([]);
    http.expectOne('/v1/businesses/orders/received').flush([order({ status: 'Fulfilled' })]);
    fixture.detectChanges();

    expect(text()).toContain('Fulfilled');
  });

  it('cancels a placed pending order', () => {
    load([order()], []);

    component.cancel('o1');

    http.expectOne('/v1/businesses/orders/o1/cancel').flush(order({ status: 'Cancelled' }));
    http.expectOne('/v1/businesses/orders/mine').flush([order({ status: 'Cancelled' })]);
    http.expectOne('/v1/businesses/orders/received').flush([]);
    fixture.detectChanges();

    expect(text()).toContain('Cancelled');
  });

  it('keeps a failed action off the page-level error', () => {
    load([order()], []);

    component.cancel('o1');

    http
      .expectOne('/v1/businesses/orders/o1/cancel')
      .flush({ title: 'Conflict', detail: 'This order has already moved on.' }, {
        status: 409,
        statusText: 'Conflict',
      });
    fixture.detectChanges();

    expect(component.error()).toBeNull();
    expect(text()).toContain('already moved on');
  });
});
