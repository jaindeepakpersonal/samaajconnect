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

  /**
   * A Super Admin's `ngOnInit`: the default Country tab's items, the country
   * dropdown shared by Cities/Localities, and Gautra if scoped.
   */
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

  it('offers a Samaaj Admin only the Gautra tab', () => {
    configure(['SamaajAdmin'], null);
    build();
    fixture.detectChanges();

    http.expectOne('/v1/master-data/gautras?includeInactive=true').flush([gautra()]);
    fixture.detectChanges();

    const tabs = Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]')) as HTMLElement[];

    expect(tabs.map((t) => t.textContent?.trim())).toEqual(['Gautra']);
    expect(text()).toContain('Gautra — Mahavir Samaaj');
    expect(text()).toContain('Kataria');
  });

  it('tells an unscoped Super Admin a Gautra list belongs to a Samaaj', () => {
    // CreateGautraCommand has nowhere to put a Gautra with no scope chosen -
    // the same rule the Pathshala create form already applies.
    configure(['SuperAdmin'], null);
    build();
    loadAsSuperAdmin(false);

    component.selectTab('Gautra');
    fixture.detectChanges();

    expect(text()).toContain('Choose one in the top bar');
    expect(component.canManageGautra()).toBe(false);
  });

  it('gives a Super Admin scoped into a Samaaj the Gautra tab too', () => {
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    component.selectTab('Gautra');
    fixture.detectChanges();

    expect(component.canManageGautra()).toBe(true);
    expect(text()).toContain('Gautra — Mahavir Samaaj');
  });

  it('shows all nine tabs to a Super Admin, one type of data at a time', () => {
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    const tabs = Array.from(fixture.nativeElement.querySelectorAll('[role="tab"]')) as HTMLElement[];

    expect(tabs.map((t) => t.textContent?.trim())).toEqual([
      'Country',
      'Profession',
      'Gender',
      'Marital status',
      'Qualification',
      'Business category',
      'Cities',
      'Localities',
      'Gautra',
    ]);

    // The default tab is Country, so its items show and nothing from another
    // tab does - one type of master data at a time, not everything stacked.
    expect(text()).toContain('India');
    expect(text()).not.toContain('Choose a country to see its cities.');
  });

  it('adds a lookup item to the active tab through the modal, sending no blanks around it', () => {
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    component.openAddLookup(new MouseEvent('click'));
    fixture.detectChanges();

    expect(component.modalOpen()).toBe(true);
    expect(component.modalMode()).toBe('add');

    component.lookupFormCode = ' US ';
    component.lookupFormLabel = ' United States ';
    component.lookupFormSortOrder = 1;
    component.submitLookupModal();

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

    expect(component.modalOpen()).toBe(false);
    expect(text()).toContain('United States added.');
  });

  it('opens the edit modal pre-filled, and Code is fixed rather than editable', () => {
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    component.openEditLookup(country(), new MouseEvent('click'));
    fixture.detectChanges();

    expect(component.modalMode()).toBe('edit');
    expect(component.lookupFormCode).toBe('IN');
    expect(component.lookupFormLabel).toBe('India');
    expect(fixture.nativeElement.querySelector('#modal-code')).toBeNull();
    expect(text()).toContain('Code: IN (fixed once created)');
  });

  it('returns focus to the button that opened the modal once it closes', () => {
    // The same "don't drop keyboard focus" rule this app already holds every
    // confirmation panel to (root CLAUDE.md's accessibility notes).
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    const trigger = document.createElement('button');
    document.body.appendChild(trigger);
    trigger.focus();

    component.openAddLookup({ currentTarget: trigger } as unknown as MouseEvent);
    fixture.detectChanges();

    component.closeModal();

    expect(component.modalOpen()).toBe(false);

    return fixture.whenStable().then(() => {
      expect(document.activeElement).toBe(trigger);
      trigger.remove();
    });
  });

  it("loads a country's cities once one is chosen, on the Cities tab", () => {
    configure(['SuperAdmin'], 't1');
    build();
    loadAsSuperAdmin(true);

    component.selectTab('Cities');
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

    component.selectTab('Cities');
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
