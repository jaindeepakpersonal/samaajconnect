import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { AdminScope } from '../../core/admin-scope';
import {
  ConversionRequest,
  Occasion,
  PathshalaDetail,
  PendingActivation,
  SamaajEvent,
  Tenant,
} from '../../core/admin.models';
import { DashboardComponent } from './dashboard.component';

const TENANT: Tenant = {
  id: 't1',
  name: 'Mahavir Samaaj',
  slug: 'mahavir-samaj',
  domain: null,
  logoUrl: null,
  contactPerson: null,
  contactEmail: null,
  status: 'Active',
  enabledModules: ['community', 'pathshala', 'boli'],
  createdAt: '2026-01-01T00:00:00Z',
  grievanceContact: null,
};

function conversionRequest(id: string): ConversionRequest {
  return {
    id,
    childProfileId: `child-${id}`,
    childFullName: 'A Child',
    mobileOrEmail: 'child@example.com',
    status: 'Pending',
    requestedAt: '2026-08-01T00:00:00Z',
    decidedBy: null,
    decidedAt: null,
    decisionNote: null,
  };
}

function pendingActivation(id: string): PendingActivation {
  return {
    userId: id,
    fullName: 'An Admin',
    mobileOrEmail: 'admin@example.com',
    createdAt: '2026-08-01T00:00:00Z',
    hasUsableCode: true,
    codeExpiresAt: null,
  };
}

function samaajEvent(id: string): SamaajEvent {
  return {
    id,
    title: 'Paryushan Lecture',
    description: null,
    startAt: '2026-09-05T10:00:00Z',
    endAt: null,
    venue: 'Jain Bhavan',
    eventTypeId: null,
    organizerType: 'Samaaj',
    organizerId: null,
    status: 'Draft',
    registrationEnabled: true,
    capacity: 200,
    registeredCount: 186,
    waitlistedCount: 0,
    isFull: false,
    myRegistrationStatus: null,
    cancelledAt: null,
    cancellationReason: null,
    createdAt: '2026-08-01T10:00:00Z',
  };
}

function occasion(id: string): Occasion {
  return {
    id,
    title: 'Diwali Boli',
    description: null,
    occasionDate: '2026-11-01T00:00:00Z',
    status: 'Upcoming',
    typeCount: 1,
    boliCount: 1,
  };
}

function pathshalaDetail(id: string, studentCounts: readonly number[]): PathshalaDetail {
  return {
    id,
    name: 'Mahavir Pathshala',
    address: null,
    contactPerson: null,
    status: 'Active',
    acceptsEnrolments: true,
    sessions: [],
    classes: studentCounts.map((studentCount, index) => ({
      id: `${id}-class-${index}`,
      sessionId: `${id}-session`,
      sessionLabel: '2026-27',
      name: `Class ${index}`,
      roomLabel: null,
      schedule: [],
      teacherMemberIds: [],
      studentCount,
    })),
  };
}

/**
 * The three tiles the wireframe's dashboard promised and the "Not built yet"
 * card used to explain away - events, Pathshala students and auctions - are
 * each exactly the list its own screen already loads, counted here instead of
 * rendered. These tests are about that wiring, not about the tiles this
 * component already had (`tenant-deactivation.spec.ts` and friends cover
 * those through their own screens).
 */
