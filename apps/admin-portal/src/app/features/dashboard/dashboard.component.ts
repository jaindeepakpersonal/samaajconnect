import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';

/**
 * Dashboard, from the admin wireframe's `#dashboard` screen.
 *
 * The wireframe's six tiles read 12 Samaaj, 18,420 members, 37 pending
 * approvals, 26 events, 2,180 students, 8 auctions. Five of those six have no
 * service behind them, and the wireframe-to-angular skill is explicit that
 * prototype numbers must not be hardcoded - so a tile appears only once
 * something can answer it, and the rest are named as what is still to come
 * rather than quietly dropped.
 *
 * What can be answered today: how many Samaaj there are and how many are
 * active, how many conversion requests are waiting, and how many invited
 * accounts have not yet been activated.
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

      <div class="card">
        <h2>Not built yet</h2>
        <p class="muted">
          The wireframe's dashboard also counts events, Pathshala students and auctions. Those
          services exist now, but each count is a call into a different one, and this panel does
          not reach across service boundaries a tile at a time — the tiles arrive with a
          reporting endpoint that can answer for them.
        </p>
        <div class="actions">
          <a class="btn link" routerLink="/audit">View the audit log →</a>
        </div>
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
  }
}
