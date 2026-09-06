import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin, map, of, switchMap } from 'rxjs';
import { AuthService } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';

/**
 * Dashboard, from the admin wireframe's `#dashboard` screen.
 *
 * The wireframe's six tiles read 12 Samaaj, 18,420 members, 37 pending
 * approvals, 26 events, 2,180 students, 8 auctions. A platform-wide member
 * total has no service behind it - member-family-service's directory is a
 * cursor page with no count, by design, the same reason the tenant list
 * dropped its own Members column - so that tile stays "conversions awaiting a
 * decision" and "invitations not yet redeemed" instead, the two member-shaped
 * counts this panel can actually answer. Events, Pathshala students and
 * auctions all *can* be answered now: each is exactly the list its own screen
 * already loads, counted rather than rendered.
 */
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  template: `
    <h1 class="title">Dashboard</h1>
    <p class="sub">{{ subtitle() }}</p>

    <div class="grid">
      @if (isSuperAdmin()) {
        <a class="card tile" routerLink="/tenants">
          <div class="muted">Samaaj on the platform</div>
          <div class="num">{{ tenantCount() ?? '—' }}</div>
          @if (activeCount() !== null) {
            <div class="muted">{{ activeCount() }} active</div>
          }
        </a>
      }

      <a class="card tile" routerLink="/conversions">
        <div class="muted">Conversions awaiting a decision</div>
        <div class="num">{{ conversionCount() ?? '—' }}</div>
        <div class="muted">Children who have turned 18</div>
      </a>

      <a class="card tile" routerLink="/admins">
        <div class="muted">Invitations not yet redeemed</div>
        <div class="num">{{ pendingCount() ?? '—' }}</div>
        <div class="muted">Accounts that cannot be signed into yet</div>
      </a>

      <a class="card tile" routerLink="/events">
        <div class="muted">Upcoming events</div>
        <div class="num">{{ eventCount() ?? '—' }}</div>
      </a>

      <a class="card tile" routerLink="/pathshala">
        <div class="muted">Students enrolled</div>
        <div class="num">{{ studentCount() ?? '—' }}</div>
        <div class="muted">Across every Pathshala this Samaaj runs</div>
      </a>

      <a class="card tile" routerLink="/boli">
        <div class="muted">Auctions</div>
        <div class="num">{{ auctionCount() ?? '—' }}</div>
      </a>
    </div>

    @if (needsSamaaj()) {
      <p class="notice">
        You are on the platform view. Choose a Samaaj in the top bar to see its queues — the
        counts above that belong to a Samaaj are blank until you do.
      </p>
    }

    <div class="grid2 next">
      <div class="card">
        <h2>Where to start</h2>
        @if (isSuperAdmin()) {
          <p class="muted">Create a Samaaj, activate it, and invite its first administrator.</p>
          <div class="actions">
            <a class="btn" routerLink="/tenants">Samaaj</a>
            <a class="btn alt" routerLink="/admins/invite">Invite an admin</a>
          </div>
        } @else {
          <p class="muted">Manage who administers your Samaaj, and decide conversion requests.</p>
          <div class="actions">
            <a class="btn" routerLink="/admins">Admin users</a>
            <a class="btn alt" routerLink="/conversions">Conversion queue</a>
          </div>
        }
      </div>
    </div>
  `,
  styles: `
    .tile {
      text-decoration: none;
      color: inherit;
      display: block;
    }

    .tile:hover {
      border-color: var(--accent);
    }

    .next {
      margin-top: 15px;
    }
  `,
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly scope = inject(AdminScope);

  readonly tenantCount = signal<number | null>(null);
  readonly activeCount = signal<number | null>(null);
  readonly conversionCount = signal<number | null>(null);
  readonly pendingCount = signal<number | null>(null);
  readonly eventCount = signal<number | null>(null);
  readonly studentCount = signal<number | null>(null);
  readonly auctionCount = signal<number | null>(null);

  readonly isSuperAdmin = computed(() => this.auth.roles().includes('SuperAdmin'));

  readonly needsSamaaj = computed(() => this.isSuperAdmin() && this.scope.tenantId() === null);

  readonly subtitle = computed(() =>
    this.isSuperAdmin() && this.scope.tenantId() === null
      ? 'Platform overview. Choose a Samaaj in the top bar to work inside one.'
      : `Administration workspace for ${this.scope.label()}.`,
  );

  ngOnInit(): void {
    if (this.isSuperAdmin()) {
      this.api.listTenants().subscribe({
        next: (tenants) => {
          this.tenantCount.set(tenants.length);
          this.activeCount.set(tenants.filter((t) => t.status === 'Active').length);
        },

        // A tile that cannot be filled stays blank. Showing a zero would be a
        // claim, and "we could not reach the service" is not zero.
        error: () => undefined,
      });
    }

    if (this.needsSamaaj()) {
      return;
    }

    this.api.listConversionRequests().subscribe({
      next: (requests) => this.conversionCount.set(requests.length),
      error: () => undefined,
    });

    this.api.listPendingActivations().subscribe({
      next: (pending) => this.pendingCount.set(pending.length),
      error: () => undefined,
    });

    // Each of these three is exactly the call its own screen already makes
    // to list what a Samaaj runs - counted, not rendered. Not gated behind
    // isSuperAdmin(): a Samaaj Admin's token already names one Samaaj, and a
    // Super Admin only reaches this line once they have picked one (see
    // needsSamaaj() above).
    this.api.listEvents(true, false).subscribe({
      next: (events) => this.eventCount.set(events.length),
      error: () => undefined,
    });

    this.api.listOccasions().subscribe({
      next: (occasions) => this.auctionCount.set(occasions.length),
      error: () => undefined,
    });

    // No endpoint answers "how many students" directly, and deliberately so:
    // ListPathshalasQuery is gated on Members.Read alone, which every member
    // holds, and member-portal's own Pathshala list calls the same endpoint
    // for a parent - a roll size is a fact about other people's children, so
    // the response nobody is supposed to see it in stays the response nobody
    // is supposed to see it in. The class detail *does* carry a per-class
    // count (active enrolments), reached one Pathshala at a time the way an
    // administrator already opens one from the directory screen - this tile
    // is that same call made for every Pathshala the Samaaj runs rather than
    // one, which costs one request per Pathshala. A Samaaj running many at
    // once would be the reason to add a manager-only aggregate endpoint
    // instead; nothing in this platform's seed or smoke data suggests that is
    // the common case yet.
    this.api
      .listPathshalas()
      .pipe(
        switchMap((pathshalas) =>
          pathshalas.length === 0
            ? of(0)
            : forkJoin(pathshalas.map((p) => this.api.pathshala(p.id))).pipe(
                map((details) =>
                  details.reduce(
                    (total, detail) =>
                      total + detail.classes.reduce((sum, c) => sum + c.studentCount, 0),
                    0,
                  ),
                ),
              ),
        ),
      )
      .subscribe({
        next: (count) => this.studentCount.set(count),
        error: () => undefined,
      });
  }
}