describe('DashboardComponent', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let http: HttpTestingController;
  let auth: AuthService;
  let scope: AdminScope;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(DashboardComponent);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    scope = TestBed.inject(AdminScope);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  /** Loads the signed-in user's roles before the component's own ngOnInit runs. */
  function signInAs(...roles: readonly string[]): void {
    auth.loadCurrentUser().subscribe();

    http.expectOne('/v1/identity/me').flush({
      userId: 'u1',
      tenantId: 't1',
      tenantSlug: 'mahavir-samaj',
      mobileOrEmail: 'admin@example.com',
      fullName: 'An Admin',
      status: 'Active',
      isContactVerified: true,
      lastLoginAt: null,
      roles,
      permissions: [],
    });
  }

  function tileNum(label: string): string | null | undefined {
    const tile = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('a.tile'),
    ).find((el) => el.querySelector('.muted')?.textContent?.trim() === label);

    return tile?.querySelector('.num')?.textContent?.trim();
  }

  it("counts a Samaaj administrator's events, Pathshala students and auctions", () => {
    signInAs('SamaajAdmin');
    fixture.detectChanges();

    http.expectNone('/v1/identity/tenants');

    http.expectOne('/v1/children/conversion-requests').flush([conversionRequest('c1')]);
    http.expectOne('/v1/identity/activations/pending').flush([pendingActivation('p1')]);
    http
      .expectOne('/v1/events?includeDrafts=true&includePast=false')
      .flush([samaajEvent('e1'), samaajEvent('e2')]);
    http
      .expectOne('/v1/boli/occasions')
      .flush([occasion('o1'), occasion('o2'), occasion('o3')]);
    http.expectOne('/v1/pathshala/pathshalas').flush([
      { id: 'ps1', name: 'Mahavir Pathshala', address: null, contactPerson: null, status: 'Active',
        currentSessionLabel: '2026-27', currentSessionId: 's1', classCount: 2, teacherCount: 2,
        acceptsEnrolments: true },
    ]);

    fixture.detectChanges();

    http.expectOne('/v1/pathshala/pathshalas/ps1').flush(pathshalaDetail('ps1', [5, 7]));

    fixture.detectChanges();

    expect(tileNum('Conversions awaiting a decision')).toBe('1');
    expect(tileNum('Invitations not yet redeemed')).toBe('1');
    expect(tileNum('Upcoming events')).toBe('2');
    expect(tileNum('Auctions')).toBe('3');
    expect(tileNum('Students enrolled')).toBe('12');
  });

  it('sums students across every Pathshala the Samaaj runs, not just the first', () => {
    signInAs('SamaajAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/children/conversion-requests').flush([]);
    http.expectOne('/v1/identity/activations/pending').flush([]);
    http.expectOne('/v1/events?includeDrafts=true&includePast=false').flush([]);
    http.expectOne('/v1/boli/occasions').flush([]);
    http.expectOne('/v1/pathshala/pathshalas').flush([
      { id: 'ps1', name: 'Pathshala One', address: null, contactPerson: null, status: 'Active',
        currentSessionLabel: null, currentSessionId: null, classCount: 1, teacherCount: 1,
        acceptsEnrolments: true },
      { id: 'ps2', name: 'Pathshala Two', address: null, contactPerson: null, status: 'Active',
        currentSessionLabel: null, currentSessionId: null, classCount: 1, teacherCount: 1,
        acceptsEnrolments: true },
    ]);

    fixture.detectChanges();

    http.expectOne('/v1/pathshala/pathshalas/ps1').flush(pathshalaDetail('ps1', [3]));
    http.expectOne('/v1/pathshala/pathshalas/ps2').flush(pathshalaDetail('ps2', [4, 2]));

    fixture.detectChanges();

    expect(tileNum('Students enrolled')).toBe('9');
  });

  it('reports zero students rather than leaving the tile blank when nobody runs one', () => {
    signInAs('SamaajAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/children/conversion-requests').flush([]);
    http.expectOne('/v1/identity/activations/pending').flush([]);
    http.expectOne('/v1/events?includeDrafts=true&includePast=false').flush([]);
    http.expectOne('/v1/boli/occasions').flush([]);
    http.expectOne('/v1/pathshala/pathshalas').flush([]);

    fixture.detectChanges();

    expect(tileNum('Students enrolled')).toBe('0');
  });

  it('leaves a tile blank, not zero, when its call fails', () => {
    // A Samaaj that has switched a module off 404s these, the same as any
    // other failure - "we could not reach the service" is not a count.
    signInAs('SamaajAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/children/conversion-requests').flush([]);
    http.expectOne('/v1/identity/activations/pending').flush([]);
    http
      .expectOne('/v1/events?includeDrafts=true&includePast=false')
      .flush({}, { status: 404, statusText: 'Not Found' });
    http.expectOne('/v1/boli/occasions').flush([]);
    http.expectOne('/v1/pathshala/pathshalas').flush([]);

    fixture.detectChanges();

    expect(tileNum('Upcoming events')).toBe('—');
  });

  it("fetches only the tenant list while a Super Admin is on the platform view", () => {
    signInAs('SuperAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/identity/tenants').flush([TENANT]);
    fixture.detectChanges();

    http.expectNone('/v1/children/conversion-requests');
    http.expectNone('/v1/identity/activations/pending');
    http.expectNone('/v1/events?includeDrafts=true&includePast=false');
    http.expectNone('/v1/boli/occasions');
    http.expectNone('/v1/pathshala/pathshalas');

    expect(tileNum('Samaaj on the platform')).toBe('1');
  });

  it("fetches a chosen Samaaj's events, students and auctions once a Super Admin picks one", () => {
    scope.select(TENANT);
    signInAs('SuperAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/identity/tenants').flush([TENANT]);
    http.expectOne('/v1/children/conversion-requests').flush([]);
    http.expectOne('/v1/identity/activations/pending').flush([]);
    http.expectOne('/v1/events?includeDrafts=true&includePast=false').flush([samaajEvent('e1')]);
    http.expectOne('/v1/boli/occasions').flush([]);
    http.expectOne('/v1/pathshala/pathshalas').flush([]);

    fixture.detectChanges();

    expect(tileNum('Upcoming events')).toBe('1');
  });
});
