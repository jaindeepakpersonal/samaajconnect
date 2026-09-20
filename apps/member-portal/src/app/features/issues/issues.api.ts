import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Issue, IssueDetail, IssueStatus, ReviewAccess } from './issues.models';

/**
 * Every call this app makes to social-issues-service.
 *
 * Module-gated on `social-issues` - its own key, not `community`, so a Samaaj
 * can run the timeline and not this.
 *
 * **The reviewer's queue is here now, and it was not always.** Reviewing used
 * to be `SocialIssues.Approve`, an administrator's permission, so it belonged
 * to the admin panel alone. Now it is the Samaaj Panch - an elected panel of
 * ordinary members - so a Panch member needs the queue and the decision call
 * from the app they use for everything else. `reviewAccess()` is what a screen
 * asks first, so it never requests a queue it may not have.
 */
@Injectable({ providedIn: 'root' })
export class IssuesApi {
  private readonly http = inject(HttpClient);

  /** Issues the Samaaj can see, plus this member's own whatever their status. */
  list(category?: string | null): Observable<Issue[]> {
    const params =
      category && category.length > 0 ? new HttpParams().set('category', category) : undefined;

    return this.http.get<Issue[]>('/v1/social-issues', params ? { params } : {});
  }

  get(id: string): Observable<IssueDetail> {
    return this.http.get<IssueDetail>(`/v1/social-issues/${id}`);
  }

  /** Whether this caller reviews issues, and whether the Samaaj has a Panch. */
  reviewAccess(): Observable<ReviewAccess> {
    return this.http.get<ReviewAccess>('/v1/social-issues/review-access');
  }

  /** What is waiting on a decision, oldest first. Only for somebody who may see it. */
  queue(): Observable<Issue[]> {
    return this.http.get<Issue[]>('/v1/social-issues/approval-queue');
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

  /** Corrects one the Panch does not have yet. Author only. */
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
   * enforces - so the portal never has to know that table, and cannot drift
   * from it.
   */
  move(id: string, status: IssueStatus, reason: string | null): Observable<Issue> {
    return this.http.post<Issue>(`/v1/social-issues/${id}/status`, { status, reason });
  }

  /**
   * The Panch's decision. Closes the issue.
   *
   * `monitoringGroupId` is optional - "if required" - and, when given, is a
   * volunteer group's own id from `GroupsApi.list()`, never typed by hand.
   */
  decide(id: string, decision: string, monitoringGroupId: string | null): Observable<IssueDetail> {
    return this.http.post<IssueDetail>(`/v1/social-issues/${id}/decision`, {
      decision,
      monitoringGroupId,
    });
  }
}
