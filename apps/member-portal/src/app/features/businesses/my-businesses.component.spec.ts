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
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
  }

  function load(found: Business[]): void {
    fixture.detectChanges();
    settleLookups();
    http.expectOne('/v1/businesses/mine').flush(found);
    fixture.detectChanges();
  }

  it('lists the caller own businesses', () => {
    load([business()]);

    expect(text()).toContain('Shah Sweets');
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

  it('registers a business and reloads the list', () => {
    load([]);

    component.form.name = 'Shah Sweets';
    component.form.categoryId = 'cat1';
    component.create();

    const request = http.expectOne('/v1/businesses/mine');
    expect(request.request.body.name).toBe('Shah Sweets');

    request.flush(business());
    http.expectOne('/v1/businesses/mine').flush([business()]);
    fixture.detectChanges();

    expect(text()).toContain('Shah Sweets');
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
