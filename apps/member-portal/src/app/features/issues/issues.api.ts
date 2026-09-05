import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Issue, IssueDetail, IssueStatus } from './issues.models';

/**
 * Every call this app makes to social-issues-service.
 *
 * Module-gated on `social-issues` - its own key, not `community`, so a Samaaj
 * can run the timeline and not this.
 *
 * The reviewer's queue (`GET /approval-queue`, `SocialIssues.Approve`) is
 * deliberately absent. Reviewing belongs to the admin panel, and the member
 * portal's list already returns a reviewer everything they can act on, each
 * carrying its own `availableTransitions`.
 */
@Injectable({ providedIn: 'root' })
export class IssuesApi {
  private readonly http = inject(HttpClient);

  /** Published issues, plus this member's own whatever their status. */
  list(category?: string | null): Observable<Issue[]> {
    const params =
      category && category.length > 0 ? new HttpParams().set('category', category) : undefined;

    return this.http.get<Issue[]>('/v1/social-issues', params ? { params } : {});
  }

  get(id: string): Observable<IssueDetail> {
    return this.http.get<IssueDetail>(`/v1/social-issues/${id}`);
  }

  /**
   * Raises one.
   *
   * `submitNow: false` saves a draft only the author sees - the wireframe has
   * no such button, but the service supports it and somebody writing up a
   * problem should be able to stop halfway.
   */
  create(
    title: string,
    description: string,
    category: string,
    locality: string | null,
    submitNow: boolean,
  ): Observable<Issue> {
    return this.http.post<Issue>('/v1/social-issues', {
      title,
      description,
      category,
      locality,
      submitNow,
    });
  }

  /** Corrects one that has not been decided. Author only. */
  revise(
    id: string,
    title: string,
    description: string,
    category: string,
    locality: string | null,
  ): Observable<Issue> {
    return this.http.put<Issue>(`/v1/social-issues/${id}`, {
      title,
      description,
      category,
      locality,
    });
  }

  /**
   * One endpoint for every move in the workflow.
   *
   * Which moves are legal for this caller is on the issue itself as
   * `availableTransitions`, computed from the transition table the aggregate
   * enforces - so the portal never has to know the eight-state table, and
   * cannot drift from it.
   */
  move(id: string, status: IssueStatus, reason: string | null): Observable<Issue> {
    return this.http.post<Issue>(`/v1/social-issues/${id}/status`, { status, reason });
  }
}
