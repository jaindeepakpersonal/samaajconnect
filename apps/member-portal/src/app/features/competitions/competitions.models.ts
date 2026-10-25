/**
 * Wire shapes for competition-service, mirroring `CompetitionResponses.cs`.
 *
 * The string unions are the names the service serialises its enums with,
 * checked against `Competition.cs` and `Round.cs` rather than guessed.
 */

/** `CompetitionStatus` in the domain, in the order it moves through. There is no route back. */
export type CompetitionStatus =
  | 'Draft'
  | 'RegistrationOpen'
  | 'RegistrationClosed'
  | 'InProgress'
  | 'Closed';

/** `RoundStatus` in the domain. */
export type RoundStatus = 'Open' | 'Closed';

/** The audition decides who takes part; a main round is the competition itself. */
export type RoundKind = 'Audition' | 'Main';

/** What a judge marks: the main rounds, the audition, or both. */
export type JudgePanel = 'Main' | 'Audition' | 'Both';

export interface Competition {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly competitionTypeId: string | null;
  readonly registrationStartAt: string;
  readonly registrationEndAt: string;
  readonly status: CompetitionStatus;

  /**
   * Whether registration is open **right now** - the status says so *and* the
   * clock agrees, the same shape celebrity-voting-service's own
   * `acceptsNominations`/`acceptsVotes` already use. A competition left at
   * RegistrationOpen past its closing date is not taking registrations, and
   * only the server knows the time it is deciding against.
   */
  readonly acceptsRegistrations: boolean;
  readonly registrationCount: number;

  /** Null when this member has never registered. */
  readonly myRegistrationStatus: 'Registered' | 'Withdrawn' | null;

  /** Whether this member is one of this competition's assigned judges. */
  readonly isJudge: boolean;
  readonly createdAt: string;

  /** The locality it is held for, a master-data id; null for the whole Samaaj. */
  readonly localityId?: string | null;

  /** Where it is held, in the organiser's words - "Mahavir Bhawan, Hall 2". */
  readonly venue?: string | null;

  /** The city or village, a master-data id. */
  readonly cityId?: string | null;

  /** The cover photo, fetched with the token (`scAuthedSrc`). Null when there is none. */
  readonly photoUrl?: string | null;

  /** Main rounds (2026-10-25). 0 is one judging, shown as its top three and nothing else. */
  readonly roundCount: number;

  /** The most one judge gives one participant in one round. */
  readonly maxMarks: number;
  readonly auditionRequired: boolean;

  /** `yyyy-MM-dd`; null without an audition. */
  readonly auditionDate: string | null;

  /** How many go through from the audition - the top N, ties at the cut-off included. */
  readonly finalistCount: number | null;

  /** This member's panel when they are a judge of it; null otherwise. */
  readonly myJudgePanel: JudgePanel | null;
}

export interface Round {
  readonly id: string;
  readonly name: string;
  readonly sequenceNumber: number;
  readonly status: RoundStatus;
  readonly createdAt: string;
  readonly closedAt: string | null;
  readonly entryCount: number;
  readonly hasPublishedResult: boolean;
  readonly kind: RoundKind;
}

export interface CompetitionJudge {
  readonly id: string;
  readonly memberId: string;
  readonly assignedAt: string;
  readonly panel: JudgePanel;
}

export interface CompetitionDetail {
  readonly competition: Competition;
  readonly rounds: readonly Round[];
  readonly judges: readonly CompetitionJudge[];

  /** Who is registered, in the order they registered. Every member of the Samaaj may see it. */
  readonly registeredMemberIds?: readonly string[];

  /** Whether the winners have been published. */
  readonly hasPublishedResult: boolean;
}

/** A place in the final standings. Equal totals share a rank. */
export interface Standing {
  readonly rank: number;
  readonly memberId: string;
  /** Round averages added up. */
  readonly total: number;
}

/** Mirrors `CompetitionResultResponse` - the published winners. */
export interface CompetitionFinalResult {
  readonly competitionId: string;
  /** First, second and third, ties included. */
  readonly winners: readonly Standing[];
  /** Every place - empty for anybody but the organiser. */
  readonly standings: readonly Standing[];
  readonly roundCount: number;
  readonly maxMarks: number;
  readonly publishedBy: string;
  readonly publishedAt: string;
}

/** One participant on a judge's own sheet. */
export interface JudgingEntry {
  readonly entryId: string;
  readonly participantMemberId: string;
  readonly myScore: number | null;
  readonly myComments: string | null;
}

export interface JudgingRound {
  readonly round: Round;
  /** The judge's panel marks it and it is still open. */
  readonly canScore: boolean;
  readonly entries: readonly JudgingEntry[];
}

/** Mirrors `JudgingSheetResponse` - a judge's own sheet. Never another judge's marks. */
export interface JudgingSheet {
  readonly competitionId: string;
  readonly maxMarks: number;
  readonly panel: JudgePanel;
  readonly rounds: readonly JudgingRound[];
}

/**
 * One entry in a round.
 *
 * `averageScore` and `myScore` are both null for an ordinary member -
 * individual marks are never shown to anyone but the organiser or this
 * competition's own assigned judges, before or after publication.
 * `myScore` is populated only for a judge viewing their own recorded mark.
 */
export interface CompetitionEntry {
  readonly id: string;
  readonly participantMemberId: string;
  readonly createdAt: string;
  readonly averageScore: number | null;
  readonly myScore: number | null;
}

export interface RoundDetail {
  readonly round: Round;
  readonly entries: readonly CompetitionEntry[];

  /** False for an ordinary member - only the organiser or an assigned judge sees marks. */
  readonly canSeeScores: boolean;
}

export interface RegistrationResult {
  readonly competitionId: string;
  readonly memberId: string;

  /** False when this member had already registered or just withdrew - reported as success either way. */
  readonly registered: boolean;
}

export interface ScoreResult {
  readonly entryId: string;
  readonly judgeMemberId: string;
  readonly value: number;
  readonly comments: string | null;
  readonly updatedAt: string;
}

export interface CompetitionResultEntry {
  readonly rank: number;
  readonly entryId: string;
  readonly participantMemberId: string;
  readonly averageScore: number;
}

/**
 * The published ranking, as frozen when it was announced.
 *
 * Read from the stored result rather than recomputed, so it cannot move after
 * the Samaaj has been told.
 */
export interface CompetitionResult {
  readonly roundId: string;
  readonly ranking: readonly CompetitionResultEntry[];
  readonly publishedBy: string;
  readonly publishedAt: string;
}

/**
 * What to call a competition's current state on screen.
 *
 * Reads `acceptsRegistrations` first and the status only as a fallback, the
 * same reasoning celebrity-voting-service's own `stageLabel` already
 * documents: a competition left at RegistrationOpen past its closing date is
 * not taking registrations, whatever the status still says.
 */
export function stageLabel(competition: Competition): string {
  if (competition.acceptsRegistrations) {
    return 'Registration open';
  }

  switch (competition.status) {
    case 'Draft':
      return 'Not started';
    case 'RegistrationOpen':
      return 'Registration closed';
    case 'RegistrationClosed':
      return 'Registration closed';
    case 'InProgress':
      return 'In progress';
    case 'Closed':
      return 'Finished';
  }
}
