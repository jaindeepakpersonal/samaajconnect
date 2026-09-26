import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { Pathshala } from '../../core/admin.models';
import { PathshalaDialogComponent } from './pathshala-dialog.component';

const COUNTRIES = '/v1/master-data/lookup-items?category=Country&includeInactive=false';

function pathshala(overrides: Partial<Pathshala> = {}): Pathshala {
  return {
    id: 'p1',
    name: 'Shri Mahavir Jain Pathshala',
    address: { line: 'Hiran Magri', countryId: 'in', stateId: null, cityId: 'udr', localityId: 'loc-hm' },
    contactPerson: 'Smt. Kavita Jain',
    status: 'Active',
    currentSessionLabel: null,
    currentSessionId: null,
    classCount: 0,
    teacherCount: 0,
    acceptsEnrolments: false,
    ...overrides,
  };
}

describe('PathshalaDialogComponent', () => {
  let fixture: ComponentFixture<PathshalaDialogComponent>;
  let component: PathshalaDialogComponent;
  let http: HttpTestingController;
  let saved: string[];

  function build(existing: Pathshala | null = null, areaLocalityId: string | null = null) {
    TestBed.configureTestingModule({
      imports: [PathshalaDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
        { provide: AuthService, useValue: { user: () => ({ areaLocalityId, permissions: [] }) } },
      ],
    });

    fixture = TestBed.createComponent(PathshalaDialogComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('pathshala', existing);
    fixture.componentRef.setInput('samaajLabel', 'Mahavir Samaaj');
    saved = [];
    component.saved.subscribe((message) => saved.push(message));
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('creates a Pathshala, sending no blanks', () => {
    build();
    http.expectOne(COUNTRIES).flush([]);

    component.name = '  Shri Mahavir Jain Pathshala  ';
    component.addressLine = '   ';
    component.contactPerson = 'Smt. Kavita Jain';
    component.save();

    const call = http.expectOne('/v1/pathshala/pathshalas');

    expect(call.request.method).toBe('POST');
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

    expect(saved[0]).toContain('Open it to start a session');
  });

  it('will not create one without a name', () => {
    build();
    http.expectOne(COUNTRIES).flush([]);

    component.name = '   ';
    component.save();

    http.expectNone('/v1/pathshala/pathshalas');
  });

  it('cascades Country -> State/City -> Locality', () => {
    build();
    http.expectOne(COUNTRIES).flush([]);

    component.onCountryChange('country-1');
    http
      .expectOne('/v1/master-data/states?includeInactive=false&countryId=country-1')
      .flush([{ id: 'state-1', countryId: 'country-1', name: 'Rajasthan', isActive: true }]);
    http
      .expectOne('/v1/master-data/cities?includeInactive=false&countryId=country-1')
      .flush([{ id: 'city-1', countryId: 'country-1', stateId: null, name: 'Udaipur', isActive: true }]);

    expect(component.states().map((s) => s.name)).toEqual(['Rajasthan']);

    component.onCityChange('city-1');
    http
      .expectOne('/v1/master-data/localities?includeInactive=false&cityId=city-1')
      .flush([{ id: 'locality-1', cityId: 'city-1', name: 'Hiran Magri', isActive: true }]);

    expect(component.localities().map((l) => l.name)).toEqual(['Hiran Magri']);

    component.stateId.set('state-1');
    component.onCountryChange('');

    expect(component.stateId()).toBe('');
    expect(component.cityId()).toBe('');
    expect(component.localityId).toBe('');
  });

  it('opens an edit on the Pathshala as it is, with every level of the address loaded', () => {
    build(pathshala());

    // A select whose options have not arrived renders blank, which would read
    // as the address being empty.
    http.expectOne(COUNTRIES).flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/states').flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);

    expect(component.name).toBe('Shri Mahavir Jain Pathshala');
    expect(component.contactPerson).toBe('Smt. Kavita Jain');
    expect(component.localityId).toBe('loc-hm');

    component.name = 'Shri Parshwanath Jain Pathshala';
    component.save();

    const call = http.expectOne('/v1/pathshala/pathshalas/p1');

    expect(call.request.method).toBe('PATCH');
    expect(call.request.body.localityId).toBe('loc-hm');
    call.flush(pathshala());

    expect(saved[0]).toBe('Shri Parshwanath Jain Pathshala is saved.');
  });

  it('keeps the dialog open with what was typed when a save is refused', () => {
    build(pathshala({ address: null }));
    http.expectOne(COUNTRIES).flush([]);

    component.name = 'Something else';
    component.save();
    http
      .expectOne('/v1/pathshala/pathshalas/p1')
      .flush({ title: 'Pathshala.OutsideLocality' }, { status: 403, statusText: 'Forbidden' });

    expect(saved).toEqual([]);
    expect(component.error()).not.toBeNull();
    expect(component.name).toBe('Something else');
  });

  it("locks an Area Admin's cascade to their own locality", async () => {
    build(pathshala({ address: { line: null, countryId: 'in', stateId: null, cityId: 'udr', localityId: 'loc-hm' } }), 'loc-hm');
    http.expectOne(COUNTRIES).flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/states').flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/cities').flush([]);
    http.expectOne((r) => r.url === '/v1/master-data/localities').flush([]);
    fixture.detectChanges();
    await fixture.whenStable();

    const locality = fixture.nativeElement.querySelector('#pathshala-locality') as HTMLSelectElement;

    expect(locality.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('The locality stays yours');
  });
});
