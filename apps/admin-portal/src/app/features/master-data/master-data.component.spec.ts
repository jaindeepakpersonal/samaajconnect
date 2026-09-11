import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { MasterDataComponent } from './master-data.component';
import { AdminScope } from '../../core/admin-scope';
import { City, Gautra, LookupItem } from '../../core/admin.models';

function country(overrides: Partial<LookupItem> = {}): LookupItem {
  return {
    id: 'country-1',
    category: 'Country',
    code: 'IN',
    label: 'India',
    sortOrder: 0,
    isActive: true,
    ...overrides,
  };
}

function city(overrides: Partial<City> = {}): City {
  return { id: 'city-1', countryId: 'country-1', name: 'Udaipur', isActive: true, ...overrides };
}

function gautra(overrides: Partial<Gautra> = {}): Gautra {
  return { id: 'g1', name: 'Kataria', isActive: true, ...overrides };
}

/** Stands in for the real services so the role and the chosen Samaaj can be set per test. */
function configure(roles: string[], tenantId: string | null) {
  TestBed.configureTestingModule({
    imports: [MasterDataComponent],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      { provide: AuthService, useValue: { roles: () => roles } },
      {
        provide: AdminScope,
        useValue: { tenantId: () => tenantId, label: () => 'Mahavir Samaaj' },
      },
    ],
  });
}

describe('MasterDataComponent', () => {
  let fixture: ComponentFixture<MasterDataComponent>;
  let component: MasterDataComponent;
  let http: HttpTestingController;

  function build() {
    fixture = TestBed.createComponent(MasterDataComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  }

  const text = () => fixture.nativeElement.textContent as string;

  /** A Super Admin's `ngOnInit`: the platform lists, the country dropdown, and Gautra if scoped. */
  function loadAsSuperAdmin(scoped: boolean) {
    fixture.detectChanges();

    http
      .expectOne('/v1/master-data/lookup-items?category=Country&includeInactive=true')
      .flush([country()]);
    http
      .expectOne('/v1/master-data/lookup-items?category=Country&includeInactive=false')
      .flush([country()]);

    if (scoped) {
      http.expectOne('/v1/master-data/gautras?includeInactive=true').flush([]);
    }

    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('offers a Samaaj Admin only the Gautra section', () => {
    configure(['SamaajAdmin'], null);
    build();
    fixture.detectChanges();

    http.expectOne('/v1/master-data/gautras?includeInactive=true').flush([gautra()]);
    fixture.detectChanges();

    expect(text()).not.toContain('Lookup lists');
    expect(text()).not.toContain('Cities');
    expect(text()).toContain('Gautra — Mahavir Samaaj');
    expect(text()).toContain('Kataria');
  });

  it('tells an unscoped Super Admin a Gautra list belongs to a Samaaj', () => {
    // CreateGautraCommand has nowhere to put a Gautra with no scope chosen -
    // the same rule the Pathshala create form already applies.
    configure(['SuperAdmin'], null);
    build();
    loadAsSuperAdmin(false);

    expect(text()).toContain('Choose one in the top bar');
    expect(component.canManageGautra()).toBe(false);
  });

  it('gives a Super Admin scoped into a Samaaj the Gautra section too', () => {
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    expect(component.canManageGautra()).toBe(true);
    expect(text()).toContain('Gautra — Mahavir Samaaj');
  });

  it('shows the platform-wide lists to a Super Admin', () => {
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    expect(text()).toContain('Lookup lists');
    expect(text()).toContain('India');
    expect(text()).toContain('Cities');
  });

  it('adds a lookup item to the selected category, sending no blanks around it', () => {
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    component.newCode = ' US ';
    component.newLabel = ' United States ';
    component.newSortOrder = 1;
    component.createLookupItem();

    const call = http.expectOne('/v1/master-data/lookup-items');

    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual({
      category: 'Country',
      code: 'US',
      label: 'United States',
      sortOrder: 1,
    });

    call.flush(country({ id: 'country-2', code: 'US', label: 'United States' }));
    http
      .expectOne('/v1/master-data/lookup-items?category=Country&includeInactive=true')
      .flush([country(), country({ id: 'country-2', code: 'US', label: 'United States' })]);
    fixture.detectChanges();

    expect(text()).toContain('United States added.');
  });

  it('keeps the Edit trigger in the DOM instead of swapping it away', () => {
    // This app's own CLAUDE.md documents the bug this guards against: a
    // confirmation that replaces its own trigger drops keyboard focus to the
    // body (WCAG 2.4.3). Toggling edit mode must not remove the button.
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    component.toggleEditLookup(country());
    fixture.detectChanges();

    const buttons = Array.from(
      fixture.nativeElement.querySelectorAll('button'),
    ) as HTMLButtonElement[];

    expect(buttons.some((b) => b.textContent?.trim() === 'Edit')).toBe(true);
    expect(component.editingLookupId()).toBe('country-1');
  });

  it('loads a country\'s cities once one is chosen', () => {
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    component.onCountryChange('country-1');

    http
      .expectOne('/v1/master-data/cities?includeInactive=true&countryId=country-1')
      .flush([city()]);
    fixture.detectChanges();

    expect(text()).toContain('Udaipur');
  });

  it('asks before deactivating a city, and there is no bring-back', () => {
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    component.onCountryChange('country-1');
    http
      .expectOne('/v1/master-data/cities?includeInactive=true&countryId=country-1')
      .flush([city()]);
    fixture.detectChanges();

    component.askDeactivateCity('city-1');
    fixture.detectChanges();

    expect(text()).toContain('There is no undo');

    component.confirmDeactivateCity(city());

    const call = http.expectOne('/v1/master-data/cities/city-1/deactivate');
    expect(call.request.method).toBe('POST');
    call.flush(city({ isActive: false }));

    http
      .expectOne('/v1/master-data/cities?includeInactive=true&countryId=country-1')
      .flush([city({ isActive: false })]);
    fixture.detectChanges();

    expect(text()).toContain('Udaipur deactivated.');
    expect(text()).not.toContain('Bring back');
  });
});
