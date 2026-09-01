import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '@samaajconnect/shared';
import { AdminApi } from '../core/admin-api';
import { AdminScope } from '../core/admin-scope';
import { Tenant } from '../core/admin.models';

interface NavItem {
  readonly label: string;
  readonly route?: string;

  /** Why this screen is not available. Present exactly when `route` is not. */
  readonly pending?: string;

  /** Roles that see the item at all. Empty means everyone signed in. */
  readonly roles?: readonly string[];
}

interface NavGroup {
  readonly heading: string;
  readonly items: readonly NavItem[];
}

/**
 * The admin panel's chrome, from the admin wireframe: a fixed dark left nav, a
 * top bar naming the current screen, and the Samaaj scope selector.
 *
 * The nav lists every screen the wireframe has. The ones with no backend are
 * shown disabled with the reason, rather than omitted or wired to a stub - the
 * wireframe-to-angular skill is explicit that a missing endpoint means build
 * the backend, and an admin who cannot see that Pathshala is coming has been
 * told less than the wireframe promised.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  styleUrl: './shell.css',
  template: `
    <a class="skip-link" href="#main-content">Skip to the main content</a>

    <div class="app">
      <aside class="side">
        <div class="brand">samaajconnect<br /><span>Unified Admin</span></div>
        <div class="scope">{{ scopeLabel() }}</div>

        <nav class="nav" aria-label="Admin sections">
          @for (group of visibleNav(); track group.heading) {
            <div class="grp">{{ group.heading }}</div>
            @for (item of group.items; track item.label) {
              @if (item.route) {
                <a
                  [routerLink]="item.route"
                  routerLinkActive="active"
                  [routerLinkActiveOptions]="{ exact: false }"
                >
                  {{ item.label }}
                </a>
              } @else {
                <!--
                  The reason is a nested span, not a title attribute. A title
                  becomes the accessible name and replaces the label entirely,
                  so a screen reader announced "The events service does not
                  exist yet" with no way to tell which item that was.
                -->
                <span class="disabled">
                  <span>{{ item.label }}</span>
                  <em aria-hidden="true">soon</em>
                  <span class="sr-only">— {{ item.pending }}</span>
                </span>
              }
            }
          }
        </nav>
      </aside>

      <main id="main-content" tabindex="-1">
        <div class="bar">
          <div>
            <b>{{ scopeLabel() }}</b>
            <div class="muted">Unified administration workspace</div>
          </div>

          @if (isSuperAdmin()) {
            <div class="scope-picker">
              <label class="sr-only" for="samaaj">Samaaj to act on</label>
              <!--
                [selected] per option rather than [value] on the select. The
                Samaaj list arrives after the first render, and a select whose
                value names an option that does not exist yet silently falls
                back to the first one - so the picker said "All Samaaj" while
                the panel was scoped to a Samaaj.
              -->
              <select id="samaaj" class="input" (change)="pick($event)">
                <option value="" [selected]="scope.tenantId() === null">
                  All Samaaj • platform view
                </option>
                @for (tenant of tenants(); track tenant.id) {
                  <option [value]="tenant.id" [selected]="tenant.id === scope.tenantId()">
                    {{ tenant.name }} • {{ tenant.slug }}
                  </option>
                }
              </select>
            </div>
          }

          <div class="profile">
            <span>{{ auth.user()?.fullName ?? 'Signed in' }}</span>
            <button class="btn link" type="button" (click)="signOut()">Sign out</button>
          </div>
        </div>

        @if (scope.tenant(); as acting) {
          <!--
            The override is logged at the gateway on every request that carries
            it, and on a single domain the SuperAdmin role is the whole gate.
            Making it impossible to forget which Samaaj you are acting on is
            the least the panel can do about that.
          -->
          <p class="notice" role="status">
            Acting on <b>{{ acting.name }}</b> ({{ acting.slug }}). Every request you make is
            recorded against your account in that Samaaj's audit log.
            <button class="btn link" type="button" (click)="scope.clear()">
              Return to the platform view
            </button>
          </p>
        }

        <router-outlet />
      </main>
    </div>
  `,
})
export class ShellComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly router = inject(Router);

  readonly auth = inject(AuthService);
  readonly scope = inject(AdminScope);

  readonly tenants = signal<readonly Tenant[]>([]);

  readonly isSuperAdmin = computed(() => this.auth.roles().includes('SuperAdmin'));

  readonly scopeLabel = computed(() => {
    const acting = this.scope.tenant();

    if (acting !== null) {
      return acting.name;
    }

    return this.isSuperAdmin() ? 'All Samaaj • Super Admin' : (this.auth.user()?.tenantSlug ?? '');
  });

  readonly visibleNav = computed(() =>
    NAV.map((group) => ({
      heading: group.heading,
      items: group.items.filter(
        (item) => item.roles === undefined || this.auth.hasAnyRole(...item.roles),
      ),
    })).filter((group) => group.items.length > 0),
  );

  ngOnInit(): void {
    // currentUserGuard has already resolved who this is, so roles are known
    // by the time anything renders. The shell only has to fill the Samaaj
    // selector, and only a Super Admin has one.
    this.loadTenantsIfPlatformAdmin();
  }

  pick(event: Event): void {
    const id = (event.target as HTMLSelectElement).value;

    this.scope.select(id === '' ? null : (this.tenants().find((t) => t.id === id) ?? null));

    // A full reload, deliberately. Screens read their data once on init, and
    // the router reuses a component when the URL has not changed - so an
    // in-app navigation would leave the previous Samaaj's rows on screen under
    // the new Samaaj's name, which is the most misleading thing this panel
    // could do. Reloading also guarantees nothing cached from the previous
    // scope survives anywhere. The selection itself is in sessionStorage, so
    // it comes back.
    location.reload();
  }

  signOut(): void {
    this.scope.clear();

    // Navigates immediately rather than waiting: the tokens are already gone
    // locally, and holding the admin on the page while a network call finishes
    // would make signing out feel broken on a bad connection. The server call
    // is what actually revokes the session.
    this.auth.signOut().subscribe();

    void this.router.navigate(['/login']);
  }

  private loadTenantsIfPlatformAdmin(): void {
    if (!this.isSuperAdmin()) {
      return;
    }

    this.api.listTenants().subscribe({
      next: (tenants) => this.tenants.set(tenants),
      error: () => this.tenants.set([]),
    });
  }
}

/**
 * The wireframe's left nav, verbatim in order and wording. `pending` says why a
 * screen is not there yet; those services are Phase 2 and later in
 * `DEVELOPMENT_PLAN.md`.
 */
