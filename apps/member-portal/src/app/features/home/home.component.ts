import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  AuthService,
  CurrentUser,
  ModuleKey,
  ModuleKeys,
  TenantSummary,
  describeError,
  hasModule,
} from '@samaajconnect/shared';

/**
 * A module tile on Home. `route` stays null until that feature ships.
 *
 * `moduleKey` is a `ModuleKey`, not a string, and that is load-bearing. These
 * were free-text before, and two of them - `Events` and `VolunteerGroups` -
 * were not keys the platform has ever had: both are behind `community`. The
 * filter did not fail, it simply never matched, so those tiles were invisible
 * to every Samaaj with nothing logged anywhere. The type now makes that
 * mistake a compile error.
 */
interface ModuleTile {
  readonly moduleKey: ModuleKey | null;
  readonly title: string;
  readonly description: string;
  readonly action: string;
  readonly route: string | null;
}

/**
 * Home, from the member-portal wireframe's `#home` screen.
 *
 * Two things the wireframe showed are deliberately not reproduced verbatim.
 * Its counters (1,248 members, 4 family, 6 events) came from services that do
 * not exist yet, and the skill is explicit that prototype numbers must not be
 * hardcoded - so a tile shows a count only once something can supply one.
 * And the tile list is filtered by the Samaaj's enabled modules, because the
 * gateway already answers 404 for a module a Samaaj has switched off; offering
 * a door that leads to a 404 would be worse than not offering it.
 *
 * The Samaaj pill, member name and Sign out button the wireframe drew beside
 * "Home" moved into the shell's top bar - every screen has one now, not just
 * this one, so showing them here too would just be the same three things
 * twice on this particular page.
 */
