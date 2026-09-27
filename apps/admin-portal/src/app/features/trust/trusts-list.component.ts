import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Trust, TrustStatus } from '../../core/admin.models';
import { isNotFound } from '../../core/http-status';
import { LocalityOptions } from '../../core/locality-options';
import { TrustDialogComponent } from './trust-dialog.component';

/**
 * A Samaaj's local Trusts - one per locality - set up and corrected in a
 * popup, the way Events are.
 *
 * No wireframe covers this: it is a new module.
 *
 * **Exactly one Trust per locality, enforced by the service, not this
 * screen.** `CreateTrustCommand` refuses a second Trust on a locality already
 * claimed with a 409 - the dialog shows the service's own refusal rather than
 * pre-checking.
 *
 * **Membership, positions, elections and the money live on the detail
 * screen.** This one is the directory, the popup, and the on/off switch.
 */
@Component({
  selector: 'app-trusts-list',
  imports: [RouterLink, TrustDialogComponent],
  template: `
    <div class="head">
      <div>
        <h1 class="title">Trusts</h1>
        <p class="sub">
          A Samaaj's local Trust, one per locality. Run its roster, elections and money from
          its own page.
        </p>
      </div>

      @if (!moduleOff() && !loading()) {
        <button class="btn" type="button" (click)="openCreate($event)">Set up a Trust</button>
      }
    </div>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    @if (moduleOff()) {
      <p class="notice">
        {{ scope.label() }} does not run the Trust module. Switch it on under the Samaaj's
        settings.
      </p>
    } @else if (loading()) {
      <p class="empty" role="status">Loading…</p>
    } @else {
      <div class="card">
        <h2>Trusts</h2>

        @if (trusts().length === 0) {
          <p class="empty">None yet. Set one up for a locality to start its roster.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <caption class="sr-only">This Samaaj's Trusts</caption>
              <thead>
                <tr><th>Name</th><th>Locality</th><th>Status</th><th></th></tr>
              </thead>
              <tbody>
                @for (trust of trusts(); track trust.id) {
                  <tr>
                    <td><a [routerLink]="['/trust', trust.id]"><b>{{ trust.name }}</b></a></td>
                    <td>{{ localities.label(trust.localityId) }}</td>
                    <td>
                      <span class="pill" [class.warn]="trust.status === 'Inactive'">
                        {{ trust.status }}
                      </span>
                    </td>
                    <td class="row-actions">
                      <button class="btn small alt" type="button" (click)="openEdit(trust, $event)">
                        Edit
                      </button>
                      @if (trust.status === 'Active') {
                        <button class="btn small alt" type="button" [disabled]="busy()"
                          (click)="setStatus(trust, 'Inactive')">
                          Deactivate
                        </button>
                      } @else {
                        <button class="btn small" type="button" [disabled]="busy()"
                          (click)="setStatus(trust, 'Active')">
                          Reactivate
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    }

    @if (dialogOpen()) {
      <app-trust-dialog [trust]="editing()" (saved)="onSaved($event)" (closed)="closeDialog()" />
    }
  `,
  styles: `
    .head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
    }
  `,
})
export class TrustsListComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly scope = inject(AdminScope);
  readonly localities = inject(LocalityOptions);

  readonly trusts = signal<readonly Trust[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly moduleOff = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  readonly dialogOpen = signal(false);
  readonly editing = signal<Trust | null>(null);
  private dialogTrigger: HTMLElement | null = null;

  ngOnInit(): void {
    this.localities.load();
    this.load();
  }

  openCreate(event: MouseEvent): void {
    this.editing.set(null);
    this.openDialog(event);
  }

  openEdit(trust: Trust, event: MouseEvent): void {
    this.editing.set(trust);
    this.openDialog(event);
  }

  onSaved(message: string): void {
    this.closeDialog();
    this.done.set(message);
    this.load();
  }

  closeDialog(): void {
    this.dialogOpen.set(false);

    const trigger = this.dialogTrigger;
    this.dialogTrigger = null;

    if (trigger) {
      setTimeout(() => trigger.focus());
    }
  }

  setStatus(trust: Trust, status: TrustStatus): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.changeTrustStatus(trust.id, status).subscribe({
      next: () => {
        this.done.set(status === 'Inactive' ? `${trust.name} is deactivated.` : `${trust.name} is active again.`);
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  private openDialog(event: MouseEvent): void {
    this.dialogTrigger = event.currentTarget as HTMLElement;
    this.done.set(null);
    this.dialogOpen.set(true);
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.listTrusts().subscribe({
      next: (found) => {
        this.trusts.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        if (isNotFound(failure)) {
          this.moduleOff.set(true);
        } else {
          this.error.set(describeError(failure));
        }

        this.loading.set(false);
      },
    });
  }
}
