import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_CONFIG, AllModuleKeys, CurrentUser, TenantSummary } from '@samaajconnect/shared';
import { routes } from '../../app.routes';
import { HomeComponent } from './home.component';

describe('HomeComponent', () => {
  let fixture: ComponentFixture<HomeComponent>;
  let component: HomeComponent;
  let http: HttpTestingController;

  const member: CurrentUser = {
    userId: 'u1',
    tenantId: 't1',
    tenantSlug: 'mahavir-samaj',
    mobileOrEmail: 'ravi@example.com',
    fullName: 'Ravi Shah',
    status: 'Active',
    isContactVerified: false,
    lastLoginAt: null,
    roles: ['Member'],
    permissions: ['Members.Read'],
  };

  function samaajWith(modules: string[]): TenantSummary {
    return {
      id: 't1',
      name: 'Mahavir Samaaj',
      slug: 'mahavir-samaj',
      logoUrl: null,
      status: 'Active',
      enabledModules: modules,
    };
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HomeComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_CONFIG, useValue: { gatewayUrl: '' } },
      ],
    });

    fixture = TestBed.createComponent(HomeComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Answers the three calls Home makes on load. */
  function load(options: { modules?: string[]; user?: CurrentUser; notifications?: number } = {}): void {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(options.user ?? member);
    http
      .expectOne('/v1/identity/tenants/mahavir-samaj')
      .flush(samaajWith(options.modules ?? ['Pathshala']));
    http
      .expectOne('/v1/notifications')
      .flush(new Array(options.notifications ?? 0).fill({}));

    fixture.detectChanges();
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function tileTitles(): string[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.card h3')).map(
      (heading) => heading.textContent?.trim() ?? '',
    );
  }

  it('greets the member with their Samaaj', () => {
    load();

    expect(text()).toContain('Ravi Shah');
    expect(text()).toContain('Mahavir Samaaj');
  });

  it('shows only the modules this Samaaj has switched on', () => {
    load({ modules: ['Pathshala'] });

    // The gateway answers 404 for a disabled module, so offering the tile
    // would be offering a door to a 404.
    expect(tileTitles()).toContain('Pathshala');
    expect(tileTitles()).not.toContain('Boli');
    expect(tileTitles()).not.toContain('Events');
  });

  it('shows the community tiles for a Samaaj that runs community', () => {
    // The regression test for a bug that was invisible by construction. These
    // three tiles were filtered on `Events` and `VolunteerGroups`, neither of
    // which is a module key the platform has ever had - all three are behind
    // `community`. The filter did not fail, it never matched, so the tiles were
    // missing for every Samaaj with nothing logged anywhere.
    load({ modules: ['community'] });

    expect(tileTitles()).toContain('Timeline');
    expect(tileTitles()).toContain('Events');
    expect(tileTitles()).toContain('Volunteer');
  });

  it('offers every module the platform has, to a Samaaj that runs them all', () => {
    load({ modules: [...AllModuleKeys] });

    // A tile the catalogue can enable but Home never lists is a feature no
    // member can reach from the portal.
    expect(tileTitles()).toEqual(
      expect.arrayContaining([
        'Timeline',
        'Events',
        'Volunteer',
        'Social Issues',
        'Celebrities of Samaaj',
        'Pathshala',
        'Boli',
      ]),
    );
  });

  it('never offers a tile whose route the app does not register', () => {
    // A tile linking to a path with no route sends the member to the wildcard
    // and straight back to Home, which reads as the button being broken.
    load({ modules: [...AllModuleKeys] });

    const registered = routes.map((route) => `/${route.path}`);

    for (const tile of component.tiles()) {
      if (tile.route !== null) {
        expect(registered).toContain(tile.route);
      }
    }
  });

  it('always shows the core tiles that are not behind a module flag', () => {
    load({ modules: [] });

    expect(tileTitles()).toContain('Members');
    expect(tileTitles()).toContain('Family');
  });

  it('matches module keys case-insensitively', () => {
    load({ modules: ['pathshala'] });

    expect(tileTitles()).toContain('Pathshala');
  });

  it('does not invent the counts the wireframe showed', () => {
    load();

    // The prototype showed 1,248 members and 6 events. Nothing can supply
    // those yet, so nothing claims them.
    expect(text()).not.toContain('1,248');
    expect(text()).not.toContain('1248');
  });

  it('marks tiles whose screens do not exist yet instead of linking nowhere', () => {
    // Boli is the last module with a tile and no screen; Pathshala was the
    // example here until its screens shipped.
    load({ modules: ['Boli'] });

    const disabled = (fixture.nativeElement as HTMLElement).querySelectorAll('.card button[disabled]');

    expect(disabled.length).toBeGreaterThan(0);
    expect(text()).toContain('Coming soon');
  });

  it('says so when the Samaaj has no modules enabled at all', () => {
    load({ modules: [] });

    expect(text()).toContain('has not switched on any modules');
  });

  it('reports the unverified contact state the API returned', () => {
    load();

    expect(text()).toContain('not verified yet');
  });

  it('does not nag a member whose contact is verified', () => {
    load({ user: { ...member, isContactVerified: true } });

    expect(text()).not.toContain('not verified yet');
  });

  it('reports notifications in the singular when there is one', () => {
    load({ notifications: 1 });

    expect(text()).toContain('You have 1 notification.');
  });

  it('shows an error with a retry when the profile call fails', () => {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.error()).not.toBeNull();
    expect(text()).toContain('Try again');
  });

  it('still renders when the Samaaj lookup fails, just without the module filter', () => {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(member);
    http
      .expectOne('/v1/identity/tenants/mahavir-samaj')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    http.expectOne('/v1/notifications').flush([]);
    fixture.detectChanges();

    // A failure there costs the Samaaj name, not the page.
    expect(component.error()).toBeNull();
    expect(tileTitles()).toContain('Members');
  });
});
