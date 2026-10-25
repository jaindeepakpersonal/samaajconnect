import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { MasterDataApi } from './master-data.api';
import { MemberCommunityComponent } from './member-community.component';
import { ProfileHeaderComponent } from './profile-header.component';
import { MembersApi } from './members.api';
import { Gautra, LookupItem, Locality, Member } from './members.models';

/**
 * One member, from the member-portal wireframe's `#memberdetail` screen.
 *
 * The wireframe's subtitle - "Visible fields only — respects this member's
 * privacy settings" - is the whole screen, and it is honoured by the service
 * rather than by this component: every field arrives already null if this
 * viewer may not see it.
 *
 * That makes one thing this screen must get right. **A null field means "not
 * shared", never "not set".** The service returns null rather than a mask,
 * because a mask like "+91 98xxxxxx10" still leaks length and shape - so from
 * here the two cases are indistinguishable, and the honest label is the one
 * that does not claim the member has no mobile number. The wireframe says
 * exactly this with "Mobile: Not shared".
 *
 * **Gautra, Locality, Qualification and, since 2026-09-12, Profession are ids
 * on the wire, resolved to names here.** Each is a small, closed, platform- or
 * Samaaj-wide list - the same shape the admin panel's Pathshala placement
 * queue and this app's own Pathshala child picker already resolve ids
 * against, rather than a call per row for an arbitrary set of people. `GET
 * .../localities` with no `cityId` returns every Locality on the platform in
 * one call, which is all this screen needs - it shows the id it already has,
 * not a City to pick one by.
 *
 * **A profile page since 2026-10-24**, like My Profile's: a cover, the
 * member's photo and name with Gautra (`ProfileHeaderComponent`), their
 * details, and their place in the community (`MemberCommunityComponent`) -
 * father and mother, the household they belong to and the volunteer groups
 * they are in. The wireframe's second card ("Family: Shah Family", "Volunteer
 * Group: Seva Group (President)") is that last part; it said "not shown yet"
 * until member-family-service and volunteer-groups-service could each answer
 * by member. Every member list in this app links here.
 *
 * **It follows the route, not a snapshot of it.** A household or group link
 * from one member's page to another's reuses this component, so the id is read
 * from `paramMap` each time it changes.
 */
@Component({
  selector: 'app-member-detail',
  imports: [ProfileHeaderComponent, MemberCommunityComponent],
  styleUrl: './members.css',
  template: `
    <div class="members-page">
      @if (loading()) {
        <p role="status">Loading the profile…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (member(); as found) {
        <app-profile-header
          [name]="found.fullName"
          [gautra]="found.gautraId ? gautraName(found.gautraId) : null"
          [photoUrl]="found.photoUrl"
          [details]="headline(found)"
        />

        <p class="small">Visible fields only — this respects what {{ firstName(found) }} chose to share.</p>

        <div class="grid2">
          <div class="card">
            <h2>About</h2>

            <dl class="profile">
              <dt>Gautra</dt>
              <dd>{{ gautraName(found.gautraId) }}</dd>

              <dt>Locality</dt>
              <dd>{{ localityName(found.permanentAddress.localityId) }}</dd>

              <dt>Profession</dt>
              <dd>{{ professionName(found.professionId) }}</dd>

              <dt>Qualification</dt>
              <dd>{{ qualificationName(found.qualificationId) }}</dd>

              <dt>Mobile</dt>
              <dd>{{ found.mobile ?? notShared }}</dd>

              <dt>Email</dt>
              <dd>{{ found.email ?? notShared }}</dd>

              <dt>Address</dt>
              <dd>{{ found.permanentAddress.line ?? notShared }}</dd>

              <dt>Date of birth</dt>
              <dd>{{ found.dateOfBirth ? date(found.dateOfBirth) : notShared }}</dd>
            </dl>
          </div>

          <div class="community">
            <app-member-community
              [memberId]="found.id"
              [fatherName]="found.fatherName"
              [motherName]="found.motherName"
            />
          </div>
        </div>
      }
    </div>
  `,
})
export class MemberDetailComponent implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly master = inject(MasterDataApi);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * The one label for every field the viewer cannot see. Deliberately not
   * "None" or an empty dash: a null here does not mean the member has no
   * mobile number, only that this viewer is not shown it.
   */
  readonly notShared = 'Not shared';

  readonly member = signal<Member | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  private readonly gautras = signal<readonly Gautra[]>([]);
  private readonly professions = signal<readonly LookupItem[]>([]);
  private readonly qualifications = signal<readonly LookupItem[]>([]);
  private readonly localities = signal<readonly Locality[]>([]);

  ngOnInit(): void {
    // A link from one member's household to another's page reuses this
    // component, so follow the id rather than reading it once.
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());

    this.master.listGautras().subscribe({ next: (found) => this.gautras.set(found), error: () => {} });
    this.master.listProfessions().subscribe({
      next: (found) => this.professions.set(found),
      error: () => {},
    });
    this.master.listQualifications().subscribe({
      next: (found) => this.qualifications.set(found),
      error: () => {},
    });
    this.master.listLocalities().subscribe({
      next: (found) => this.localities.set(found),
      error: () => {},
    });
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id === null) {
      this.loading.set(false);
      this.error.set('That link does not name a member.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.api.get(id).subscribe({
      next: (found) => {
        this.member.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  /** The line under the name: profession and locality, where known to this viewer. */
  headline(member: Member): string[] {
    const parts: string[] = [];

    if (member.professionId) {
      parts.push(this.professionName(member.professionId));
    }

    if (member.permanentAddress.localityId) {
      parts.push(this.localityName(member.permanentAddress.localityId));
    }

    return parts.filter((part) => part !== this.notShared);
  }

  /** For the subtitle. Falls back to the whole name rather than to nothing. */
  firstName(member: Member): string {
    return member.fullName.split(' ')[0] || member.fullName;
  }

  gautraName(gautraId: string | null): string {
    if (gautraId === null) {
      return this.notShared;
    }

    return this.gautras().find((gautra) => gautra.id === gautraId)?.name ?? this.notShared;
  }

  professionName(professionId: string | null): string {
    if (professionId === null) {
      return this.notShared;
    }

    return this.professions().find((profession) => profession.id === professionId)?.label ?? this.notShared;
  }

  qualificationName(qualificationId: string | null): string {
    if (qualificationId === null) {
      return this.notShared;
    }

    return (
      this.qualifications().find((qualification) => qualification.id === qualificationId)?.label
        ?? this.notShared
    );
  }

  localityName(localityId: string | null): string {
    if (localityId === null) {
      return this.notShared;
    }

    return this.localities().find((locality) => locality.id === localityId)?.name ?? this.notShared;
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? iso
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
