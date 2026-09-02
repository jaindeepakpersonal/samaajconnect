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
    address: 'Hiran Magri',
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

    expect(component.canCreate()).toBe(false);
    expect(text()).toContain('Choose one in the top bar');

    http.verify();
  });

  it('creates a Pathshala, sending no blanks', () => {
    configure(['SuperAdmin'], 't1');
    build();
    load([]);

    component.name = '  Shri Mahavir Jain Pathshala  ';
    component.address = '   ';
    component.contactPerson = 'Smt. Kavita Jain';
    component.create();

    const call = http.expectOne('/v1/pathshala/pathshalas');

    expect(call.request.body).toEqual({
      name: 'Shri Mahavir Jain Pathshala',
      address: null,
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

  it('reads a 404 as the module being off', () => {
    configure(['SamaajAdmin'], 't1');
    build();
    fixture.detectChanges();
    http.expectOne('/v1/pathshala/pathshalas').flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(text()).toContain('does not run the Pathshala module');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();

    http.verify();
  });
});
