/**
 * Wire shapes for boli-service, mirroring `BoliResponses.cs`.
 *
 * **Every amount on this wire is an integer number of paise.** A Boli is money
 * the Samaaj collects against, and the service holds it as a `long` for that
 * reason; the portal must not turn it into a float on the way past. See
 * `libs/shared`'s `money.ts` for the one place the conversion happens.
 */

/** `OccasionStatus` in the domain. */
export type OccasionStatus = 'Upcoming' | 'Active' | 'Closed';

/** `BoliStatus` in the domain, in the order it moves through. */
export type BoliStatus = 'Scheduled' | 'Open' | 'Closed' | 'ResultPublished';

export interface Occasion {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly occasionDate: string;
  readonly status: OccasionStatus;
  readonly typeCount: number;
  readonly boliCount: number;
}

export interface BoliType {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
}

export interface OccasionDetail {
  readonly id: string;
  readonly title: string;
  readonly description: string | null;
  readonly occasionDate: string;
  readonly status: OccasionStatus;
  readonly types: readonly BoliType[];
  readonly boli: readonly Boli[];
}

/**
 * One item being bid for.
 *
 * `minimumNextBid` comes from the server and **must not be recomputed here**.
 * The increment rule belongs to the Boli, and a second copy of it in the portal
 * would be one that can drift — the first time a Samaaj changed an increment,
 * the screen would confidently tell a bidder the wrong number.
 *
 * `highestBidderIsMe` rather than a member id: the wireframe hides who is
 * leading until the Boli closes ("name hidden until close"), and a bidder still
 * needs to know whether the bid they are looking at is their own.
 */
export interface Boli {
  readonly id: string;
  readonly occasionId: string;
  readonly boliTypeId: string;
  readonly boliTypeName: string;
  readonly title: string;
  readonly startAt: string;
  readonly endAt: string;

  /** In paise. */
  readonly startingAmount: number;
  readonly minIncrement: number;

  /** The Samaaj's own words. Not a rule anything enforces. */
  readonly eligibilityRule: string | null;
  readonly status: BoliStatus;

  /**
   * Taking bids **right now** — the status says so *and* the clock agrees.
   *
   * The screen reads this rather than the status, for the reason the voting
   * screens do: a Boli left Open past its closing time is not taking bids, and
   * only the server knows the time it is deciding against.
   */
  readonly acceptsBids: boolean;

  /** In paise. Null when nobody has bid — not zero. */
  readonly highestAmount: number | null;
  readonly minimumNextBid: number;
  readonly highestBidderIsMe: boolean;
  readonly bidCount: number;
}

/**
 * One row of the wireframe's bid history.
 *
 * No member id, by design. While a Boli is open, a public running list of who
 * is prepared to pay what turns an auction into a statement about people's
 * means.
 */
export interface Bid {
  readonly id: string;
  readonly amount: number;
  readonly placedAt: string;
  readonly isMine: boolean;
}

/**
 * What placing a bid answers with.
 *
 * `accepted` is false when the amount did not clear the bar, reported as
 * success rather than as an error: somebody outbid while their form was open
 * has done nothing wrong. `minimumNextBid` is then the number they need.
 */
export interface PlaceBidResult {
  readonly boliId: string;
  readonly bidId: string | null;
  readonly accepted: boolean;
  readonly reason: string | null;
  readonly highestAmount: number | null;
  readonly minimumNextBid: number;
}

/**
 * A result.
 *
 * `winningMemberId` is null until it has been published — for everybody,
 * including whoever recorded it. The two steps exist so that nothing is
 * announced before it is announced.
 */
export interface BoliResult {
  readonly boliId: string;
  readonly boliTitle: string;
  readonly amount: number;
  readonly winningMemberId: string | null;
  readonly winnerIsMe: boolean;
  readonly isPublished: boolean;
  readonly recordedAt: string;
  readonly publishedAt: string | null;
}

/** What each Boli state is called on screen, when nothing else describes it. */
export const BoliStatusLabels: Readonly<Record<BoliStatus, string>> = {
  Scheduled: 'Not open yet',
  Open: 'Bidding closed',
  Closed: 'Bidding closed',
  ResultPublished: 'Result announced',
};

export const OccasionStatusLabels: Readonly<Record<OccasionStatus, string>> = {
  Upcoming: 'Upcoming',
  Active: 'Under way',
  Closed: 'Finished',
};
