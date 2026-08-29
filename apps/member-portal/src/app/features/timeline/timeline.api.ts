import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Comment, Post, PostDetail, ReactionType, ReportAcknowledgement } from './timeline.models';

/**
 * Every call this app makes to timeline-service.
 *
 * Absolute paths, through the shared `HttpClient` and its interceptors (root
 * CLAUDE.md section 7): the tenant interceptor decides the Samaaj and the auth
 * interceptor renews the token. Nothing here knows about either.
 *
 * The whole surface is module-gated on `community`, so every one of these
 * answers 404 rather than 403 for a Samaaj that has switched it off. A screen
 * reaching one of these should already have been filtered out by the module
 * check on Home; the 404 is the backstop, not the design.
 */
@Injectable({ providedIn: 'root' })
export class TimelineApi {
  private readonly http = inject(HttpClient);

  /**
   * The Samaaj's timeline: approved posts, plus this member's own whatever
   * their status.
   */
  feed(): Observable<Post[]> {
    return this.http.get<Post[]>('/v1/timeline/posts');
  }

  post(id: string): Observable<PostDetail> {
    return this.http.get<PostDetail>(`/v1/timeline/posts/${id}`);
  }

  /**
   * Writes a post.
   *
   * `asAnnouncement` needs `Timeline.Moderate`, which an ordinary member does
   * not hold, so the portal only offers it to somebody who does. A member's
   * post lands Pending and goes to the moderation queue - the wireframe's
   * "Post for Review" is accurate, and the screen says so rather than implying
   * the post is live.
   */
  create(title: string, body: string, asAnnouncement = false): Observable<Post> {
    return this.http.post<Post>('/v1/timeline/posts', { title, body, asAnnouncement });
  }

  comment(id: string, body: string): Observable<Comment> {
    return this.http.post<Comment>(`/v1/timeline/posts/${id}/comments`, { body });
  }

  /**
   * Sets, changes or clears this member's reaction.
   *
   * Sending the one they already hold removes it, which is what makes a single
   * tap work as a toggle. The server decides that, not the caller.
   */
  react(id: string, reaction: ReactionType): Observable<Post> {
    return this.http.put<Post>(`/v1/timeline/posts/${id}/reaction`, { reaction });
  }

  /**
   * Flags a post for the moderators. Removes nothing by itself.
   *
   * Returns a message rather than the post: a report does not change what the
   * reporter sees, and returning the post would invite a screen to redraw it
   * as though something had happened.
   */
  report(id: string): Observable<ReportAcknowledgement> {
    return this.http.post<ReportAcknowledgement>(`/v1/timeline/posts/${id}/report`, {});
  }
}