const NAV: readonly NavGroup[] = [
  {
    heading: 'Overview',
    items: [{ label: 'Dashboard', route: '/dashboard' }],
  },
  {
    heading: 'Platform',
    items: [
      { label: 'Samaaj / Tenants', route: '/tenants', roles: ['SuperAdmin'] },
      { label: 'Admin Users & Roles', route: '/admins' },
      { label: 'Audit Logs', route: '/audit' },
    ],
  },
  {
    heading: 'Community',
    items: [
      { label: 'Members', pending: 'The member directory screen is not built yet.' },
      { label: 'Families & Children', route: '/conversions' },
      { label: 'Timeline / Moderation', pending: 'The timeline service does not exist yet.' },
      { label: 'Volunteer Groups', pending: 'The volunteer-groups service does not exist yet.' },
      { label: 'Events', pending: 'The events service does not exist yet.' },
      { label: 'Social Issues', pending: 'The social-issues service does not exist yet.' },
      { label: 'Celebrities / Voting', pending: 'The celebrity-voting service does not exist yet.' },
    ],
  },
  {
    heading: 'Education',
    items: [{ label: 'Jain Pathshala', pending: 'The Pathshala service does not exist yet.' }],
  },
  {
    heading: 'Boli',
    items: [{ label: 'Auctions / Boli', pending: 'The Boli service does not exist yet.' }],
  },
  {
    heading: 'Operations',
    items: [
      {
        label: 'Notifications',
        pending: 'There is no delivery channel yet, so there is nothing to send.',
      },
      { label: 'Reports & Analytics', pending: 'Reporting is a later phase.' },
      { label: 'Settings', pending: 'Samaaj settings live on the Samaaj screen for now.' },
    ],
  },
];
