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
  function load(
    options: {
      modules?: string[];
      user?: CurrentUser;
      /** How many notifications, all of them unread. */
      notifications?: number;
      /** Exact rows, when a test cares which of them have been read. */
      notificationRows?: { readAt: string | null }[];
    } = {},
  ): void {
    fixture.detectChanges();

    http.expectOne('/v1/identity/me').flush(options.user ?? member);
    http
      .expectOne('/v1/identity/tenants/mahavir-samaj')
      .flush(samaajWith(options.modules ?? ['Pathshala']));
    http
      .expectOne('/v1/notifications')
      .flush(
        options.notificationRows ??
          new Array(options.notifications ?? 0).fill({ readAt: null }),
      );

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

  it('every tile it offers actually goes somewhere', () => {
    // This used to assert the opposite half: that a tile with no screen yet is
    // marked "Coming soon" rather than linking nowhere. Every module now has
    // screens, so there is no such tile left to point at, and the property
    // worth holding is the stronger one. The template still handles a
    // routeless tile - a module can land before its screen again - it just has
    // no example to test against today.
    load({ modules: ['Community', 'Pathshala', 'Boli', 'CelebrityVoting', 'SocialIssues'] });

    const cards = (fixture.nativeElement as HTMLElement).querySelectorAll('.card');
    const dead = (fixture.nativeElement as HTMLElement).querySelectorAll('.card button[disabled]');

    expect(cards.length).toBeGreaterThan(0);
    expect(dead.length).toBe(0);
    expect(text()).not.toContain('Coming soon');
  });

  it('offers each tile as a link, not a button that navigates', () => {
    // These were buttons calling router.navigateByUrl. Identical on screen, and
    // wrong in every other way: announced as "button" rather than "link", and
    // impossible to middle-click or long-press into a second tab. This is
    // navigation, so it is an anchor with an href.
    load({ modules: ['Community'] });

    const root = fixture.nativeElement as HTMLElement;
    const links = root.querySelectorAll('.card a[href]');
    const navButtons = root.querySelectorAll('.card button:not([disabled])');

    expect(links.length).toBeGreaterThan(0);
    expect(navButtons.length).toBe(0);
  });

  it('gives each tile link a name that says which tile it is', () => {
    // Every tile's visible label is "Open", so a screen reader listing the
    // links on this page would read "Open, Open, Open". The tile's title is
    // appended for assistive technology and hidden from sighted readers.
    load({ modules: ['Community'] });

    const cards = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.card'));

    expect(cards.length).toBeGreaterThan(0);

    for (const card of cards) {
      const link = card.querySelector('a[href]');
      const title = card.querySelector('h3')?.textContent?.trim() ?? '';

      expect(link).not.toBeNull();
      expect(link?.textContent).toContain(title);
      expect(link?.querySelector('.sr-only')).not.toBeNull();
    }
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

    expect(text()).toContain('You have 1 unread notification.');
  });

  it('counts the unread ones, not every notification that exists', () => {
    // It counted the length of the list until read state existed, so the badge
    // sat at the same number no matter what the member did with it - which
    // trains people to ignore a badge.
    load({
      notificationRows: [
        { readAt: null },
        { readAt: '2026-01-01T10:00:00Z' },
        { readAt: '2026-01-02T10:00:00Z' },
      ],
    });

    expect(text()).toContain('You have 1 unread notification.');
  });

  it('says nothing at all once everything has been read', () => {
    load({ notificationRows: [{ readAt: '2026-01-01T10:00:00Z' }] });

    expect(text()).not.toContain('unread');
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
