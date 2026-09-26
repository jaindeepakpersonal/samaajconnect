import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, AuthService } from '@samaajconnect/shared';
import { AdminScope } from '../../core/admin-scope';
import { SamaajEvent, Tenant } from '../../core/admin.models';
import { ReportsComponent } from './reports.component';

const TENANT: Tenant = {
  id: 't1',
  name: 'Mahavir Samaaj',
  slug: 'mahavir-samaj',
  domain: null,
  logoUrl: null,
  contactPerson: null,
  contactEmail: null,
  status: 'Active',
  enabledModules: ['community', 'pathshala'],
  createdAt: '2026-01-01T00:00:00Z',
  grievanceContact: null,
};

const THIS_YEAR = new Date().getFullYear();

function samaajEvent(id: string, year: number, registeredCount: number): SamaajEvent {
  return {
    id,
    title: 'Paryushan Lecture',
    description: null,
    startAt: `${year}-06-01T10:00:00Z`,
    endAt: null,
    venue: 'Jain Bhavan',
    eventTypeId: null,
    localityId: null,
    organizerType: 'Samaaj',
    organizerId: null,
    status: 'Published',
    registrationEnabled: true,
    capacity: null,
    registeredCount,
    waitlistedCount: 0,
    isFull: false,
    myRegistrationStatus: null,
    cancelledAt: null,
    cancellationReason: null,
    createdAt: `${year}-01-01T10:00:00Z`,
  };
}

/**
 * The three per-Samaaj cards this screen closes: registration growth,
 * average attendance and event registrations this year. The Super-Admin-only
 * cross-tenant comparison table from the wireframe is a separate, later
 * piece of work - not covered here.
 */
describe('ReportsComponent', () => {
  let fixture: ComponentFixture<ReportsComponent>;
  let http: HttpTestingController;
  let auth: AuthService;
  let scope: AdminScope;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      imports: [ReportsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(ReportsComponent);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    scope = TestBed.inject(AdminScope);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

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

  function cardNum(heading: string): string | null | undefined {
    const card = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.card'),
    ).find((el) => el.querySelector('h2')?.textContent?.trim() === heading);

    return card?.querySelector('.num')?.textContent?.trim();
  }

  it('shows growth, attendance and this year\'s registrations for a Samaaj administrator', () => {
    signInAs('SamaajAdmin');
    fixture.detectChanges();

    http
      .expectOne('/v1/members/registration-summary')
      .flush({ currentPeriodCount: 18, previousPeriodCount: 15, growthPercentage: 20 });
    http
      .expectOne('/v1/pathshala/attendance-summary')
      .flush({ present: 2, absent: 1, excused: 0, recorded: 3, percentage: 67 });
    http
      .expectOne('/v1/events?includeDrafts=true&includePast=true')
      .flush([samaajEvent('e1', THIS_YEAR, 186), samaajEvent('e2', THIS_YEAR, 40)]);

    fixture.detectChanges();

    expect(cardNum('Members')).toBe('+20.0%');
    expect(cardNum('Pathshala')).toBe('67%');
    expect(cardNum('Events')).toBe('226');
  });

  it('excludes registrations from events outside the current year', () => {
    signInAs('SamaajAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/members/registration-summary').flush({
      currentPeriodCount: 0, previousPeriodCount: 0, growthPercentage: null,
    });
    http.expectOne('/v1/pathshala/attendance-summary').flush({
      present: 0, absent: 0, excused: 0, recorded: 0, percentage: null,
    });
    http.expectOne('/v1/events?includeDrafts=true&includePast=true').flush([
      samaajEvent('e1', THIS_YEAR, 50),
      samaajEvent('e2', THIS_YEAR - 1, 999),
    ]);

    fixture.detectChanges();

    expect(cardNum('Events')).toBe('50');
  });

  it('shows no growth percentage when the previous period counted nothing', () => {
    signInAs('SamaajAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/members/registration-summary').flush({
      currentPeriodCount: 4, previousPeriodCount: 0, growthPercentage: null,
    });
    http.expectOne('/v1/pathshala/attendance-summary').flush({
      present: 0, absent: 0, excused: 0, recorded: 0, percentage: null,
    });
    http.expectOne('/v1/events?includeDrafts=true&includePast=true').flush([]);

    fixture.detectChanges();

    expect(cardNum('Members')).toBe('—');
    expect(cardNum('Pathshala')).toBe('—');
  });

  it('shows negative growth with an explicit sign', () => {
    signInAs('SamaajAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/members/registration-summary').flush({
      currentPeriodCount: 5, previousPeriodCount: 10, growthPercentage: -50,
    });
    http.expectOne('/v1/pathshala/attendance-summary').flush({
      present: 0, absent: 0, excused: 0, recorded: 0, percentage: null,
    });
    http.expectOne('/v1/events?includeDrafts=true&includePast=true').flush([]);

    fixture.detectChanges();

    expect(cardNum('Members')).toBe('-50.0%');
  });

  it('leaves a card blank, not zero, when its call fails', () => {
    signInAs('SamaajAdmin');
    fixture.detectChanges();

    http
      .expectOne('/v1/members/registration-summary')
      .flush({}, { status: 500, statusText: 'Server Error' });
    http.expectOne('/v1/pathshala/attendance-summary').flush({
      present: 0, absent: 0, excused: 0, recorded: 0, percentage: null,
    });
    http.expectOne('/v1/events?includeDrafts=true&includePast=true').flush([]);

    fixture.detectChanges();

    expect(cardNum('Members')).toBe('—');
  });

  it('fetches nothing while a Super Admin is on the platform view', () => {
    signInAs('SuperAdmin');
    fixture.detectChanges();

    http.expectNone('/v1/members/registration-summary');
    http.expectNone('/v1/pathshala/attendance-summary');
    http.expectNone('/v1/events?includeDrafts=true&includePast=true');

    expect(
      (fixture.nativeElement as HTMLElement).textContent,
    ).toContain('Choose a Samaaj in the top bar');
  });

  it('fetches reports for a chosen Samaaj once a Super Admin picks one', () => {
    scope.select(TENANT);
    signInAs('SuperAdmin');
    fixture.detectChanges();

    http.expectOne('/v1/members/registration-summary').flush({
      currentPeriodCount: 1, previousPeriodCount: 1, growthPercentage: 0,
    });
    http.expectOne('/v1/pathshala/attendance-summary').flush({
      present: 1, absent: 0, excused: 0, recorded: 1, percentage: 100,
    });
    http.expectOne('/v1/events?includeDrafts=true&includePast=true').flush([]);

    fixture.detectChanges();

    expect(cardNum('Members')).toBe('0.0%');
    expect(cardNum('Pathshala')).toBe('100%');
  });
});