@Component({
  selector: 'app-home',
  imports: [RouterLink],
  styleUrl: './home.css',
  template: `
    <div class="home">
      <header class="home-head">
        <h1 class="page-title">Home</h1>
        <p class="subtitle">Choose where you want to go.</p>
      </header>

      @if (loading()) {
        <p role="status">Loading your Samaaj…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else {
        @if (user(); as member) {
          @if (!member.isContactVerified) {
            <p class="notice info" role="status">
              Your mobile number is not verified yet. Verification messages are not switched on
              yet; nothing is blocked in the meantime.
            </p>
          }
        }

        @if (unreadNotifications() > 0) {
          <p class="notice info" role="status">
            You have {{ unreadNotifications() }} unread
            {{ unreadNotifications() === 1 ? 'notification' : 'notifications' }}.
            <a routerLink="/notifications">Read them</a>
          </p>
        }

        @if (noModulesEnabled()) {
          <p class="notice info" role="status">
            Your Samaaj has not switched on any modules yet. Your administrator can enable them.
          </p>
        }

        <div class="grid">
          @for (tile of tiles(); track tile.title) {
            <div class="card">
              <h2>{{ tile.title }}</h2>
              <p>{{ tile.description }}</p>

              <div class="actions">
                @if (tile.route; as target) {
                  <!-- A link, not a button. This is navigation: it belongs in
                       the tab order as a link, it should say "link" to a screen
                       reader rather than "button", and a member should be able
                       to middle-click or long-press it to open their Samaaj's
                       events in a second tab like anything else on the web.
                       A button that calls router.navigateByUrl can do none of
                       those, and it looked identical on screen, which is why
                       it survived this long. -->
                  <a class="btn" [routerLink]="target">
                    {{ tile.action }}
                    <span class="sr-only">{{ tile.title }}</span>
                  </a>
                } @else {
                  <button class="btn" type="button" disabled>{{ tile.action }}</button>
                  <span class="pill">Coming soon</span>
                }
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class HomeComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);

  readonly user = signal<CurrentUser | null>(null);
  readonly samaaj = signal<TenantSummary | null>(null);
  readonly unreadNotifications = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  /**
   * Every tile the platform can offer. A tile with a module key appears only
   * when the Samaaj has that module switched on; one without a key is core and
   * always shown.
   */
  private static readonly AllTiles: readonly ModuleTile[] = [
    {
      // First, because the welcome message every new member gets tells them to
      // complete their profile, and for a long time there was nowhere to do it.
      moduleKey: null,
      title: 'My profile',
      description: 'Your details, and who in your Samaaj can see each one.',
      action: 'Edit',
      route: '/profile',
    },
    {
      moduleKey: null,
      title: 'Members',
      description: 'Explore the Samaaj member directory.',
      action: 'Open',
      route: '/members',
    },
    {
      moduleKey: null,
      title: 'Family',
      description: 'Family members linked to your profile.',
      action: 'Manage',
      route: '/family',
    },
    {
      // Core, and deliberately not behind a module key: a right under the DPDP
      // Act does not depend on which modules a Samaaj has switched on.
      moduleKey: null,
      title: 'Your data and privacy',
      description: 'What you agreed to, a copy of your data, and erasing your account.',
      action: 'Open',
      route: '/privacy',
    },
    {
      moduleKey: ModuleKeys.Community,
      title: 'Timeline',
      description: 'Samaaj announcements and approved member posts.',
      action: 'Open',
      route: '/timeline',
    },
    {
      moduleKey: ModuleKeys.Community,
      title: 'Events',
      description: 'Upcoming community events.',
      action: 'View',
      route: '/events',
    },
    {
      moduleKey: ModuleKeys.Community,
      title: 'Volunteer',
      description: 'Find groups and apply to join.',
      action: 'Explore',
      route: '/groups',
    },
    {
      moduleKey: ModuleKeys.SocialIssues,
      title: 'Social Issues',
      description: 'Raise an issue, and follow the ones your Samaaj published.',
      action: 'Open',
      route: '/issues',
    },
    {
      moduleKey: ModuleKeys.CelebrityVoting,
      title: 'Celebrities of Samaaj',
      description: 'Nominate, vote, and see the published result.',
      action: 'Open',
      route: '/voting',
    },
    {
      moduleKey: ModuleKeys.Pathshala,
      title: 'Pathshala',
      description: "Manage your child's enrollment and education.",
      action: 'Open',
      route: '/pathshala',
    },
    {
      moduleKey: ModuleKeys.Boli,
      title: 'Boli',
      description: 'View active and published Boli results.',
      action: 'Open',
      route: '/boli',
    },
  ];

  readonly tiles = computed(() => {
    const enabled = this.samaaj()?.enabledModules ?? [];

    return HomeComponent.AllTiles.filter(
      (tile) => tile.moduleKey === null || hasModule(enabled, tile.moduleKey),
    );
  });

  /**
   * True when the Samaaj runs no optional modules at all. Checked separately
   * from an empty tile list, because the core tiles - Members and Family - are
   * not behind a module flag and are always present.
   */
  readonly noModulesEnabled = computed(() =>
    this.tiles().every((tile) => tile.moduleKey === null),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.auth.loadCurrentUser().subscribe({
      next: (member) => {
        this.user.set(member);
        this.loadSamaaj(member.tenantSlug);
        this.loadNotifications();
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  private loadSamaaj(slug: string): void {
    if (!slug) {
      return;
    }

    // A failure here costs the Samaaj name and the module filter, not the
    // page, so it is not raised as a page-level error.
    this.auth.findTenant(slug).subscribe({
      next: (found) => this.samaaj.set(found),
      error: () => this.samaaj.set(null),
    });
  }

  /**
   * Counts the ones this member has not read, not the ones that exist.
   *
   * It counted every row until read state was per member and there was
   * anything to count: "You have 12 notifications" stayed at 12 no matter what
   * the member did, which is a badge that trains people to ignore it. `readAt`
   * is this member's own - a broadcast read by four hundred others is still
   * unread for them - so this is now the number the word promises.
   */
  private loadNotifications(): void {
    this.http.get<{ readAt: string | null }[]>('/v1/notifications').subscribe({
      next: (found) => this.unreadNotifications.set(found.filter((n) => !n.readAt).length),
      error: () => this.unreadNotifications.set(0),
    });
  }
}
