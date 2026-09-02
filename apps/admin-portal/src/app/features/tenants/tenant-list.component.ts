import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { ModuleDescriptor, Tenant, TenantStatus } from '../../core/admin.models';

/**
 * Samaaj / Tenants, from the admin wireframe's `#tenants` screen.
 *
 * Two things the wireframe shows are deliberately not here.
 *
 * The **Subdomain** column is gone. The platform runs on one domain now (root
 * `CLAUDE.md` §6), so a column of `x.samaajconnect.com` would name addresses
 * that do not resolve. The slug is what identifies a Samaaj and is shown
 * instead.
 *
 * The **Members** count is gone. That number lives in member-family-service,
 * and fetching it would mean one cross-service call per row for a column
 * nobody acts on - the repo avoids exactly that kind of synchronous reach
 * across a service boundary. It comes back when there is one call that can
 * answer it.
 */
@Component({
  selector: 'app-tenant-list',
  imports: [FormsModule, RouterLink],
  template: `
    <h1 class="title">Samaaj / Tenants</h1>
    <p class="sub">Super Admin only • manage all tenant organisations.</p>

    <div class="actions">
      <a class="btn" routerLink="/tenants/new">+ Create Samaaj</a>
    </div>

    <div class="card filters">
      <div class="filter-row">
        <div>
          <label for="search">Search</label>
          <input
            id="search"
            class="input"
            type="search"
            placeholder="Name or slug"
            [(ngModel)]="search"
            (keyup.enter)="load()"
          />
        </div>
        <div>
          <label for="status">Status</label>
          <select id="status" class="input" [(ngModel)]="status" (change)="load()">
            <option value="">Any status</option>
            <option value="Active">Active</option>
            <option value="Inactive">Inactive</option>
            <option value="Archived">Archived</option>
          </select>
        </div>
        <button class="btn alt" type="button" (click)="load()">Apply</button>
      </div>
    </div>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (loading()) {
      <p class="empty" role="status">Loading Samaaj…</p>
    } @else if (tenants().length === 0) {
      <p class="empty">
        No Samaaj match that filter.
        @if (search || status) {
          <button class="btn link" type="button" (click)="clearFilters()">Clear the filter</button>
        }
      </p>
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">Samaaj on this platform</caption>
          <thead>
            <tr>
              <th>Samaaj</th>
              <th>Slug</th>
              <th>Modules</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            @for (tenant of tenants(); track tenant.id) {
              <tr>
                <td>
                  <b>{{ tenant.name }}</b>
                  @if (tenant.contactPerson) {
                    <div class="muted">{{ tenant.contactPerson }}</div>
                  }
                </td>
                <td>{{ tenant.slug }}</td>
                <td>
                  @if (tenant.enabledModules.length === 0) {
                    <span class="muted">None</span>
                  } @else {
                    <div class="pills">
                      @for (key of tenant.enabledModules; track key) {
                        <span class="pill">{{ moduleLabel(key) }}</span>
                      }
                    </div>
                  }
                </td>
                <td>
                  <span class="pill" [class.ok]="tenant.status === 'Active'"
                    [class.off]="tenant.status !== 'Active'">{{ tenant.status }}</span>
                </td>
                <td>
                  <div class="row-actions">
                    @if (tenant.status === 'Inactive') {
                      <button
                        class="btn small"
                        type="button"
                        [disabled]="busyId() === tenant.id"
                        (click)="setStatus(tenant, 'Active')"
                      >
                        Activate
                      </button>
                    } @else if (tenant.status === 'Active') {
                      <button
                        class="btn alt small"
                        type="button"
                        [disabled]="busyId() === tenant.id"
                        (click)="askToConfirm(tenant)"
                      >
                        Deactivate
                      </button>
                      <button
                        class="btn alt small"
                        type="button"
                        (click)="act(tenant)"
                      >
                        Open
                      </button>
                    }

                    @if (tenant.status !== 'Archived') {
                      <button
                        class="btn small"
                        type="button"
                        (click)="openModules(tenant)"
                      >
                        Modules
                      </button>
                    }
                  </div>

                  @if (confirming() === tenant.id) {
                    <form class="confirm" (ngSubmit)="confirmDeactivate(tenant)">
                      <p class="small">
                        Deactivating {{ tenant.name }} signs out every one of its members and
                        stops the whole Samaaj serving. Enter your own password to confirm.
                      </p>
                      <label [for]="tenant.id + 'pwd'">Your password</label>
                      <input
                        class="input"
                        type="password"
                        autocomplete="current-password"
                        [id]="tenant.id + 'pwd'"
                        [(ngModel)]="password"
                        name="password"
                        required
                      />
                      @if (confirmError()) {
                        <p class="small error" role="alert">{{ confirmError() }}</p>
                      }
                      <div class="actions">
                        <button class="btn small" type="submit" [disabled]="busyId() === tenant.id">
                          Deactivate
                        </button>
                        <button class="btn alt small" type="button" (click)="cancelConfirm()">
                          Cancel
                        </button>
                      </div>
                    </form>
                  }

                  @if (editing() === tenant.id) {
                    <div class="modules">
                      <p class="small">
                        Switching a module off makes every screen in it answer “not found” for
                        everyone in this Samaaj. Saved as one set.
                      </p>
                      @for (module of modules(); track module.key) {
                        <div class="toggle-row">
                          <label [for]="tenant.id + module.key">{{ module.label }}</label>
                          <input
                            type="checkbox"
                            [id]="tenant.id + module.key"
                            [checked]="draft().includes(module.key)"
                            (change)="toggle(module.key)"
                          />
                        </div>
                      }
                      <div class="actions">
                        <button
                          class="btn small"
                          type="button"
                          [disabled]="busyId() === tenant.id"
                          (click)="saveModules(tenant)"
                        >
                          {{ busyId() === tenant.id ? 'Saving…' : 'Save modules' }}
                        </button>
                        <button class="btn alt small" type="button" (click)="editing.set(null)">
                          Cancel
                        </button>
                      </div>
                    </div>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styles: `
    .filters {
      margin-bottom: var(--space-4);
    }

    .filter-row {
      display: flex;
      gap: var(--space-3);
      align-items: end;
      flex-wrap: wrap;
    }

    .filter-row > div {
      flex: 1 1 200px;
    }

    .filter-row label {
      margin-top: 0;
    }

    .filter-row .input {
      margin-bottom: 0;
    }

    .row-actions {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
    }

    .modules {
      margin-top: var(--space-3);
      padding-top: var(--space-2);
      border-top: 1px solid var(--line-soft);
      min-width: 260px;
    }

    .confirm {
      margin-top: var(--space-3);
      padding-top: var(--space-2);
      border-top: 1px solid var(--line-soft);
      min-width: 260px;
    }

    .pills {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
    }
  `,
})
export class TenantListComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly scope = inject(AdminScope);

  readonly tenants = signal<readonly Tenant[]>([]);
  readonly modules = signal<readonly ModuleDescriptor[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /** Which row has its module toggles open. */
  readonly editing = signal<string | null>(null);

  /** Unsaved module selection for the open row. */
  readonly draft = signal<readonly string[]>([]);

  readonly busyId = signal<string | null>(null);

  /**
   * The Samaaj whose deactivation is waiting on a password, if any.
   * Deactivating signs out every member of that Samaaj, so the server re-asks
   * for the administrator's own password and this is the panel that collects
   * it.
   */
  readonly confirming = signal<string | null>(null);

  /** Kept apart from `error` so a wrong password lands next to the field. */
  readonly confirmError = signal<string | null>(null);

  password = '';

  search = '';
  status: TenantStatus | '' = '';

  ngOnInit(): void {
    this.api.listModules().subscribe({
      next: (modules) => this.modules.set(modules),

      // The list still works without labels; the toggles are what need them,
      // and those are hidden until a row is opened.
      error: () => this.modules.set([]),
    });

    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.editing.set(null);

    this.api
      .listTenants({ status: this.status === '' ? null : this.status, search: this.search })
      .subscribe({
        next: (tenants) => {
          this.tenants.set(tenants);
          this.loading.set(false);
        },
        error: (failure: unknown) => {
          this.error.set(describeError(failure));
          this.loading.set(false);
        },
      });
  }

  clearFilters(): void {
    this.search = '';
    this.status = '';
    this.load();
  }

  moduleLabel(key: string): string {
    return this.modules().find((m) => m.key === key)?.label ?? key;
  }

  /** Switch the panel's scope to this Samaaj - the wireframe's "Open". */
  act(tenant: Tenant): void {
    this.scope.select(tenant);
  }

  /**
   * Opens the toggle panel, seeded from what this Samaaj runs right now. The
   * draft is a copy: nothing is sent until Save, so cancelling really cancels.
   */
  openModules(tenant: Tenant): void {
    if (this.editing() === tenant.id) {
      this.editing.set(null);
      return;
    }

    this.draft.set([...tenant.enabledModules]);
    this.editing.set(tenant.id);
  }

  toggle(key: string): void {
    const current = this.draft();

    this.draft.set(
      current.includes(key) ? current.filter((k) => k !== key) : [...current, key],
    );
  }

  setStatus(tenant: Tenant, status: TenantStatus): void {
    this.busyId.set(tenant.id);
    this.error.set(null);

    this.api.changeTenantStatus(tenant.id, status).subscribe({
      next: (updated) => this.replace(updated),
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busyId.set(null);
      },
    });
  }

  /** Deactivating asks before it acts, rather than acting on one click. */
  askToConfirm(tenant: Tenant): void {
    this.password = '';
    this.confirmError.set(null);
    this.confirming.set(this.confirming() === tenant.id ? null : tenant.id);
  }

  cancelConfirm(): void {
    this.password = '';
    this.confirmError.set(null);
    this.confirming.set(null);
  }

  confirmDeactivate(tenant: Tenant): void {
    this.busyId.set(tenant.id);
    this.confirmError.set(null);

    this.api.changeTenantStatus(tenant.id, 'Inactive', this.password).subscribe({
      next: (updated) => {
        this.cancelConfirm();
        this.replace(updated);
      },
      error: (failure: unknown) => {
        // Left open, with the message beside the field. Closing the panel on a
        // wrong password would make the admin start again to correct a typo.
        this.confirmError.set(describeError(failure));
        this.busyId.set(null);
      },
    });
  }

  saveModules(tenant: Tenant): void {
    this.busyId.set(tenant.id);
    this.error.set(null);

    this.api.setTenantModules(tenant.id, this.draft()).subscribe({
      next: (updated) => {
        this.replace(updated);
        this.editing.set(null);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busyId.set(null);
      },
    });
  }

  private replace(updated: Tenant): void {
    this.tenants.set(this.tenants().map((t) => (t.id === updated.id ? updated : t)));
    this.busyId.set(null);
  }
}
