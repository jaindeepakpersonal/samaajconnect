import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { AuthService } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AttendanceSummary, RegistrationSummary } from '../../core/admin.models';

/**
 * Reports & Analytics, from the admin wireframe's `#reports` screen -
 * its three per-Samaaj summary cards only. The wireframe's fourth piece, a
 * Super-Admin-only cross-tenant comparison table, needs a cross-tenant
 * aggregate read this platform has never done in any service - every query
 * everywhere else is scoped to one tenant by the global query filter - and is
 * a deliberately separate piece of work, not built here.
 *
 * None of the three needed a new service. Registration growth and average
 * attendance needed one small new tenant-scoped query each
 * (`GetRegistrationSummaryQuery` in member-family-service,
 * `GetAttendanceSummaryQuery` in pathshala-service, both admin-only from the
 * start). Registrations this year needed nothing new at all: it is exactly
 * `GET /v1/events`, the same call the dashboard and the Events screen already
 * make, summed instead of rendered.
 */
@Component({
  selector: 'app-reports',
  template: `
    <h1 class="title">Reports & Analytics</h1>
    <p class="sub">Tenant-aware operational reporting.</p>

    @if (needsSamaaj()) {
      <p class="notice">
        You are on the platform view. Choose a Samaaj in the top bar to see its reports.
      </p>
    } @else {
      <div class="grid">
        <div class="card">
          <h2>Members</h2>
          <div class="num">{{ growthLabel() }}</div>
          <p class="muted">Registration growth</p>
        </div>

        <div class="card">
          <h2>Pathshala</h2>
          <div class="num">{{ attendanceLabel() }}</div>
          <p class="muted">Average attendance</p>
        </div>

        <div class="card">
          <h2>Events</h2>
          <div class="num">{{ eventRegistrations() ?? '—' }}</div>
          <p class="muted">Registrations this year</p>
        </div>
      </div>
    }
  `,
})
export class ReportsComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly scope = inject(AdminScope);

  readonly registrationSummary = signal<RegistrationSummary | null>(null);
  readonly attendanceSummary = signal<AttendanceSummary | null>(null);
  readonly eventRegistrations = signal<number | null>(null);

  readonly isSuperAdmin = computed(() => this.auth.roles().includes('SuperAdmin'));

  readonly needsSamaaj = computed(() => this.isSuperAdmin() && this.scope.tenantId() === null);

  readonly growthLabel = computed(() => {
    const pct = this.registrationSummary()?.growthPercentage;

    if (pct === null || pct === undefined) {
      return '—';
    }

    return `${pct > 0 ? '+' : ''}${pct.toFixed(1)}%`;
  });

  readonly attendanceLabel = computed(() => {
    const pct = this.attendanceSummary()?.percentage;

    return pct === null || pct === undefined ? '—' : `${pct}%`;
  });

  ngOnInit(): void {
    if (this.needsSamaaj()) {
      return;
    }

    this.api.registrationSummary().subscribe({
      next: (summary) => this.registrationSummary.set(summary),

      // A card that cannot be filled stays blank. Showing a zero would be a
      // claim, and "we could not reach the service" is not zero.
      error: () => undefined,
    });

    this.api.attendanceSummary().subscribe({
      next: (summary) => this.attendanceSummary.set(summary),
      error: () => undefined,
    });

    const thisYear = new Date().getFullYear();

    // The same list the dashboard and the Events screen already load
    // (includeDrafts, includePast - registrations already taken this year
    // count even for an event that has already happened), summed instead of
    // rendered.
    this.api.listEvents(true, true).subscribe({
      next: (events) => {
        const total = events
          .filter((event) => new Date(event.startAt).getFullYear() === thisYear)
          .reduce((sum, event) => sum + event.registeredCount, 0);

        this.eventRegistrations.set(total);
      },
      error: () => undefined,
    });
  }
}
