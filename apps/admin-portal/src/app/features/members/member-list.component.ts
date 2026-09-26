import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService, DEFAULT_PAGE_SIZE, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AdminMember, Locality } from '../../core/admin.models';

/**
 * Members, from the admin wireframe's `#members` screen.
 *
 * **This nav item said "not built" while the permission behind it was already
 * granted.** `Members.Write` has been on SamaajAdmin since the authorization
 * catalogue was seeded, and `SERVICES.md` has always said an administrator
 * holding it may correct anyone's profile in their Samaaj. There was nowhere to
 * do it, and — see `member-detail.component.ts` — the endpoint that existed
 * could not have been used correctly if there had been.
 *
 * Two of the wireframe's five columns are not here, and both absences are the
 * same rule the rest of this panel follows.
 *
 * **ID** ("MEM-00124") is not a thing this platform has. A member's id is the
 * GUID from identity-tenant-service, which is not something an administrator
 * reads out over the phone, and inventing a short code would be inventing an
 * identifier nothing else on the platform knows.
 *
 * **Family** would be one call per row into a household this endpoint does not
 * return — and a household's membership is other members' data, so exposing it
 * on a directory row is a privacy decision rather than a layout one. Locality
 * is what the search actually filters on, so it earns the column instead.
 *
 * **Status** is not member-family-service's to answer either: whether an account
 * is activated lives in identity-tenant-service, and the pending ones already
 * have a screen under Admin Users.
 *
 * **The Locality filter is a dropdown now, not a text box.** Locality moved
 * from free text to a master-data-service pick on 2026-09-11, so the search
 * takes an exact `localityId` rather than a substring - a member picks one
 * Locality, an administrator filters by picking the same one. The full
 * Locality list is fetched once, from master-data-service, to resolve both
 * the dropdown's options and the Locality column's ids to names.
 */
@Component({
  selector: 'app-member-list',
  imports: [FormsModule, RouterLink],
  template: `
    <h1 class="title">Members</h1>
    <p class="sub">
      Everyone in this Samaaj, including members who have taken themselves out of the
      directory. Open one to correct their details.
    </p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (needsSamaaj()) {
      <p class="notice">
        Members belong to a Samaaj. Choose one in the top bar to see its directory.
      </p>
    } @else {
      <div class="card">
        <h2>Find somebody</h2>

        <form (ngSubmit)="search()">
          <div class="row">
            <div class="field">
              <label for="term">Name</label>
              <input
                class="input"
                id="term"
                name="term"
                [(ngModel)]="term"
                placeholder="Part of a name"
              />
            </div>

            <div class="field">
              <label for="locality">Locality</label>
              <select
                class="input"
                id="locality"
                name="locality"
                [(ngModel)]="localityId"
                [disabled]="areaLocalityId() !== null"
              >
                @if (areaLocalityId() === null) {
                  <option value="">All localities</option>
                }
                @for (option of localities(); track option.id) {
                  <option [value]="option.id">{{ option.name }}</option>
                }
              </select>
            </div>

            <div class="actions">
              <button class="btn" type="submit" [disabled]="loading()">Search</button>
              <button class="btn alt" type="button" (click)="clear()">Clear</button>
            </div>
          </div>
        </form>

        <!--
          No profession box, and that is member-family-service's decision rather
          than a gap here: profession carries a per-field privacy level, and a
          server-side filter on it would let anybody confirm a private value one
          query at a time. An administrator sees the column because they see
          every field; nobody gets to search by it.
        -->
        <p class="small">
          Search matches names and localities. Profession is shown but cannot be searched —
          it carries a privacy level, and a filter on it would confirm private values one
          query at a time.
        </p>
      </div>

      @if (loading()) {
        <p class="empty" role="status">Loading the directory…</p>
      } @else if (members().length === 0) {
        <p class="empty">Nobody in this Samaaj matches that.</p>
      } @else {
        <div class="table-wrap">
          <table>
            <caption class="sr-only">Members of this Samaaj</caption>
            <thead>
              <tr>
                <th>Member</th>
                <th>Locality</th>
                <th>Mobile</th>
                <th>Email</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              @for (member of members(); track member.id) {
                <tr>
                  <td>
                    <b>{{ member.fullName }}</b>
                  </td>
                  <td>{{ localityName(member.permanentAddress.localityId) }}</td>
                  <td>{{ member.mobile ?? '—' }}</td>
                  <td>{{ member.email ?? '—' }}</td>
                  <td>
                    <a class="btn alt" [routerLink]="['/members', member.id]">
                      Open<span class="sr-only"> {{ member.fullName }}</span>
                    </a>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <nav class="pager" aria-label="Member pages">
          <p class="pager-summary">Page {{ page() }}</p>
          <div class="pager-controls">
            <button
              class="pager-btn"
              type="button"
              [disabled]="page() <= 1 || loading()"
              (click)="goTo(page() - 1)"
            >
              Previous
            </button>
            <button
              class="pager-btn"
              type="button"
              [disabled]="!hasMore() || loading()"
              (click)="goTo(page() + 1)"
            >
              Next
            </button>
          </div>
        </nav>
      }
    }
  `,
  styles: `
    .row {
      display: flex;
      gap: 12px;
      flex-wrap: wrap;
      align-items: flex-end;
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
  `,
})
export class MemberListComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly scope = inject(AdminScope);

  readonly members = signal<readonly AdminMember[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly hasMore = signal(false);

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  readonly localities = signal<readonly Locality[]>([]);

  /**
   * An Area Admin administers one locality, and member-family-service gives
   * the administrator's view only to a search confined to it - anything wider
   * is the member directory, which is not what this screen is for. So the
   * filter is fixed to their locality rather than offered.
   */
  readonly areaLocalityId = computed(() => this.auth.user()?.areaLocalityId ?? null);

  term = '';
  localityId = this.areaLocalityId() ?? '';

  ngOnInit(): void {
    this.api.listLocalities().subscribe({ next: (found) => this.localities.set(found), error: () => {} });

    if (this.needsSamaaj()) {
      this.loading.set(false);
      return;
    }

    this.search();
  }

  /** A new search starts again from the first page. */
  search(): void {
    this.goTo(1);
  }

  goTo(page: number): void {
    this.page.set(page);
    this.loading.set(true);
    this.error.set(null);

    this.api
      .searchMembers(this.term, this.localityId || null, (page - 1) * DEFAULT_PAGE_SIZE, DEFAULT_PAGE_SIZE)
      .subscribe({
      next: (found) => {
        this.members.set(found.items);
        this.hasMore.set(found.hasMore);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  clear(): void {
    this.term = '';
    this.localityId = this.areaLocalityId() ?? '';
    this.search();
  }

  localityName(localityId: string | null): string {
    if (localityId === null) {
      return '—';
    }

    return this.localities().find((locality) => locality.id === localityId)?.name ?? '—';
  }
}
