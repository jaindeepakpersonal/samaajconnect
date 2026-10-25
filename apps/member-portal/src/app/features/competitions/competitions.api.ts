import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Competition,
  CompetitionDetail,
  CompetitionFinalResult,
  CompetitionResult,
  JudgingSheet,
  RegistrationResult,
  RoundDetail,
  ScoreResult,
} from './competitions.models';

/**
 * Every call this app makes to competition-service.
 *
 * Module-gated on `competitions`, its own key.
 *
 * The organiser's calls - creating a competition, moving its status, assigning
 * judges, creating rounds and entries, publishing a result - all need
 * `Competition.Manage`, which is a Samaaj admin's. They belong to the admin
 * panel and are not here. Registering, withdrawing, and recording a score as
 * an assigned judge need only `Members.Read`, which every member holds, so
 * those are the calls this app makes.
 */
@Injectable({ providedIn: 'root' })
export class CompetitionsApi {
  private readonly http = inject(HttpClient);

  /** This Samaaj's competitions, each with this member's own registration status. */
  list(): Observable<Competition[]> {
    return this.http.get<Competition[]>('/v1/competitions');
  }

  get(id: string): Observable<CompetitionDetail> {
    return this.http.get<CompetitionDetail>(`/v1/competitions/${id}`);
  }

  /**
   * Registers the caller. Only while registration is open.
   *
   * A second registration is reported as success with `registered: false` -
   * pressing the button twice is not an error.
   */
  register(id: string): Observable<RegistrationResult> {
    return this.http.post<RegistrationResult>(`/v1/competitions/${id}/registrations`, {});
  }

  /** The caller's own withdrawal. Registering again later reactivates it. */
  withdraw(id: string): Observable<RegistrationResult> {
    return this.http.delete<RegistrationResult>(`/v1/competitions/${id}/registrations/mine`);
  }

  /** A round's entries. Marks are visible only to the organiser or an assigned judge. */
  round(competitionId: string, roundId: string): Observable<RoundDetail> {
    return this.http.get<RoundDetail>(`/v1/competitions/${competitionId}/rounds/${roundId}`);
  }

  /**
   * Records or corrects this judge's mark for an entry.
   *
   * An upsert while the round is open - correcting a misrecorded mark before
   * judging closes is the ordinary case, not something the service refuses a
   * second time the way a vote is.
   */
  recordScore(
    competitionId: string,
    entryId: string,
    value: number,
    comments: string | null,
  ): Observable<ScoreResult> {
    return this.http.post<ScoreResult>(
      `/v1/competitions/${competitionId}/entries/${entryId}/scores`,
      { value, comments },
    );
  }

  /** The published ranking, as frozen. 404 until it has been published. */
  /** This judge's own sheet: their panel's rounds, each participant, and the marks they gave. 403 for a non-judge. */
  judging(competitionId: string): Observable<JudgingSheet> {
    return this.http.get<JudgingSheet>(`/v1/competitions/${competitionId}/judging/mine`);
  }

  /** The published winners. 404 until they are published. */
  result(competitionId: string): Observable<CompetitionFinalResult> {
    return this.http.get<CompetitionFinalResult>(`/v1/competitions/${competitionId}/result`);
  }

  roundResult(competitionId: string, roundId: string): Observable<CompetitionResult> {
    return this.http.get<CompetitionResult>(
      `/v1/competitions/${competitionId}/rounds/${roundId}/results`,
    );
  }
}
