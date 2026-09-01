import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { MarkAllReadResult, MarkReadResult, Notification } from './notifications.models';

/**
 * Every call this app makes to audit-notification-service's notification
 * endpoints.
 *
 * Not module-gated: notifications are platform infrastructure, so the gateway
 * never switches this route off (see `gateway/appsettings.json`).
 *
 * `POST /v1/notifications/broadcast` is deliberately absent. Announcing to a
 * whole Samaaj needs `Notifications.Broadcast`, which no member holds, and it
 * belongs on the admin panel rather than here.
 */
@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);

  /**
   * This member's notifications and their Samaaj's announcements, newest first.
   *
   * In-app only. The service filters out copies it sent by email or text,
   * because those are the same message and listing both reads as having been
   * told twice.
   */
  list(limit = 50): Observable<Notification[]> {
    return this.http.get<Notification[]>(`/v1/notifications?limit=${limit}`);
  }

  markRead(id: string): Observable<MarkReadResult> {
    return this.http.post<MarkReadResult>(`/v1/notifications/${id}/read`, {});
  }

  /**
   * One request rather than one per row. The server chooses the set, so a
   * notification that arrived after this list was drawn is cleared too - which
   * is what the button says it does.
   */
  markAllRead(): Observable<MarkAllReadResult> {
    return this.http.post<MarkAllReadResult>('/v1/notifications/read-all', {});
  }
}
