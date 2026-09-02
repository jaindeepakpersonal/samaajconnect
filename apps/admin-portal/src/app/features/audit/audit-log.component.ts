import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { AuditLogEntry } from '../../core/admin.models';

/**
 * Audit Logs, from the admin wireframe's `#audit` screen.
 *
 * The wireframe has a single free-text box "filter by actor, entity, or
 * action". The endpoint filters by action and entity, not by actor, so this
 * offers those two rather than a box that silently ignores what was typed into
 * it. Filtering by actor would also be the wrong shape: audit rows are
 * de-identified when a member is erased, so an actor filter would quietly stop
 * matching rows that are still there.
 *
 * The **Actor** column shows an id, not a name. Resolving it would mean a call
 * per row into identity-tenant-service, and for an erased member there is
 * deliberately nothing left to resolve - the row keeps what happened and loses
 * who, which is the whole design (see `docs/product/DPDP-COMPLIANCE.md`).
 */
@Component({
  selector: 'app-audit-log',
  imports: [FormsModule, DatePipe],
  template: `
    <h1 class="title">Audit Logs</h1>
    <p class="sub">Trace critical administrative activity for {{ scope.label() }}.</p>

    <div class="card filters">
      <div class="filter-row">
        <div>
          <label for="action">Action</label>
          <input
            id="action"
            class="input"
            type="search"
            placeholder="e.g. UserRegistered"
            [(ngModel)]="action"
            (keyup.enter)="load()"
          />
        </div>
        <div>
          <label for="entity">Entity</label>
          <input
            id="entity"
            class="input"
            type="search"
            placeholder="e.g. User"
            [(ngModel)]="entityName"
            (keyup.enter)="load()"
          />
        </div>
        <button class="btn alt" type="button" (click)="load()">Apply</button>
      </div>
    </div>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (needsSamaaj()) {
      <p class="notice">
        Audit rows belong to a Samaaj. Choose one in the top bar to read its log.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading the audit log…</p>
    } @else if (entries().length === 0) {
      <p class="empty">No audit rows match.</p>
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">Audit log entries</caption>
          <thead>
            <tr>
              <th>When</th>
              <th>Action</th>
              <th>Entity</th>
              <th>Actor</th>
              <th>Topic</th>
            </tr>
          </thead>
          <tbody>
            @for (entry of entries(); track entry.id) {
              <tr>
                <td>{{ entry.occurredAt | date: 'd MMM y, HH:mm:ss' }}</td>
                <td>
                  <b>{{ entry.action }}</b>
                </td>
                <td>
                  {{ entry.entityName }}
                  @if (entry.entityId) {
                    <div class="muted id">{{ entry.entityId }}</div>
                  }
                </td>
                <td>
                  @if (entry.actorUserId) {
                    <span class="id">{{ entry.actorUserId }}</span>
                  } @else {
                    <span class="pill off" title="Either a system event, or a member who has since been erased">
                      no actor
                    </span>
                  }
                </td>
                <td class="muted id">{{ entry.topic }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <p class="small">
        The most recent {{ entries().length }} rows. Audit rows are append-only: nothing on this
        platform edits or deletes one, with a single exception — erasing an account removes the
        actor from the rows it left behind, so the fact that something happened survives and the
        person does not.
      </p>
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

    .id {
      font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
      font-size: 11px;
      overflow-wrap: anywhere;
    }
  `,
})
export class AuditLogComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);

  readonly scope = inject(AdminScope);

  readonly entries = signal<readonly AuditLogEntry[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  action = '';
  entityName = '';

  ngOnInit(): void {
    if (this.needsSamaaj()) {
      this.loading.set(false);
      return;
    }

    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.listAuditLogs({ action: this.action, entityName: this.entityName }).subscribe({
      next: (entries) => {
        this.entries.set(entries);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }
}
