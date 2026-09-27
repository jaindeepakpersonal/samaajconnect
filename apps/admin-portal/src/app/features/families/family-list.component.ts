import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService, DEFAULT_PAGE_SIZE, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { FamilyOverview } from '../../core/admin.models';
import { ALL_LOCALITIES, LocalityFilterComponent } from '../../core/locality-filter.component';
import { LocalityOptions } from '../../core/locality-options';

/**
 * Families &amp; Children: this Samaaj's households, each with its members and
 * its minor children, narrowed by locality.
 *
 * **A household is found by where its members live.** A family has no
 * locality of its own; member-family-service matches one whose active members
 * include somebody with a permanent address there, so a family split across
 * two localities is under both. Unlike the other module lists this filter is
 * the server's, because the list is paged and the locality is not on the row.
 *
 * **An Area Admin's filter is fixed to their locality**, the Members screen's
 * rule: the service refuses them any other, so the screen does not offer one.
 *
 * **Read-only, and a child is an age.** Who belongs to a household is its
 * admin's to decide, and a child's record its household's to keep; this screen
 * shows them so an administrator can find them, and the date of birth never
 * leaves the service. The conversion queue - the one household decision that is
 * an administrator's - is a link from here.
 */
@Component({
  selector: 'app-family-list',
  imports: [LocalityFilterComponent, RouterLink],
  template: `
    <h1 class="title">Families &amp; Children</h1>
    <p class="sub">
      Households in {{ scope.label() }}, each with its members and children.
      @if (!areaLocalityId()) {
        Converting a child who has turned 18 is on the
        <a routerLink="/conversions">adult child conversion queue</a>.
      }
    </p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (needsSamaaj()) {
      <p class="notice">Households belong to a Samaaj. Choose one in the top bar.</p>
    } @else {
      <div class="card">
        <app-locality-filter
          inputId="families-locality"
          [value]="localityFilter()"
          (valueChange)="setLocality($event)"
          [offerAll]="!areaLocalityId()"
          [offerWholeSamaaj]="false"
          [disabled]="!!areaLocalityId()"
        />

        @if (loading()) {
          <p class="empty" role="status">Loading…</p>
        } @else if (families().length === 0) {
          <p class="empty">
            {{ localityFilter() ? 'No household has a member living in that locality.' : 'No households yet.' }}
          </p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">Households, with their members and children</caption>
              <thead>
                <tr><th>Family code</th><th>Members</th><th>Children</th></tr>
              </thead>
              <tbody>
                @for (family of families(); track family.id) {
                  <tr>
                    <td><span class="code">{{ family.familyCode }}</span></td>
                    <td>
                      <ul class="people">
                        @for (member of family.members; track member.memberId) {
                          <li>
                            <a [routerLink]="['/members', member.memberId]">{{ member.fullName }}</a>
                            @if (member.isHead) {
                              <span class="pill ok">Head</span>
                            }
                            <span class="small">
                              · {{ member.relationship }} · {{ placeOf(member.localityId) }}
                            </span>
                          </li>
                        }
                      </ul>
                    </td>
                    <td>
                      @if (family.children.length === 0) {
                        <span class="small">None</span>
                      } @else {
                        <ul class="people">
                          @for (child of family.children; track child.id) {
                            <li>{{ child.fullName }} <span class="small">· {{ child.age }} · {{ child.gender }}</span></li>
                          }
                        </ul>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <nav class="pager" aria-label="Household pages">
            <p class="pager-summary">Page {{ page() }}</p>
            <div class="pager-controls">
              <button class="pager-btn" type="button" [disabled]="page() <= 1 || loading()"
                (click)="goTo(page() - 1)">
                Previous
              </button>
              <button class="pager-btn" type="button" [disabled]="!hasMore() || loading()"
                (click)="goTo(page() + 1)">
                Next
              </button>
            </div>
          </nav>
        }
      </div>
    }
  `,
  styles: `
    .people {
      list-style: none;
      margin: 0;
      padding: 0;
    }

    .people li + li {
      margin-top: var(--space-1);
    }
  `,
})
export class FamilyListComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);

  readonly scope = inject(AdminScope);
  readonly localities = inject(LocalityOptions);

  readonly families = signal<readonly FamilyOverview[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly page = signal(1);
  readonly hasMore = signal(false);

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  readonly areaLocalityId = computed(() => this.auth.user()?.areaLocalityId ?? null);

  /** Fixed to an Area Admin's locality; currentUserGuard has loaded /me before this screen exists. */
  readonly localityFilter = signal<string>(this.areaLocalityId() ?? ALL_LOCALITIES);

  /** Where a member lives, or that they have not said. */
  placeOf(localityId: string | null): string {
    return localityId ? this.localities.label(localityId) : 'No locality';
  }

  ngOnInit(): void {
    this.localities.load();

    if (this.needsSamaaj()) {
      this.loading.set(false);
      return;
    }

    this.goTo(1);
  }

  setLocality(localityId: string): void {
    this.localityFilter.set(localityId);
    this.goTo(1);
  }

  goTo(page: number): void {
    this.page.set(page);
    this.loading.set(true);
    this.error.set(null);

    const locality = this.localityFilter() || null;

    this.api.listFamilies(locality, (page - 1) * DEFAULT_PAGE_SIZE, DEFAULT_PAGE_SIZE).subscribe({
      next: (found) => {
        this.families.set(found.items);
        this.hasMore.set(found.hasMore);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }
}
