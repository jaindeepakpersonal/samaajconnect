import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { NotificationsApi } from './notifications.api';
import { Notification } from './notifications.models';

/**
 * Notifications, from the member-portal wireframe's `#notifications` screen.
 *
 * The wireframe draws five rows, each carrying a "New" pill or the word "Read",
 * and a "Mark all as read" button whose handler was `alert('Demo: all marked
 * read')`. Both are real here.
 *
 * **A broadcast is one row the whole Samaaj shares**, so read state cannot live
 * on the notification - the first member to open an announcement would have
 * marked it read for everybody. The service keeps a row per person per message
 * and `readAt` is this member's, which is why "New" is a claim this screen can
 * make honestly.
 *
 * The wireframe's leading emoji per row are gone. They encode a category the
 * service does not have - a notification carries a title and a body, not a kind
 * - and picking one per message by matching words in the title would be
 * inventing data the platform does not hold.
 *
 * Only in-app notifications arrive here. A message the platform also emailed is
 * the same message, and the service filters the copy out rather than showing it
 * twice; whether an email was opened is not something this platform knows.
 */
@Component({
  selector: 'app-notifications',
  imports: [RouterLink],
  styleUrl: './notifications.css',
  template: `
    <div class="notifications-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Notifications</h1>
          <p class="subtitle">Approvals, events and updates from your Samaaj.</p>
        </div>
        <a class="btn secondary" routerLink="/home">Back to home</a>
      </header>

      @if (error(); as message) {
        <p class="notice error" role="alert">{{ message }}</p>
      }

      @if (loading()) {
        <p class="notice info" role="status">Loading your notifications…</p>
      } @else {
        @if (notifications().length === 0) {
          <div class="card">
            <p class="small">
              Nothing yet. Approvals, event reminders and your Samaaj's announcements will
              appear here.
            </p>
          </div>
        } @else {
          <div class="card">
            @for (item of notifications(); track item.id) {
              <div class="notification-row" [class.unread]="!item.readAt">
                <div class="notification-text">
                  <p class="notification-title">{{ item.title }}</p>
                  <p class="notification-body">{{ item.body }}</p>
                  <p class="notification-meta">
                    {{ sentAt(item.createdAt) }}
                    @if (item.isBroadcast) {
                      <span class="small"> · to everyone in your Samaaj</span>
                    }
                  </p>
                </div>

                @if (item.readAt) {
                  <span class="small">Read</span>
                } @else {
                  <button
                    class="btn secondary small-btn"
                    type="button"
                    [disabled]="busy()"
                    (click)="markRead(item)"
                  >
                    Mark read<span class="sr-only">: {{ item.title }}</span>
                  </button>
                }
              </div>
            }
          </div>

          @if (unreadCount() > 0) {
            <div class="actions">
              <button class="btn secondary" type="button" [disabled]="busy()" (click)="markAllRead()">
                Mark all as read ({{ unreadCount() }})
              </button>
            </div>
          }
        }
      }
    </div>
  `,
})
export class NotificationsComponent implements OnInit {
  private readonly api = inject(NotificationsApi);

  readonly notifications = signal<Notification[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly unreadCount = computed(() => this.notifications().filter((n) => !n.readAt).length);

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.list().subscribe({
      next: (found) => {
        this.notifications.set(found);
        this.loading.set(false);
      },
      error: (failure) => {
        this.error.set(describeError(failure));
        this.loading.set(false);
      },
    });
  }

  /**
   * Re-reads the list rather than patching the row in place. The response says
   * when it was read, and a screen that shows its own guess at that while the
   * server holds another is the failure this app has hit before.
   */
  markRead(item: Notification): void {
    this.busy.set(true);
    this.error.set(null);

    this.api.markRead(item.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (failure) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }

  /**
   * Date and time, in the reader's own locale. Same approach as every other
   * screen in this app: no fixed format, because a member in Mumbai and one in
   * London should each see their own.
   */
  sentAt(value: string): string {
    const date = new Date(value);

    return Number.isNaN(date.getTime()) ? '' : date.toLocaleString();
  }

  markAllRead(): void {
    this.busy.set(true);
    this.error.set(null);

    this.api.markAllRead().subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (failure) => {
        this.error.set(describeError(failure));
        this.busy.set(false);
      },
    });
  }
}
