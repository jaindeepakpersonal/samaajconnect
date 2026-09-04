import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  Child,
  ChildDataNotice,
  Family,
  Member,
  MyProfile,
  Relationship,
} from './members.models';

/**
 * Every call this app makes to member-family-service.
 *
 * Not module-gated: a Samaaj cannot switch off its own member directory, so
 * these routes carry no module key at the gateway and answer whatever the
 * permission check says.
 *
 * The admin-only calls - the conversion-request queue and its decide endpoint -
 * are absent. Approving a conversion is a Samaaj admin's job and the admin
 * panel already has that screen.
 */
@Injectable({ providedIn: 'root' })
export class MembersApi {
  private readonly http = inject(HttpClient);

  /**
   * The Samaaj directory, each row already filtered to what this viewer may
   * see.
   *
   * The service takes a name/locality term and a locality; it does **not**
   * filter on profession, because profession carries a privacy level and a
   * server-side filter on it would let a caller confirm a private value one
   * query at a time. See the list screen for what that costs the wireframe.
   */
  search(term?: string | null, locality?: string | null): Observable<Member[]> {
    let params = new HttpParams();

    if (term && term.length > 0) {
      params = params.set('term', term);
    }

    if (locality && locality.length > 0) {
      params = params.set('locality', locality);
    }

    return this.http.get<Member[]>('/v1/members', { params });
  }

  /** One member, through the same per-field privacy mapper the directory uses. */
  get(id: string): Observable<Member> {
    return this.http.get<Member>(`/v1/members/${id}`);
  }

  /** The caller's own profile, complete regardless of their privacy settings. */
  me(): Observable<MyProfile> {
    return this.http.get<MyProfile>('/v1/members/me');
  }

  updateMe(profile: MyProfile): Observable<MyProfile> {
    return this.http.patch<MyProfile>(`/v1/members/${profile.id}`, {
      fullName: profile.fullName,
      dateOfBirth: profile.dateOfBirth,
      gender: profile.gender,
      mobile: profile.mobile,
      email: profile.email,
      address: profile.address,
      locality: profile.locality,
      profession: profile.profession,
      privacy: profile.privacy,
      // Always sent. The service refuses a body without it rather than
      // defaulting, because defaulting would put a member who had taken
      // themselves out of the directory back into it the next time they
      // edited anything else.
      isListedInDirectory: profile.isListedInDirectory,
    });
  }

  // ---- Photos -------------------------------------------------------------

  /**
   * Uploads a photo the platform will host.
   *
   * Sent as multipart because it is a file. Note what is deliberately absent:
   * no Content-Type is set on the request, so the browser writes the multipart
   * boundary itself - setting it by hand produces a body no server can parse.
   * The part header naming the file type is sent and ignored: the service reads
   * the format out of the bytes, because a declared type is a string the
   * uploader chose.
   */
  uploadMyPhoto(memberId: string, file: File): Observable<void> {
    const body = new FormData();
    body.append('file', file);

    return this.http.post<void>(`/v1/members/${memberId}/photo`, body);
  }

  /** Takes a photo down. Doing it twice is success. */
  removeMyPhoto(memberId: string): Observable<void> {
    return this.http.delete<void>(`/v1/members/${memberId}/photo`);
  }

  // ---- The household ------------------------------------------------------

  /** This member's household, or 404 when they are in none. */
  myFamily(): Observable<Family> {
    return this.http.get<Family>('/v1/families/mine');
  }

  /** Creates one, and makes the caller its head. */
  createFamily(): Observable<Family> {
    return this.http.post<Family>('/v1/families', {});
  }

  /** Asks to join, using the code the head has. */
  requestToJoin(familyCode: string, relationship: Relationship): Observable<Family> {
    return this.http.post<Family>('/v1/families/join-requests', { familyCode, relationship });
  }

  /**
   * Takes back a request nobody has decided.
   *
   * No id: a member has at most one standing request and it is their own, so
   * naming which to withdraw would be asking for something the caller cannot
   * get wrong and the server already knows.
   */
  withdrawJoinRequest(): Observable<{ withdrawn: boolean }> {
    return this.http.delete<{ withdrawn: boolean }>('/v1/families/join-requests/mine');
  }

  /**
   * Leaves the household this member belongs to.
   *
   * A different call from withdrawing a request, deliberately: taking back a
   * request nobody answered affects nobody, while leaving a household can move
   * headship and changes what other people see.
   */
  leaveFamily(): Observable<{ left: boolean; newHeadMemberId: string | null }> {
    return this.http.delete<{ left: boolean; newHeadMemberId: string | null }>(
      '/v1/families/mine/membership',
    );
  }

  /** The head accepts or turns down a request. */
  decideJoinRequest(
    familyId: string,
    requestId: string,
    accept: boolean,
  ): Observable<Family> {
    return this.http.post<Family>(
      `/v1/families/${familyId}/join-requests/${requestId}/decide`,
      { accept },
    );
  }

  // ---- Children -----------------------------------------------------------

  children(): Observable<Child[]> {
    return this.http.get<Child[]>('/v1/children');
  }

  /**
   * The notice a parent has to be shown before a child profile exists.
   *
   * Fetched before the form is offered, not after: DPDP section 9 makes
   * parental consent the basis on which a child's data may be held, and consent
   * to something nobody has read is not consent.
   */
  childDataNotice(): Observable<ChildDataNotice> {
    return this.http.get<ChildDataNotice>('/v1/children/data-notice');
  }

  addChild(
    fullName: string,
    dateOfBirth: string,
    gender: string,
    noticeVersion: string,
  ): Observable<Child> {
    return this.http.post<Child>('/v1/children', {
      fullName,
      dateOfBirth,
      gender,
      parentalConsentGiven: true,
      noticeVersion,
    });
  }

  /**
   * Adds or replaces a child's photo.
   *
   * A household's own act, not an administrator's: unlike a member photo, this
   * is not opened by `Members.Write`. A Samaaj admin correcting a member's
   * details is administrative work and a child's photograph is not — the same
   * line the service draws, and the same one that keeps deciding a join request
   * with the household head.
   */
  uploadChildPhoto(childId: string, file: File): Observable<void> {
    const body = new FormData();
    body.append('file', file);

    return this.http.post<void>(`/v1/children/${childId}/photo`, body);
  }

  removeChildPhoto(childId: string): Observable<void> {
    return this.http.delete<void>(`/v1/children/${childId}/photo`);
  }

  /**
   * Withdraws the parental consent one child's record is held on.
   *
   * DPDP section 6(4) requires withdrawing a consent to be about as easy as
   * giving it, and giving was one tick beside the notice on the family screen.
   * Until this endpoint existed the only way to withdraw was
   * `POST /v1/identity/me/erase` — surrendering your own account, your household
   * and everything you had written, which is section 12 and a different right
   * entirely.
   *
   * DELETE on the consent rather than on the child, because the consent is what
   * is being withdrawn; the record going is the consequence.
   */
  withdrawParentalConsent(childId: string): Observable<{ withdrawn: boolean }> {
    return this.http.delete<{ withdrawn: boolean }>(
      `/v1/children/${childId}/parental-consent`,
    );
  }

  /** Starts the adult-child conversion. A Samaaj admin decides it. */
  startConversion(childId: string, mobileOrEmail: string): Observable<unknown> {
    return this.http.post(`/v1/children/${childId}/conversion`, { mobileOrEmail });
  }
}
