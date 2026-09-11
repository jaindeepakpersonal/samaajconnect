import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs/operators';
import { AuthService, ModuleKey, ModuleKeys, TenantSummary, hasModule } from '@samaajconnect/shared';

interface NavItem {
  readonly label: string;
  readonly route: string;
  readonly moduleKey: ModuleKey | null;
}

interface NavGroup {
  readonly heading: string;
  readonly items: readonly NavItem[];
}

/**
 * The wireframe's left nav, verbatim in grouping and order - minus the
 * screens it drew as separate buttons that this app built as one route.
 * `#children` is `/family`'s own second card, not a nav destination; the five
 * Pathshala screens (`#myclass`/`#attendance`/`#exams`/`#progress`) are one
 * enrolment screen at `/pathshala/:id`, reached from the Pathshala list rather
 * than the nav; and `#pathevents` has no endpoint and stays off it entirely.
 *
 * No separate "Home" row. It used to lead to a tile grid that was every one
 * of these routes offered a second time - see apps/member-portal/CLAUDE.md.
 * The brand at the top of the sidebar is the way back to Timeline now, the
 * same "click the logo" pattern as everywhere else on the web.
 */
const NAV: readonly NavGroup[] = [
  {
    heading: 'Core',
    items: [
      { label: 'My Profile', route: '/profile', moduleKey: null },
      { label: 'Members', route: '/members', moduleKey: null },
      { label: 'My Family', route: '/family', moduleKey: null },
      { label: 'Timeline', route: '/timeline', moduleKey: ModuleKeys.Community },
    ],
  },
  {
    heading: 'Community',
    items: [
      { label: 'Volunteer Groups', route: '/groups', moduleKey: ModuleKeys.Community },
      { label: 'Events', route: '/events', moduleKey: ModuleKeys.Community },
      { label: 'Social Issues', route: '/issues', moduleKey: ModuleKeys.SocialIssues },
      { label: 'Celebrities of Samaaj', route: '/voting', moduleKey: ModuleKeys.CelebrityVoting },
    ],
  },
  {
    heading: 'Jain Pathshala',
    items: [{ label: 'Pathshala', route: '/pathshala', moduleKey: ModuleKeys.Pathshala }],
  },
  {
    heading: 'Boli',
    items: [{ label: 'Auctions / Boli', route: '/boli', moduleKey: ModuleKeys.Boli }],
  },
  {
    heading: 'Account',
    items: [
      { label: 'Notifications', route: '/notifications', moduleKey: null },
      { label: 'Your data and privacy', route: '/privacy', moduleKey: null },
    ],
  },
];

const ALL_NAV_ITEMS: readonly NavItem[] = NAV.flatMap((group) => group.items);

