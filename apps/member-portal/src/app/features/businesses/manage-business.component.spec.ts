import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { API_CONFIG } from '@samaajconnect/shared';
import { ManageBusinessComponent } from './manage-business.component';
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
    localityId: 'loc1',
    addressLine: 'Shop 4, Bapu Bazaar',
    photoUrl: null,
    description: null,
    contactPhone: null,
    contactEmail: null,
    isActive: true,
    products: [product()],
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

    URL.createObjectURL = () => 'blob:test';
    URL.revokeObjectURL = () => undefined;

    fixture = TestBed.createComponent(ManageBusinessComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const text = () => fixture.nativeElement.textContent as string;

  const buttonSaying = (label: string) =>
    [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((b) =>
      b.textContent?.trim().startsWith(label),
    );

  function showTab(label: string): void {
    [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="tab"]')]
      .find((t) => t.textContent?.trim().startsWith(label))!
      .click();
    fixture.detectChanges();
    settlePhotos();
  }

  /** Flushes the photo fetches `scAuthedSrc` makes. */
  function settlePhotos(): void {
    for (const request of http.match((r) => r.url.includes('/photo') && r.method === 'GET')) {
      request.flush(new Blob(['x']));
    }

    fixture.detectChanges();
  }

  function settleCities(): void {
    for (const request of http.match((r) => r.url === '/v1/master-data/cities')) {
      request.flush([{ id: 'city1', countryId: 'in', name: 'Udaipur', isActive: true }]);
    }
  }

  function settleLocalities(): void {
    for (const request of http.match((r) => r.url === '/v1/master-data/localities')) {
      request.flush([{ id: 'loc1', cityId: 'city1', name: 'Hiran Magri', isActive: true }]);
    }
  }

  function load(found: Business = business()): void {
    fixture.detectChanges();
    http
      .expectOne((r) => r.url === '/v1/master-data/lookup-items' && r.params.get('category') === 'BusinessCategory')
      .flush([{ id: 'cat1', category: 'BusinessCategory', code: 'SWEETS', label: 'Sweets', sortOrder: 1, isActive: true }]);
    settleCities();
    settleLocalities();
    http.expectOne('/v1/businesses/b1').flush(found);
    fixture.detectChanges();
    settlePhotos();
  }

  it('opens on Details, showing the business as it reads - category and where it is', () => {
    load();

    expect(text()).toContain('Sweets');
    expect(text()).toContain('Shop 4, Bapu Bazaar · Hiran Magri, Udaipur');
    expect(fixture.nativeElement.querySelector('#business-tab-details').getAttribute('aria-selected')).toBe('true');
    expect(fixture.nativeElement.querySelector('.product-grid')).toBeNull();
  });

  it('edits the details in a popup, with the address line and location', () => {
    load();

    buttonSaying('Edit details')!.click();
    fixture.detectChanges();
    settleCities();
    settleLocalities();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();
    expect(component.form.addressLine).toBe('Shop 4, Bapu Bazaar');

    component.form.addressLine = 'Shop 9, Bapu Bazaar';
    component.save();

    const call = http.expectOne('/v1/businesses/mine/b1');

    expect(call.request.method).toBe('PATCH');
    expect(call.request.body).toEqual(expect.objectContaining({ addressLine: 'Shop 9, Bapu Bazaar', cityId: 'city1', localityId: 'loc1' }));

    call.flush(business({ addressLine: 'Shop 9, Bapu Bazaar' }));
    fixture.detectChanges();

    expect(component.popup()).toBeNull();
    expect(text()).toContain('Details saved.');
  });

  it('uploads and removes the business photo', () => {
    load();

    const file = new File(['x'], 'shop.png', { type: 'image/png' });
    component.uploadPhoto({ target: { files: [file], value: 'shop.png' } } as unknown as Event);

    const upload = http.expectOne('/v1/businesses/mine/b1/photo');

    expect(upload.request.method).toBe('POST');
    expect((upload.request.body as FormData).get('file')).toBe(file);

    upload.flush(business({ photoUrl: '/v1/businesses/b1/photo?v=abc' }));
    fixture.detectChanges();
    settlePhotos();

    buttonSaying('Remove the photo')!.click();
    const removal = http.expectOne('/v1/businesses/mine/b1/photo');

    expect(removal.request.method).toBe('DELETE');
    removal.flush(business());
    fixture.detectChanges();

    expect(text()).toContain('Photo removed.');
  });

  it('confirms deactivating and keeps the trigger', () => {
    load();

    buttonSaying('Deactivate')!.click();
    fixture.detectChanges();

    expect(text()).toContain('this can be undone at any time');
    expect(buttonSaying('Deactivate')!.getAttribute('aria-expanded')).toBe('true');

    buttonSaying('Deactivate Shah Sweets')!.click();
    const call = http.expectOne('/v1/businesses/mine/b1/status');

    expect(call.request.body).toEqual({ active: false });
    call.flush(business({ isActive: false }));
    fixture.detectChanges();

    expect(text()).toContain('Reactivate');
  });

  it('shows the products as tiles, with their images, on the Products tab', () => {
    load(business({
      products: [product({ photoUrl: '/v1/businesses/b1/products/p1/photo?v=1' }), product({ id: 'p2', name: 'Besan Ladoo', isAvailable: false })],
    }));

    showTab('Products');

    expect(fixture.nativeElement.querySelectorAll('.product-card').length).toBe(2);
    expect(fixture.nativeElement.querySelector('.product-card img.product-image')).not.toBeNull();
    expect(text()).toContain('₹450');
    expect(text()).toContain('Off sale');
  });

  it('adds a product in a popup, then uploads its image against the new product', () => {
    load();
    showTab('Products');

    buttonSaying('Add a product')!.click();
    fixture.detectChanges();

    component.productForm.name = 'Besan Ladoo';
    component.productPriceInput = '300';
    const image = new File(['x'], 'ladoo.png', { type: 'image/png' });
    component.chooseProductPhoto({ target: { files: [image] } } as unknown as Event);
    component.saveProduct(component.business()!);

    const add = http.expectOne('/v1/businesses/mine/b1/products');

    expect(add.request.body).toEqual({ name: 'Besan Ladoo', description: null, priceInPaise: 30000 });
    add.flush(business({ products: [product(), product({ id: 'p2', name: 'Besan Ladoo', priceInPaise: 30000 })] }));

    const upload = http.expectOne('/v1/businesses/mine/b1/products/p2/photo');

    expect((upload.request.body as FormData).get('file')).toBe(image);
    upload.flush(business());
    fixture.detectChanges();
    settlePhotos();

    expect(component.popup()).toBeNull();
    expect(text()).toContain('Besan Ladoo added, with its image.');
  });

  it('refuses a product with no price before asking', () => {
    load();
    component.openProduct(null);
    component.productForm.name = 'Besan Ladoo';
    component.productPriceInput = '';
    component.saveProduct(component.business()!);

    http.expectNone('/v1/businesses/mine/b1/products');
    expect(component.error()).not.toBeNull();
  });

  it('edits a product and removes its image from the same popup', () => {
    load(business({ products: [product({ photoUrl: '/v1/businesses/b1/products/p1/photo?v=1' })] }));
    showTab('Products');

    buttonSaying('Edit')!.click();
    fixture.detectChanges();
    settlePhotos();

    expect(component.productPriceInput).toBe('450');

    buttonSaying('Remove the image')!.click();
    const removal = http.expectOne('/v1/businesses/mine/b1/products/p1/photo');

    expect(removal.request.method).toBe('DELETE');
    removal.flush(business());
    fixture.detectChanges();
  });

  it('takes a product off sale from its tile', () => {
    load();
    showTab('Products');

    buttonSaying('Take off sale')!.click();
    const call = http.expectOne('/v1/businesses/mine/b1/products/p1/availability');

    expect(call.request.body).toEqual({ available: false });
    call.flush(business({ products: [product({ isAvailable: false })] }));
    fixture.detectChanges();

    expect(text()).toContain('Put back on sale');
  });
});
