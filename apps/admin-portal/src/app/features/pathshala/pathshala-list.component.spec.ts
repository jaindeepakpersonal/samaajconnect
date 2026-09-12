import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { PathshalaListComponent } from './pathshala-list.component';
import { AdminScope } from '../../core/admin-scope';
import { Pathshala } from '../../core/admin.models';

function pathshala(overrides: Partial<Pathshala> = {}): Pathshala {
  return {
    id: 'p1',
    name: 'Shri Mahavir Jain Pathshala',
    address: { line: 'Hiran Magri', countryId: null, stateId: null, cityId: null, localityId: null },
    contactPerson: null,
    status: 'Active',
    currentSessionLabel: '2026-27',
    currentSessionId: 's1',
    classCount: 2,
    teacherCount: 3,
    acceptsEnrolments: true,
    ...overrides,
  };
}

/**
 * Stands in for the real services so the role and the chosen Samaaj can be set
 * per test — the two facts that decide whether the create form is offered.
 */
function configure(roles: string[], tenantId: string | null) {
  TestBed.configureTestingModule({
    imports: [PathshalaListComponent],
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

describe('PathshalaListComponent', () => {
  let fixture: ComponentFixture<PathshalaListComponent>;
  let component: PathshalaListComponent;
  let http: HttpTestingController;

  function build() {
    fixture = TestBed.createComponent(PathshalaListComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  }

  const text = () => fixture.nativeElement.textContent as string;

  function load(list: Pathshala[] = []) {
    fixture.detectChanges();
    http.expectOne('/v1/pathshala/pathshalas').flush(list);
    http
      .expectOne('/v1/master-data/lookup-items?category=Country&includeInactive=false')
      .flush([]);
    fixture.detectChanges();
  }

  it('offers a Samaaj Admin no create form', () => {
    // The master record is the platform operator's. Offering the form here
    // would be offering a control that always answers 403.
    configure(['SamaajAdmin'], 't1');
    build();
    load([pathshala()]);

    expect(component.canCreate()).toBe(false);
    expect(text()).not.toContain('Create a Pathshala');

    http.verify();
  });

  it('offers a Super Admin scoped into a Samaaj the create form', () => {
    // The panel is used by Super Admins too, which is what the earlier "always
    // answers 403" note missed — and why this endpoint had no caller at all.
    configure(['SuperAdmin'], 't1');
    build();
    load([pathshala()]);

    expect(component.canCreate()).toBe(true);
    expect(text()).toContain('Create a Pathshala');

    http.verify();
  });

  it('offers a Super Admin with no Samaaj chosen nothing to create into', () => {
    // The command creates the record inside whichever Samaaj the request is
    // scoped to, so with no scope there is nowhere to put one.
    configure(['SuperAdmin'], null);
    build();
    fixture.detectChanges();
    http
      .expectOne('/v1/master-data/lookup-items?category=Country&includeInactive=false')
      .flush([]);
    fixture.detectChanges();

    expect(component.canCreate()).toBe(false);
    expect(text()).toContain('Choose one in the top bar');

    http.verify();
  });

  it('creates a Pathshala, sending no blanks', () => {
    configure(['SuperAdmin'], 't1');
    build();
    load([]);

    component.name = '  Shri Mahavir Jain Pathshala  ';
    component.addressLine = '   ';
    component.contactPerson = 'Smt. Kavita Jain';
    component.create();

    const call = http.expectOne('/v1/pathshala/pathshalas');

    expect(call.request.body).toEqual({
      name: 'Shri Mahavir Jain Pathshala',
      addressLine: null,
      countryId: null,
      stateId: null,
      cityId: null,
      localityId: null,
      contactPerson: 'Smt. Kavita Jain',
    });

    call.flush(pathshala());
    http.expectOne('/v1/pathshala/pathshalas').flush([pathshala()]);
    fixture.detectChanges();

    expect(text()).toContain('Open a session to start teaching');

    http.verify();
  });

  it('will not create one without a name', () => {
    configure(['SuperAdmin'], 't1');
    build();
    load([]);

    component.name = '   ';
    component.create();

    http.expectNone('/v1/pathshala/pathshalas');
    http.verify();
  });

  it('still explains an empty list to somebody who cannot create one', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    load([]);

    expect(text()).toContain('ask them to add one');

    http.verify();
  });

  it('cascades Country -> State/City -> Locality on the create form', () => {
    configure(['SuperAdmin'], 't1');
    build();
    load([]);

    component.onCountryChange('country-1');

    http
      .expectOne('/v1/master-data/states?includeInactive=false&countryId=country-1')
      .flush([{ id: 'state-1', countryId: 'country-1', name: 'Rajasthan', isActive: true }]);
    http
      .expectOne('/v1/master-data/cities?includeInactive=false&countryId=country-1')
      .flush([{ id: 'city-1', countryId: 'country-1', stateId: null, name: 'Udaipur', isActive: true }]);

    expect(component.states().map((s) => s.name)).toEqual(['Rajasthan']);
    expect(component.cities().map((c) => c.name)).toEqual(['Udaipur']);

    component.onCityChange('city-1');

    http
      .expectOne('/v1/master-data/localities?includeInactive=false&cityId=city-1')
      .flush([{ id: 'locality-1', cityId: 'city-1', name: 'Hiran Magri', isActive: true }]);

    expect(component.localities().map((l) => l.name)).toEqual(['Hiran Magri']);

    component.name = 'Shri Mahavir Jain Pathshala';
    component.localityId = 'locality-1';
    component.create();

    const call = http.expectOne('/v1/pathshala/pathshalas');
    expect(call.request.body).toEqual({
      name: 'Shri Mahavir Jain Pathshala',
      addressLine: null,
      countryId: 'country-1',
      stateId: null,
      cityId: 'city-1',
      localityId: 'locality-1',
      contactPerson: null,
    });
  });

  it('resets State, City and Locality when the country changes', () => {
    configure(['SuperAdmin'], 't1');
    build();
    load([]);

    component.onCountryChange('country-1');
    http
      .expectOne('/v1/master-data/states?includeInactive=false&countryId=country-1')
      .flush([]);
    http
      .expectOne('/v1/master-data/cities?includeInactive=false&countryId=country-1')
      .flush([]);

    component.stateId.set('state-1');
    component.cityId.set('city-1');
    component.localityId = 'locality-1';

    component.onCountryChange('');

    expect(component.stateId()).toBe('');
    expect(component.cityId()).toBe('');
    expect(component.localityId).toBe('');
    expect(component.states()).toEqual([]);
    expect(component.cities()).toEqual([]);
    expect(component.localities()).toEqual([]);
  });

  it('reads a 404 as the module being off', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    fixture.detectChanges();
    http.expectOne('/v1/pathshala/pathshalas').flush({}, { status: 404, statusText: 'Not Found' });
    http
      .expectOne('/v1/master-data/lookup-items?category=Country&includeInactive=false')
      .flush([]);
    fixture.detectChanges();

    expect(text()).toContain('does not run the Pathshala module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();

    http.verify();
  });
});
