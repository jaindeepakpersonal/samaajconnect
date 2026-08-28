import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService, CurrentUser, TenantSummary, describeError } from '@samaajconnect/shared';

/** A module tile on Home. `route` stays null until that feature ships. */
interface ModuleTile {
  readonly moduleKey: string | null;
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
 */
@Component({
  selector: 'app-home',
  styleUrl: './home.css',
  template: `
    <div class="home">
      <header class="home-head">
        <div>
          <h1 class="page-title">Home</h1>
          <p class="subtitle">Choose where you want to go.</p>
        </div>

        @if (user(); as member) {
          <div class="home-identity">
            <span class="pill">{{ samaajName() }}</span>
            <span class="home-name">{{ member.fullName }}</span>
            <button class="btn secondary" type="button" (click)="signOut()">Sign out</button>
          </div>
        }
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
            You have {{ unreadNotifications() }}
            {{ unreadNotifications() === 1 ? 'notification' : 'notifications' }}.
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
              <h3>{{ tile.title }}</h3>
              <p>{{ tile.description }}</p>

              <div class="actions">
                @if (tile.route; as target) {
                  <button class="btn" type="button" (click)="open(target)">{{ tile.action }}</button>
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
  private readonly router = inject(Router);

  readonly user = signal<CurrentUser | null>(null);
  readonly samaaj = signal<TenantSummary | null>(null);
  readonly unreadNotifications = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly samaajName = computed(() => this.samaaj()?.name ?? this.user()?.tenantSlug ?? '');

  /**
   * Every tile the platform can offer. A tile with a module key appears only
   * when the Samaaj has that module switched on; one without a key is core and
   * always shown.
   */
  private static readonly AllTiles: readonly ModuleTile[] = [
    {
      moduleKey: null,
      title: 'Members',
      description: 'Explore the Samaaj member directory.',
      action: 'Open',
      route: null,
    },
    {
      moduleKey: null,
      title: 'Family',
      description: 'Family members linked to your profile.',
      action: 'Manage',
      route: null,
    },
    {
      moduleKey: 'Events',
      title: 'Events',
      description: 'Upcoming community events.',
      action: 'View',
      route: null,
    },
    {
      moduleKey: 'Pathshala',
      title: 'Pathshala',
      description: "Manage your child's enrollment and education.",
      action: 'Open',
      route: null,
    },
    {
      moduleKey: 'VolunteerGroups',
      title: 'Volunteer',
      description: 'Find groups and apply to join.',
      action: 'Explore',
      route: null,
    },
    {
      moduleKey: 'Boli',
      title: 'Boli',
      description: 'View active and published Boli results.',
      action: 'Open',
      route: null,
    },
  ];

  readonly tiles = computed(() => {
    const enabled = this.samaaj()?.enabledModules ?? [];

    return HomeComponent.AllTiles.filter(
      (tile) =>
        tile.moduleKey === null ||
        enabled.some((module) => module.toLowerCase() === tile.moduleKey!.toLowerCase()),
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

  open(route: string): void {
    void this.router.navigateByUrl(route);
  }

  signOut(): void {
    this.auth.signOut();
    void this.router.navigate(['/login']);
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

  private loadNotifications(): void {
    this.http.get<unknown[]>('/v1/notifications').subscribe({
      next: (found) => this.unreadNotifications.set(found.length),
      error: () => this.unreadNotifications.set(0),
    });
  }
}
