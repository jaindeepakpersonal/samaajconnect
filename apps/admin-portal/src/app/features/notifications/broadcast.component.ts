import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { AdminScope } from '../../core/admin-scope';
import { Broadcast } from '../../core/admin.models';

/**
 * Notifications, from the admin wireframe's `#notifications` screen.
 *
 * The wireframe's Compose card has four fields. Two are here and two are not,
 * and both absences are the same rule this panel already follows for the tenant
 * list: a control with no cheap honest answer is gone, not faked.
 *
 * **Audience** offered "Selected Samaaj", "All Members" and "Specific Role".
 * Only the first is real. "All Members" is a write that deliberately crosses
 * every Samaaj on the platform, which nothing else here does and which should
 * not arrive as a side effect of a dropdown; "Specific Role" needs to know who
 * holds which role, which lives in identity-tenant-service and is not something
 * the notification service can ask without reaching across a service boundary
 * per member. The scope banner already names the Samaaj being written to, so
 * the screen says which rather than offering a choice of one.
 *
 * **Channel** offered "In-App", "In-App + Email" and "In-App + SMS/WhatsApp".
 * There is a delivery channel now, but audit-notification-service holds no
 * directory of member addresses - it learns one only from an event that carries
 * it - so there is no set of addresses to send a Samaaj-wide message to. An
 * announcement is in-app, and the card says so instead of offering two options
 * that would silently behave like the first.
 *
 * **Status** in the Recent table said "Delivered", which for an in-app
 * announcement is true the moment the row is written and therefore tells the
 * reader nothing. It is a read count instead - the number that answers the
 * question somebody opens this screen to ask.
 */
@Component({
  selector: 'app-broadcast',
  imports: [FormsModule, DatePipe],
  template: `
    <h1 class="title">Notifications</h1>
    <p class="sub">Announce something to every member of {{ scope.label() }}.</p>

    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (sent(); as confirmation) {
      <p class="notice ok" role="status">{{ confirmation }}</p>
    }

    @if (needsSamaaj()) {
      <p class="notice">
        An announcement goes to one Samaaj. Choose one in the top bar to send to it.
      </p>
    } @else {
      <form class="card" (ngSubmit)="send()">
        <h3>Compose announcement</h3>

        <label for="broadcast-title">Title</label>
        <input
          id="broadcast-title"
          class="input"
          name="title"
          [(ngModel)]="title"
          maxlength="200"
          placeholder="Paryushan schedule update"
          required
          [attr.aria-invalid]="titleInvalid() ? 'true' : null"
        />

        <label for="broadcast-body">Message</label>
        <textarea
          id="broadcast-body"
          class="input"
          name="body"
          rows="4"
          [(ngModel)]="body"
          maxlength="2000"
          placeholder="Timings for the week, and where to gather."
          required
          [attr.aria-invalid]="bodyInvalid() ? 'true' : null"
        ></textarea>

        <p class="small">
          Goes to every member of {{ scope.label() }}, in the app. There is no email or SMS
          provider yet, so this reaches members when they next open the portal.
        </p>

        <button class="btn" type="submit" [disabled]="sending() || !canSend()">
          {{ sending() ? 'Sending…' : 'Send announcement' }}
        </button>
      </form>

      <div class="card recent">
        <h3>Recent announcements</h3>

        @if (loading()) {
          <p class="empty" role="status">Loading…</p>
        } @else if (broadcasts().length === 0) {
          <p class="empty">Nothing has been announced to this Samaaj yet.</p>
        } @else {
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Title</th>
                  <th>Sent</th>
                  <th>Members who opened it</th>
                </tr>
              </thead>
              <tbody>
                @for (item of broadcasts(); track item.id) {
                  <tr>
                    <td>
                      <b>{{ item.title }}</b>
                      <div class="muted">{{ item.body }}</div>
                    </td>
                    <td>{{ item.sentAt | date: 'd MMM y, HH:mm' }}</td>
                    <td>{{ item.readCount }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <p class="small">
            Nothing stops the same announcement being sent twice, deliberately — two identical
            messages an hour apart are two messages, and a rule that guessed otherwise would
            eventually swallow one somebody meant to send. This list is how a duplicate becomes
            visible before it is sent rather than after.
          </p>
        }
      </div>
    }
  `,
  styles: `
    .recent {
      margin-top: var(--space-4);
    }

    .recent .muted {
      font-size: 12px;
      overflow-wrap: anywhere;
    }
  `,
})
export class BroadcastComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);

  readonly scope = inject(AdminScope);

  readonly broadcasts = signal<readonly Broadcast[]>([]);
  readonly loading = signal(true);
  readonly sending = signal(false);
  readonly error = signal<string | null>(null);
  readonly sent = signal<string | null>(null);

  readonly needsSamaaj = computed(
    () => this.auth.roles().includes('SuperAdmin') && this.scope.tenantId() === null,
  );

  title = '';
  body = '';

  /**
   * The same rules the service's validator applies, character for character.
   * A form stricter than the service refuses an announcement somebody could
   * have sent; a looser one is a wasted round trip that comes back 400.
   */
  canSend(): boolean {
    return this.title.trim().length > 0 && this.body.trim().length > 0;
  }

  titleInvalid = () => this.attempted() && this.title.trim().length === 0;
  bodyInvalid = () => this.attempted() && this.body.trim().length === 0;

  private readonly attempted = signal(false);

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

    this.api.listBroadcasts().subscribe({
      next: (found) => {
        this.broadcasts.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  send(): void {
    this.attempted.set(true);
    this.error.set(null);
    this.sent.set(null);

    if (!this.canSend()) {
      return;
    }

    this.sending.set(true);

    this.api.broadcast(this.title.trim(), this.body.trim()).subscribe({
      next: () => {
        this.sent.set(`Sent to every member of ${this.scope.label()}.`);
        this.title = '';
        this.body = '';
        this.attempted.set(false);
        this.sending.set(false);

        // Re-read rather than pushing the new row in from the response. The
        // list is the check against sending the same thing twice, and a list
        // assembled partly from what this screen believes it sent is worth less
        // than one that came from the server.
        this.load();
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.sending.set(false);
      },
    });
  }
}
