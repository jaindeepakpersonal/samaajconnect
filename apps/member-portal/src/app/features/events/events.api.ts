import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CancelRegistrationResult, RegistrationResult, SamaajEvent } from './events.models';

/**
 * Every call this app makes to events-service.
 *
 * Module-gated on `community`, the same key as the timeline, so a Samaaj that
 * has switched it off gets 404 here. Home already filters the tile out; the
 * 404 is the backstop.
 *
 * The organiser-only calls - creating, publishing, cancelling, and reading the
 * attendee list - are deliberately absent. They need `Events.Publish`, they
 * belong to a screen the admin panel does not have yet, and adding them here
 * would put a member-facing service in the position of carrying calls no member
 * can make.
 */
@Injectable({ providedIn: 'root' })
export class EventsApi {
  private readonly http = inject(HttpClient);

  /** The Samaaj's published events, each with this member's own standing. */
  list(): Observable<SamaajEvent[]> {
    return this.http.get<SamaajEvent[]>('/v1/events');
  }

  get(id: string): Observable<SamaajEvent> {
    return this.http.get<SamaajEvent>(`/v1/events/${id}`);
  }

  /**
   * RSVPs, or joins the waitlist when the event is full.
   *
   * One call for both, because from the member's side it is one decision - "I
   * want to come" - and which of the two they get depends on a count they
   * cannot see the current value of. Asking the portal to choose would mean
   * racing the count and sometimes asking for the wrong thing.
   */
  register(id: string): Observable<RegistrationResult> {
    return this.http.post<RegistrationResult>(`/v1/events/${id}/registration`, {});
  }

  /** Gives up a place, or leaves the queue. Promotes whoever waited longest. */
  cancelRegistration(id: string): Observable<CancelRegistrationResult> {
    return this.http.delete<CancelRegistrationResult>(`/v1/events/${id}/registration`);
  }
}
