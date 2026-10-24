import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { Observable } from 'rxjs';
import { AdminApi } from '../../core/admin-api';
import { TrustMember, TrustRole } from '../../core/admin.models';
import { TrustMemberDialogComponent } from './trust-member-dialog.component';

/**
 * A Trust's roster: who is on it, the role each holds, and adding and removing.
 *
 * **Every trustee holds one role**, Trustee unless they hold another. The
 * picker on a row offers a role only while it has a free seat - the service
 * refuses a full one - and moving somebody by hand is the other way a role is
 * filled besides an election.
 *
 * **Removing has no confirmation.** It is reversible: adding the same member
 * back reactivates their old row rather than starting a new one.
 */
@Component({
  selector: 'app-trust-members',
  imports: [FormsModule, TrustMemberDialogComponent, RouterLink],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    <div class="card">
      <div class="section-head">
        <h2>Members</h2>
        <button class="btn" type="button" (click)="openDialog($event)">Add a trustee</button>
      </div>

      @if (loading()) {
        <p class="empty" role="status">Loading…</p>
      } @else if (members().length === 0) {
        <p class="empty">Nobody added yet.</p>
      } @else {
        <p class="small">
          {{ activeCount() }} trustee{{ activeCount() === 1 ? '' : 's' }}. Removing keeps the row:
          adding the same member back later reactivates it.
        </p>

        <div class="table-wrap">
          <table>
            <caption class="sr-only">This Trust's members and their roles</caption>
            <thead>
              <tr><th>Member</th><th>Role</th><th>Status</th><th></th></tr>
            </thead>
            <tbody>
              @for (member of members(); track member.id) {
                <tr>
                  <td><b><a [routerLink]="['/members', member.memberId]">{{ name(member.memberId) }}</a></b></td>
                  <td>
                    @if (member.status === 'Active') {
                      <label class="sr-only" [for]="'role-' + member.id">Role of {{ name(member.memberId) }}</label>
                      <select class="input inline" [id]="'role-' + member.id" [disabled]="busy()"
                        [ngModel]="member.roleId" (ngModelChange)="assign(member, $event)">
                        @for (role of rolesFor(member); track role.id) {
                          <option [value]="role.id">{{ role.title }}</option>
                        }
                      </select>
                    } @else {
                      {{ member.positionTitle ?? '—' }}
                    }
                  </td>
                  <td>
                    <span class="pill" [class.warn]="member.status === 'Removed'">{{ member.status }}</span>
                  </td>
                  <td>
                    @if (member.status === 'Active') {
                      <button class="btn small alt" type="button" [disabled]="busy()" (click)="remove(member)">
                        Remove<span class="sr-only"> {{ name(member.memberId) }}</span>
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

    @if (adding()) {
      <app-trust-member-dialog
        [trustId]="trustId()"
        [roles]="roles()"
        [onRoster]="activeMemberIds()"
        (saved)="added($event)"
        (closed)="closeDialog()"
      />
    }
  `,
  styles: `
    .section-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .section-head h2 {
      margin: 0;
    }
  `,
})
export class TrustMembersComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly trustId = input.required<string>();

  /** Member names from the page's one directory call. */
  readonly names = input<ReadonlyMap<string, string>>(new Map());

  readonly members = signal<readonly TrustMember[]>([]);
  readonly roles = signal<readonly TrustRole[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly adding = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  private dialogTrigger: HTMLElement | null = null;

  readonly activeMemberIds = computed(
    () => new Set(this.members().filter((m) => m.status === 'Active').map((m) => m.memberId)),
  );

  readonly activeCount = computed(() => this.activeMemberIds().size);

  ngOnInit(): void {
    this.load();
  }

  name(memberId: string): string {
    return this.names().get(memberId) ?? 'A member';
  }

  /** Their own role, and every other with a free seat. */
  rolesFor(member: TrustMember): readonly TrustRole[] {
    return this.roles().filter(
      (role) => role.id === member.roleId || role.seats === null || role.holders < role.seats,
    );
  }

  private load(): void {
    this.api.listTrustMembers(this.trustId()).subscribe({
      next: (found) => {
        this.members.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });

    this.loadRoles();
  }

  private loadRoles(): void {
    this.api.listTrustRoles(this.trustId()).subscribe({
      next: (found) => this.roles.set(found),
      error: (failure: unknown) => this.error.set(describeError(failure)),
    });
  }

  assign(member: TrustMember, roleId: string): void {
    if (roleId === member.roleId) {
      return;
    }

    const title = this.roles().find((role) => role.id === roleId)?.title ?? 'the role';

    this.act(
      this.api.assignTrustRole(this.trustId(), member.id, roleId),
      `${this.name(member.memberId)} is now ${title}.`,
    );
  }

  remove(member: TrustMember): void {
    this.act(
      this.api.removeTrustMember(this.trustId(), member.id),
      `${this.name(member.memberId)} is off the roster.`,
    );
  }

  openDialog(event: MouseEvent): void {
    this.dialogTrigger = event.currentTarget as HTMLElement;
    this.adding.set(true);
  }

  added(message: string): void {
    this.closeDialog();
    this.error.set(null);
    this.done.set(message);
    this.load();
  }

  /** Focus goes back to the button that opened the dialog. */
  closeDialog(): void {
    this.adding.set(false);

    const trigger = this.dialogTrigger;
    this.dialogTrigger = null;

    if (trigger) {
      setTimeout(() => trigger.focus());
    }
  }

  private act(work: Observable<unknown>, message: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    work.subscribe({
      next: () => {
        this.done.set(message);
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
        // A refused change re-reads the roster, so the select shows what is true.
        this.load();
      },
    });
  }
}