/**
 * The chrome every signed-in screen sits inside: the wireframe's fixed dark
 * sidebar and its top bar's breadcrumb, bell and user chip. Nothing built
 * this before now - every screen was an island reachable only from Home's own
 * tiles or the browser's back button, which is not what the wireframe drew or
 * what a member coming back to this app a second time should have to
 * rediscover each visit.
 *
 * The wireframe's "Tenant: mahavir-samaj / mahavir-samaj.samaajconnect.com"
 * block under the brand is gone rather than translated - root CLAUDE.md
 * section 6 is explicit that there is no Samaaj subdomain any more, and the
 * breadcrumb already names the Samaaj on every screen.
 *
 * Sign out moved from a nav row ("Logout / Login") into the top bar's user
 * chip, matching the one other shell this platform already has -
 * admin-portal's - rather than inventing a second placement for the same
 * control.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  styleUrl: './shell.css',
  template: `
    <div class="shell">
      <!--
        Mobile only (hidden by CSS above 700px). Unconditionally rendered
        with aria-expanded rather than swapped for a close button, per this
        app's own rule that a trigger stays in the DOM - the same reasoning
        the privacy screen's erasure panel already follows, applied to a
        menu instead of a confirmation.
      -->
      <button
        type="button"
        class="nav-toggle"
        [attr.aria-expanded]="navOpen()"
        aria-controls="shell-nav"
        (click)="toggleNav()"
      >
        <span aria-hidden="true">☰</span>
        <span class="sr-only">{{ navOpen() ? 'Close menu' : 'Open menu' }}</span>
      </button>

      @if (navOpen()) {
        <!-- Closes the menu without also acting as a second way to navigate. -->
        <button type="button" class="nav-backdrop" aria-hidden="true" (click)="closeNav()"></button>
      }

      <aside class="side" id="shell-nav" [class.open]="navOpen()">
        <a class="brand" routerLink="/timeline" (click)="closeNav()">samaajconnect</a>

        <nav class="nav" aria-label="Main sections">
          @for (group of visibleNav(); track group.heading) {
            <div class="grp">{{ group.heading }}</div>
            @for (item of group.items; track item.route) {
              <a
                [routerLink]="item.route"
                routerLinkActive="active"
                [routerLinkActiveOptions]="{ exact: false }"
                (click)="closeNav()"
              >
                {{ item.label }}
                @if (item.route === '/notifications' && unread() > 0) {
                  <span class="badge">{{ unread() }}</span>
                }
              </a>
            }
          }
        </nav>
      </aside>

      <div class="content">
        <div class="topbar">
          <div class="crumb">{{ samaajName() }} / {{ crumbLabel() }}</div>

          <div class="top-actions">
            <a
              class="bell"
              routerLink="/notifications"
              [attr.aria-label]="
                unread() > 0 ? 'Notifications, ' + unread() + ' unread' : 'Notifications'
              "
            >
              🔔
              @if (unread() > 0) {
                <span class="dot" aria-hidden="true"></span>
              }
            </a>

            <a class="user-chip" routerLink="/profile">{{ auth.user()?.fullName ?? 'Signed in' }}</a>

            <button class="btn secondary" type="button" (click)="signOut()">Sign out</button>
          </div>
        </div>

        <router-outlet />
      </div>
    </div>
  `,
})
export class ShellComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly auth = inject(AuthService);

  readonly samaaj = signal<TenantSummary | null>(null);
  readonly unread = signal(0);

  /** The mobile nav drawer. Always false above the 700px breakpoint - the
   *  toggle button is hidden there by CSS, so nothing ever sets it. */
  readonly navOpen = signal(false);

  readonly samaajName = computed(() => this.samaaj()?.name ?? this.auth.user()?.tenantSlug ?? '');

  readonly visibleNav = computed(() => {
    const enabled = this.samaaj()?.enabledModules ?? [];

    return NAV.map((group) => ({
      heading: group.heading,
      items: group.items.filter(
        (item) => item.moduleKey === null || hasModule(enabled, item.moduleKey),
      ),
    })).filter((group) => group.items.length > 0);
  });

  /**
   * Bridges the router into a signal the way `App` already does for focus
   * management - `router.url` is a plain property, and a `computed` reading
   * it directly would never see it change.
   */
  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /** The section name for the breadcrumb - the nav item this URL belongs to. */
  readonly crumbLabel = computed(() => {
    const url = this.currentUrl().split('?')[0] ?? '';
    const match = ALL_NAV_ITEMS.find(
      (item) => url === item.route || url.startsWith(`${item.route}/`),
    );

    return match?.label ?? '';
  });

  ngOnInit(): void {
    this.auth.ensureCurrentUser().subscribe({
      next: (user) => this.loadSamaaj(user.tenantSlug),
      error: () => {},
    });

    this.loadUnread();

    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe(() => this.loadUnread());
  }

  toggleNav(): void {
    this.navOpen.update((open) => !open);
  }

  closeNav(): void {
    this.navOpen.set(false);
  }

  signOut(): void {
    // Navigates immediately rather than waiting: the tokens are already gone
    // locally, and holding the member on the page while a network call
    // finishes would make signing out feel broken on a bad connection. The
    // server call is what actually revokes the session.
    this.auth.signOut().subscribe();

    void this.router.navigate(['/login']);
  }

  private loadSamaaj(slug: string): void {
    if (!slug) {
      return;
    }

    // A failure here costs the Samaaj name and the module filter, not the
    // shell itself, so it is not raised as a page-level error.
    this.auth.findTenant(slug).subscribe({
      next: (found) => this.samaaj.set(found),
      error: () => this.samaaj.set(null),
    });
  }

  /** Counts the ones this member has not read, refreshed on every navigation
   *  so the badge does not go stale after a visit to Notifications. */
  private loadUnread(): void {
    this.http.get<{ readAt: string | null }[]>('/v1/notifications').subscribe({
      next: (found) => this.unread.set(found.filter((n) => !n.readAt).length),
      error: () => this.unread.set(0),
    });
  }
}
