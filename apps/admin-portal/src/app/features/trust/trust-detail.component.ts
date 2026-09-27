import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Trust } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';
import { LocalityOptions } from '../../core/locality-options';
import { TrustCollectionsComponent } from './trust-collections.component';
import { TrustDonationsComponent } from './trust-donations.component';
import { TrustElectionsComponent } from './trust-elections.component';
import { TrustExpensesComponent } from './trust-expenses.component';
import { TrustMembersComponent } from './trust-members.component';
import { TrustRolesComponent } from './trust-roles.component';

export type TrustTab = 'members' | 'roles' | 'elections' | 'donations' | 'collections' | 'expenses';

/**
 * One Trust, as six tabs: its members, its roles, the elections that fill
 * them, and its money - donations, collections and expenses.
 *
 * **Each tab is its own component and loads when it is opened**, so opening a
 * Trust costs the Trust and one directory call for names, not six lists. The
 * names are shared: every tab shows people, and none should print a GUID.
 *
 * **Membership is admin-curated, not derived from where a member lives.**
 * `MemberProfile.Locality` says where somebody lives; it says nothing about
 * who administers the Trust there - confirmed with the user rather than
 * assumed (`trust-service`'s own CLAUDE.md).
 *
 * **There is no president-equivalent split here, unlike Volunteer Groups.**
 * A Trust's roles are plural, so every admin-side command stays behind the one
 * `Trust.Manage` permission; the service enforces it, and this page shows the
 * controls.
 */
@Component({
  selector: 'app-trust-detail',
  imports: [
    RouterLink,
    TrustMembersComponent,
    TrustRolesComponent,
    TrustElectionsComponent,
    TrustDonationsComponent,
    TrustExpensesComponent,
    TrustCollectionsComponent,
  ],
  template: `
    <p><a class="btn link" routerLink="/trust">← Trusts</a></p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else if (notFound()) {
      <p class="notice">No such Trust in {{ scope.label() }}, or the module is switched off.</p>
    } @else if (trust(); as found) {
      <h1 class="title">{{ found.name }}</h1>
      <p class="sub">
        <span class="pill" [class.warn]="found.status === 'Inactive'">{{ found.status }}</span>
        · {{ localities.label(found.localityId) }}
      </p>

      <div class="tabs" role="tablist" aria-label="This Trust">
        @for (item of tabs; track item.value) {
          <button
            type="button"
            role="tab"
            [id]="'trust-tab-' + item.value"
            [class.on]="tab() === item.value"
            [attr.aria-selected]="tab() === item.value"
            aria-controls="trust-tab-panel"
            (click)="selectTab(item.value)"
          >
            {{ item.label }}
          </button>
        }
      </div>

      <div id="trust-tab-panel" role="tabpanel" [attr.aria-labelledby]="'trust-tab-' + tab()">
        @switch (tab()) {
          @case ('roles') {
            <app-trust-roles [trustId]="found.id" />
          }
          @case ('elections') {
            <app-trust-elections [trustId]="found.id" [names]="memberNames()" />
          }
          @case ('donations') {
            <app-trust-donations [trustId]="found.id" [names]="memberNames()" />
          }
          @case ('collections') {
            <app-trust-collections [trustId]="found.id" [names]="memberNames()" />
          }
          @case ('expenses') {
            <app-trust-expenses [trustId]="found.id" [names]="memberNames()" />
          }
          @default {
            <app-trust-members [trustId]="found.id" [names]="memberNames()" />
          }
        }
      </div>
    }
  `,
})
export class TrustDetailComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly route = inject(ActivatedRoute);

  readonly scope = inject(AdminScope);
  readonly localities = inject(LocalityOptions);

  readonly trust = signal<Trust | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly error = signal<string | null>(null);

  private readonly names = signal<ReadonlyMap<string, string>>(new Map());

  /** Member names for every tab - trustees, candidates, donors, payers, approvers. */
  readonly memberNames = this.names.asReadonly();

  readonly tabs: readonly { value: TrustTab; label: string }[] = [
    { value: 'members', label: 'Members' },
    { value: 'roles', label: 'Roles' },
    { value: 'elections', label: 'Elections' },
    { value: 'donations', label: 'Donations' },
    { value: 'collections', label: 'Collections' },
    { value: 'expenses', label: 'Expenses' },
  ];

  readonly tab = signal<TrustTab>('members');

  private get id(): string {
    return this.route.snapshot.paramMap.get('id') ?? '';
  }

  ngOnInit(): void {
    this.localities.load();
    this.load();
  }

  selectTab(tab: TrustTab): void {
    this.tab.set(tab);
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.getTrust(this.id).subscribe({
      next: (found) => {
        this.trust.set(found);
        this.loading.set(false);
        this.loadNames();
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.notFound.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }

  /** A failure leaves "A member" rather than a GUID, and is not an error. */
  private loadNames(): void {
    this.api.listMembers().subscribe({
      next: (found) => this.names.set(new Map(found.map((m) => [m.id, m.fullName]))),
      error: () => this.names.set(new Map()),
    });
  }
}
