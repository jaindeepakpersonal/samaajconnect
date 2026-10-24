import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { PathshalaListComponent } from './pathshala-list.component';
import { AdminScope } from '../../core/admin-scope';
import { Pathshala } from '../../core/admin.models';
import { LocalityOptions } from '../../core/locality-options';

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
 * Stands in for the real services so the role, the permissions, an Area
 * Admin's locality and the chosen Samaaj can be set per test - the facts that
 * decide which controls a tile carries.
 */
function configure(
  roles: string[],
  tenantId: string | null,
  permissions: string[] = ['Pathshala.Manage'],
  areaLocalityId: string | null = null,
) {
  TestBed.configureTestingModule({
    imports: [PathshalaListComponent],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      {
        provide: LocalityOptions,
        useValue: {
          load: () => undefined,
          options: () => [{ id: 'loc1', label: 'Hiran Magri, Udaipur' }],
          label: (id: string | null) => (id === 'loc1' ? 'Hiran Magri, Udaipur' : 'Whole Samaaj'),
          place: (localityId: string | null, cityId: string | null) =>
            localityId === 'loc1' ? 'Hiran Magri, Udaipur' : cityId === 'city1' ? 'Bhinder' : null,
        },
      },
      {
        provide: AuthService,
        useValue: { roles: () => roles, user: () => ({ roles, permissions, areaLocalityId }) },
      },
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

  const buttonSaying = (label: string) =>
    [...fixture.nativeElement.querySelectorAll('button')].find((b) =>
      (b as HTMLElement).textContent?.trim().startsWith(label),
    ) as HTMLButtonElement | undefined;

  function load(list: Pathshala[] = []) {
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/v1/pathshala/pathshalas').flush(list);
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('names where each Pathshala is - its locality and city, or the city or village alone', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    load([
      pathshala({ address: { line: 'Near the Mandir', countryId: null, stateId: null, cityId: 'c9', localityId: 'loc1' } }),
      pathshala({ id: 'p2', address: { line: null, countryId: null, stateId: null, cityId: 'city1', localityId: null } }),
      pathshala({ id: 'p3', address: null }),
    ]);

    const tiles = [...fixture.nativeElement.querySelectorAll('.grid > .card')].map((t) => (t as HTMLElement).textContent!);

    expect(tiles[0]).toContain('Near the Mandir');
    expect(tiles[0]).toContain('Hiran Magri, Udaipur');
    expect(tiles[1]).toContain('Bhinder');
    expect(tiles[2]).not.toContain('Udaipur');
  });

  it('offers a Samaaj Admin no create button', () => {
    // The master record is the platform operator's. Offering it here would be
    // offering a control that always answers 403.
    configure(['SamaajAdmin'], 't1');
    build();
    load([pathshala()]);

    expect(component.canCreate()).toBe(false);
    expect(buttonSaying('Create a Pathshala')).toBeUndefined();
  });

  it('offers a Super Admin scoped into a Samaaj the create button, which opens the dialog', () => {
    configure(['SuperAdmin'], 't1');
    build();
    load([pathshala()]);

    buttonSaying('Create a Pathshala')!.click();
    fixture.detectChanges();

    expect(component.dialogOpen()).toBe(true);
    expect(component.editing()).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();

    // The dialog asks for the country list as it opens.
    http.expectOne('/v1/master-data/lookup-items?category=Country&includeInactive=false').flush([]);
  });

  it('offers a Super Admin with no Samaaj chosen nothing to create into', () => {
    // The command creates the record inside whichever Samaaj the request is
    // scoped to, so with no scope there is nowhere to put one.
    configure(['SuperAdmin'], null);
    build();
    fixture.detectChanges();

    expect(component.canCreate()).toBe(false);
    expect(text()).toContain('Choose one in the top bar');
  });

  it('still explains an empty list to somebody who cannot create one', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    load([]);

    expect(text()).toContain('ask them to add one');
  });

  it('puts Edit and Stand down on the tile of a Pathshala the admin runs', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    load([pathshala()]);

    expect(buttonSaying('Edit')).toBeDefined();
    expect(buttonSaying('Stand down')).toBeDefined();
  });

  it('opens the edit dialog on the Pathshala as it is', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    load([pathshala()]);

    buttonSaying('Edit')!.click();
    fixture.detectChanges();
    http.expectOne('/v1/master-data/lookup-items?category=Country&includeInactive=false').flush([]);

    expect(component.editing()?.id).toBe('p1');
    expect(text()).toContain('Edit Shri Mahavir Jain Pathshala');
  });

  it('shows a teacher only the Pathshalas they teach at', () => {
    configure(['PathshalaTeacher'], 't1', ['Pathshala.Teach', 'Pathshala.Attendance.Write']);
    build();
    fixture.detectChanges();

    const call = http.expectOne((r) => r.url === '/v1/pathshala/pathshalas');

    expect(call.request.params.get('staffed')).toBe('true');

    call.flush([pathshala()]);
  });

  it('shows somebody who runs Pathshalas the whole directory', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    fixture.detectChanges();

    const call = http.expectOne((r) => r.url === '/v1/pathshala/pathshalas');

    expect(call.request.params.has('staffed')).toBe(false);

    call.flush([]);
  });

  it('gives somebody without Pathshala.Manage neither control', () => {
    configure(['Member', 'PathshalaTeacher'], 't1', ['Members.Read', 'Pathshala.Attendance.Write']);
    build();
    load([pathshala()]);

    expect(buttonSaying('Edit')).toBeUndefined();
    expect(buttonSaying('Stand down')).toBeUndefined();
    expect(text()).toContain('Open');
  });

  it("gives an Area Admin the controls on their own locality's Pathshala only", () => {
    configure(['Member', 'AreaAdmin'], 't1', ['Pathshala.Manage'], 'loc-hm');
    build();
    load([
      pathshala({ id: 'own', address: { line: null, countryId: null, stateId: null, cityId: null, localityId: 'loc-hm' } }),
      pathshala({ id: 'other', name: 'Elsewhere', address: null }),
    ]);

    const edits = [...fixture.nativeElement.querySelectorAll('button')].filter((b) =>
      (b as HTMLElement).textContent?.trim().startsWith('Edit'),
    );

    expect(edits).toHaveLength(1);
    expect(component.canManage(pathshala({ address: null }))).toBe(false);
  });

  it('will not stand a Pathshala down on one click, and keeps the trigger', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    load([pathshala()]);

    const trigger = buttonSaying('Stand down')!;
    trigger.click();
    fixture.detectChanges();

    expect(text()).toContain('cannot be undone');
    expect(buttonSaying('Stand down')).toBe(trigger);
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    http.expectNone((r) => r.method === 'DELETE');
  });

  it('stands it down once confirmed and re-reads the list', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    load([pathshala()]);

    component.standingDown.set('p1');
    fixture.detectChanges();
    buttonSaying('Stand it down')!.click();

    const call = http.expectOne('/v1/pathshala/pathshalas/p1');

    expect(call.request.method).toBe('DELETE');
    call.flush(pathshala({ status: 'Inactive' }));
    http.expectOne((r) => r.url === '/v1/pathshala/pathshalas').flush([pathshala({ status: 'Inactive' })]);
    fixture.detectChanges();

    expect(text()).toContain('has been stood down');
    expect(text()).toContain('Stood down');
    expect(buttonSaying('Stand down')).toBeUndefined();
  });

  it('reads a 404 as the module being off', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/v1/pathshala/pathshalas').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('does not run the Pathshala module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
