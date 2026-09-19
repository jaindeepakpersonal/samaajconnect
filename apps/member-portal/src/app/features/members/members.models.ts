/**
 * Wire shapes for member-family-service, mirroring `MemberResponses.cs`,
 * `FamilyResponses.cs` and `ChildResponses.cs`.
 *
 * The string unions are the names the service serialises its enums with,
 * checked against the domain rather than guessed.
 */

/** `Gender` in the domain. */
export type Gender = 'Unspecified' | 'Female' | 'Male' | 'Other';

/** `PrivacyLevel` in the domain. */
export type PrivacyLevel = 'Private' | 'SamaajOnly' | 'Public';

/** `Relationship` in the domain. */
export type Relationship = 'Spouse' | 'Parent' | 'Sibling' | 'Child' | 'Other';

/** `FamilyMemberStatus` in the domain. */
export type FamilyMemberStatus = 'PendingJoinRequest' | 'Active' | 'Rejected';

/** `ChildStatus` in the domain. */
export type ChildStatus = 'Minor' | 'Converted';

/**
 * One address - the free-text line plus the City and Locality ids picked
 * from master-data-service. Mirrors `AddressResponse` in
 * member-family-service, reused for both Permanent and Temporary.
 */
export interface Address {
  readonly line: string | null;
  readonly cityId: string | null;
  readonly localityId: string | null;
}

/**
 * A profile as this viewer is allowed to see it.
 *
 * **A null field means "not shared", not "not set", and the two are
 * indistinguishable here on purpose.** The service returns null rather than
 * masking, because a mask like "+91 98xxxxxx10" still leaks length and shape.
 * That means the screen must never say "no mobile number" - it can only say the
 * member has not shared one.
 */
export interface Member {
  readonly id: string;
  readonly fullName: string;
  readonly photoUrl: string | null;
  readonly gautraId: string | null;
  readonly dateOfBirth: string | null;
  readonly mobile: string | null;
  readonly email: string | null;
  // Never null - City/Locality carry no privacy level and are always
  // present; only `line` is gated, per viewer.
  readonly permanentAddress: Address;
  // Null when there is none, or the viewer may not see it - gated as a whole
  // rather than field by field, since it is the supplementary address.
  readonly temporaryAddress: Address | null;
  readonly professionId: string | null;
  readonly qualificationId: string | null;
  readonly gender: Gender;
}

export interface FieldPrivacy {
  readonly mobile: PrivacyLevel;
  readonly email: PrivacyLevel;
  readonly address: PrivacyLevel;
  readonly profession: PrivacyLevel;
  readonly dateOfBirth: PrivacyLevel;
  readonly qualification: PrivacyLevel;
}

/** The member's own profile: always complete, plus their privacy settings. */
export interface MyProfile {
  readonly id: string;
  readonly tenantId: string;
  readonly fullName: string;
  readonly photoUrl: string | null;
  readonly dateOfBirth: string | null;
  readonly gender: Gender;
  readonly mobile: string | null;
  readonly email: string | null;
  readonly gautraId: string | null;
  readonly professionId: string | null;
  readonly qualificationId: string | null;
  readonly permanentAddress: Address;
  readonly temporaryAddress: Address | null;
  readonly privacy: FieldPrivacy;

  /**
   * Whether this member appears in the Samaaj directory search.
   *
   * Per-field privacy cannot express this: a member who marks every field
   * Private is still in the directory under their name, because a listing is a
   * name. It hides them from the search and from nothing else — a profile is
   * still reachable by id, which is what group applications and post authors
   * need.
   */
  readonly isListedInDirectory: boolean;
  readonly createdAt: string;
  readonly updatedAt: string | null;
}

export interface FamilyMember {
  readonly id: string;
  readonly memberProfileId: string;
  readonly fullName: string;
  readonly relationship: Relationship;
  readonly status: FamilyMemberStatus;
  readonly requestedAt: string;
  readonly decidedAt: string | null;
}

/**
 * A household.
 *
 * `familyCode` is present **only for the family admin**. It is the token anyone needs
 * to request to join, so the service withholds it from ordinary members - a
 * screen must not assume it is there.
 */
export interface Family {
  readonly id: string;
  /** Manages the household: code, join requests, children, conversions. */
  readonly familyAdminMemberId: string;
  /** The eldest member, as the admin sees it - a label that grants nothing. Null until chosen. */
  readonly familyHeadMemberId: string | null;
  readonly familyCode: string | null;
  readonly viewerIsAdmin: boolean;
  readonly members: readonly FamilyMember[];
  readonly createdAt: string;
}

export interface ParentalConsent {
  readonly givenByMemberId: string;
  readonly noticeVersion: string;
  readonly attestation: string;
  readonly givenAt: string;
}

export interface Child {
  readonly id: string;
  readonly familyId: string;
  readonly fullName: string;
  readonly dateOfBirth: string;
  readonly age: number;
  readonly gender: Gender;
  readonly photoUrl: string | null;
  readonly status: ChildStatus;
  readonly isEligibleForConversion: boolean;
  readonly hasPendingConversion: boolean;
  readonly createdAt: string;

  /** What the parent agreed to, and when. Shown back rather than taken on trust. */
  readonly parentalConsent: ParentalConsent | null;
}

/**
 * What a parent must be shown before a child profile is created (DPDP s.9).
 *
 * `version` travels back with the consent, so the record can say what the
 * parent was actually shown rather than what the notice says today - s.6(7)
 * makes a consent that cannot answer that worth little.
 */
export interface ChildDataNotice {
  readonly version: string;
  readonly summary: string;
  readonly attestation: string;
}

/**
 * Wire shapes for master-data-service, mirroring `GautraResponse`,
 * `LookupItemResponse`, `CityResponse` and `LocalityResponse`. Kept local to
 * this feature rather than shared with admin-portal's own copies - a type
 * moves to `libs/shared` when a second *app* needs it, and admin-portal
 * already has its own for managing these lists, not for picking from them.
 */
export interface Gautra {
  readonly id: string;
  readonly name: string;
  readonly isActive: boolean;
}

/** A `LookupItem` row - used here for the `Qualification` and `ProfessionType` categories. */
export interface LookupItem {
  readonly id: string;
  readonly category: string;
  readonly code: string;
  readonly label: string;
  readonly sortOrder: number;
  readonly isActive: boolean;
}

export interface City {
  readonly id: string;
  readonly countryId: string;
  readonly name: string;
  readonly isActive: boolean;
}

export interface Locality {
  readonly id: string;
  readonly cityId: string;
  readonly name: string;
  readonly isActive: boolean;
}

export const Relationships: readonly Relationship[] = [
  'Spouse',
  'Parent',
  'Sibling',
  'Child',
  'Other',
];

export const Genders: readonly Gender[] = ['Unspecified', 'Female', 'Male', 'Other'];

export const PrivacyLevels: readonly PrivacyLevel[] = ['Private', 'SamaajOnly', 'Public'];

/** What each privacy level means, in the member's own terms. */
export const PrivacyLabels: Readonly<Record<PrivacyLevel, string>> = {
  Private: 'Only me and Samaaj admins',
  SamaajOnly: 'Anyone in my Samaaj',
  Public: 'Anyone at all',
};
