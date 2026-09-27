import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { describeError } from '@samaajconnect/shared';
import { AdminApi } from '../../core/admin-api';
import { PathshalaAnnouncement } from '../../core/admin.models';

/**
 * A message to every parent with a child at this Pathshala - "no class on
 * Sunday", "exam results are out".
 *
 * **Who may send is the service's answer, not this screen's.** A teacher at
 * this Pathshala, its Area Admin, and the Samaaj Admin may; everybody else
 * is refused, and the refusal is shown as it comes back.
 *
 * **The recipients are worked out when it is sent**: the parents of every
 * child with a place or a request at that moment, each told once however many
 * children they have here, and the student too once they have an account. A
 * Pathshala with no children yet is refused rather than sent into the void.
 * Each is notified in the member portal - there is no SMS or WhatsApp.
 */
@Component({
  selector: 'app-pathshala-announcements',
  imports: [FormsModule, DatePipe],
  template: `
    @if (error(); as message) {
      <p class="notice error" role="alert">{{ message }}</p>
    }

    @if (done(); as message) {
      <p class="notice ok" role="status">{{ message }}</p>
    }

    <div class="card">
      <h2>Message every parent</h2>

      <form (ngSubmit)="send()">
        <label for="announcement-title">Title</label>
        <input id="announcement-title" class="input" name="title" [(ngModel)]="title"
          maxlength="150" placeholder="No class this Sunday" />

        <label for="announcement-body">Message</label>
        <textarea id="announcement-body" class="input" name="body" rows="5" [(ngModel)]="body"
          maxlength="2000"></textarea>
        <p class="small">{{ body.length }} / 2000. Every parent with a child here is notified.</p>

        <div class="actions">
          <button class="btn" type="submit" [disabled]="busy() || !canSend()">Send to parents</button>
        </div>
      </form>
    </div>

    <div class="card spaced">
      <h2>Sent</h2>

      @if (loading()) {
        <p class="empty" role="status">Loading…</p>
      } @else if (sent().length === 0) {
        <p class="empty">Nothing sent yet.</p>
      } @else {
        <ul class="plain messages">
          @for (item of sent(); track item.id) {
            <li>
              <h3>{{ item.title }}</h3>
              <p class="small">
                {{ item.sentAt | date: 'd MMM y, h:mm a' }} ·
                sent to {{ item.recipientCount === 1 ? '1 person' : item.recipientCount + ' people' }}
              </p>
              <p class="body">{{ item.body }}</p>
            </li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    ul.plain {
      list-style: none;
      padding: 0;
      margin: 0;
    }

    .spaced {
      margin-top: var(--space-4);
    }

    .messages li + li {
      border-top: 1px solid var(--line);
      margin-top: var(--space-3);
      padding-top: var(--space-3);
    }

    .body {
      white-space: pre-line;
    }
  `,
})
export class PathshalaAnnouncementsComponent implements OnInit {
  private readonly api = inject(AdminApi);

  readonly pathshalaId = input.required<string>();

  readonly sent = signal<readonly PathshalaAnnouncement[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly done = signal<string | null>(null);

  title = '';
  body = '';

  ngOnInit(): void {
    this.load();
  }

  canSend(): boolean {
    return this.title.trim().length > 0 && this.body.trim().length > 0;
  }

  send(): void {
    if (!this.canSend() || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);

    this.api.sendAnnouncement(this.pathshalaId(), this.title.trim(), this.body.trim()).subscribe({
      next: (sent) => {
        this.done.set(
          `Sent to ${sent.recipientCount} ${sent.recipientCount === 1 ? 'person' : 'people'}.`,
        );
        this.title = '';
        this.body = '';
        this.busy.set(false);
        this.load();
      },
      error: (failure: unknown) => {
        // What was typed stays in the form.
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  private load(): void {
    this.api.announcements(this.pathshalaId()).subscribe({
      next: (found) => {
        this.sent.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }
}
