/**
 * Wire shapes for matrimony-service, mirroring `BiodataResponses.cs` and
 * `ProposalResponses.cs`.
 *
 * The string unions are the names the service serialises its enums with,
 * checked against the domain rather than guessed - the rule this app learned
 * the hard way (see `apps/member-portal/CLAUDE.md`).
 */

/** `Gender` in the domain. Shared with member-family-service's own enum. */
export type Gender = 'Unspecified' | 'Female' | 'Male' | 'Other';

/** `Diet` in the domain. */
export type Diet = 'Vegetarian' | 'JainVegetarian' | 'Vegan' | 'NonVegetarian';

/** `FamilyType` in the domain. */
export type FamilyType = 'Nuclear' | 'Joint';

/** `ProposalStatus` in the domain. */
export type ProposalStatus = 'Pending' | 'Accepted' | 'Declined' | 'Withdrawn';

export const Diets: readonly Diet[] = ['Vegetarian', 'JainVegetarian', 'Vegan', 'NonVegetarian'];

export const DietLabels: Readonly<Record<Diet, string>> = {
  Vegetarian: 'Vegetarian',
  JainVegetarian: 'Jain vegetarian',
  Vegan: 'Vegan',
  NonVegetarian: 'Non-vegetarian',
};

export const FamilyTypes: readonly FamilyType[] = ['Nuclear', 'Joint'];

/**
 * A candidate as any same-Samaaj member searching may see them. All-or-
 * nothing: opting in shows every field here to any member of the same Samaaj,
 * unlike the base profile's own per-field privacy - matching how a biodata
 * document is conventionally an all-in-one disclosure once shared.
 */
export interface Biodata {
  readonly memberId: string;
  readonly fullName: string;
  readonly photoUrl: string | null;
  readonly gender: Gender;
  readonly age: number;
  readonly gautraId: string | null;
  readonly professionId: string | null;
  readonly qualificationId: string | null;
  readonly cityId: string | null;
  readonly localityId: string | null;
  readonly heightCm: number | null;
  readonly maritalStatusId: string | null;
  readonly diet: Diet | null;
  readonly familyType: FamilyType | null;
  readonly fatherOccupation: string | null;
  readonly motherOccupation: string | null;
  readonly siblingsCount: number | null;
  readonly manglik: boolean | null;
  readonly horoscopeNotes: string | null;
  readonly aboutMe: string | null;
  readonly preferredAgeMin: number | null;
  readonly preferredAgeMax: number | null;
  readonly partnerPreferenceNotes: string | null;
}

/**
 * The caller's own biodata, plus whether it stands, the consent behind it,
 * and the exact date of birth - `Biodata` above carries only the derived
 * age, which is all a searcher needs; the owner's own edit form needs the
 * real date back so it need not be retyped on every edit.
 */
export interface MyBiodata {
  readonly biodata: Biodata;
  readonly dateOfBirth: string;
  readonly isActive: boolean;
  readonly consentGivenAt: string;
  readonly consentNoticeVersion: string;
  readonly createdAt: string;
  readonly updatedAt: string | null;
}

/** Shown before the opt-in form, per DPDP consent conventions (`ChildDataNotice`'s own shape). */
/**
 * Whether the caller may keep a biodata - matrimony is for members who are
 * unmarried or divorced, as their own My Profile says. Unknown until they set it.
 */
export type MatrimonyEligibility = 'Eligible' | 'NotEligible' | 'Unknown';

export interface MatrimonyConsentNotice {
  readonly version: string;
  readonly summary: string;
  readonly attestation: string;
}

/**
 * Every field the opt-in and edit forms submit. The same shape both ways -
 * opting in adds `consentGiven`/`noticeVersion`, editing does not.
 */
export interface BiodataFields {
  fullName: string;
  gender: Gender;
  dateOfBirth: string;
  gautraId: string | null;
  professionId: string | null;
  qualificationId: string | null;
  cityId: string | null;
  localityId: string | null;
  heightCm: number | null;
  maritalStatusId: string | null;
  diet: Diet | null;
  familyType: FamilyType | null;
  fatherOccupation: string | null;
  motherOccupation: string | null;
  siblingsCount: number | null;
  manglik: boolean | null;
  horoscopeNotes: string | null;
  aboutMe: string | null;
  preferredAgeMin: number | null;
  preferredAgeMax: number | null;
  partnerPreferenceNotes: string | null;
}

export interface SearchFilters {
  gender: Gender | '';
  minAge: number | null;
  maxAge: number | null;
  maritalStatusId: string;
  minHeightCm: number | null;
  maxHeightCm: number | null;
  diet: Diet | '';
  localityId: string;
  excludeOwnGautra: boolean;
}

/**
 * A proposal as one of its two parties sees it. Carries the other party's
 * full biodata inline so the Proposals screen needs no follow-up call per
 * row - the same "resolve it once, in the response" shape member-family-service's
 * own directory search already uses.
 *
 * `otherParty` is null only when that member's account has been fully erased
 * (not merely withdrawn) - a withdrawn profile is still readable by its own
 * owner and still resolves here.
 */
export interface Proposal {
  readonly id: string;
  readonly fromMemberId: string;
  readonly toMemberId: string;
  readonly status: ProposalStatus;
  readonly message: string | null;
  readonly sentAt: string;
  readonly respondedAt: string | null;
  readonly otherParty: Biodata | null;
}
