import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { MembersApi } from './members.api';
import { Member } from './members.models';

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
 * The wireframe's second card - Family and Volunteer Group - is **not** built.
 * Both live in other services: a household is member-family-service's but is
 * not exposed per member, and a group membership is volunteer-groups-service's
 * with no by-member lookup. Guessing either would mean inventing a
 * relationship, so the card says what it would take instead.
 */
@Component({
  selector: 'app-member-detail',
  imports: [RouterLink],
  styleUrl: './members.css',
  template: `
    <div class="members-page">
      <a class="back" routerLink="/members">‹ Back to Members</a>

      @if (loading()) {
        <p role="status">Loading the profile…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (member(); as found) {
        <h1 class="page-title">{{ found.fullName }}</h1>
        <p class="subtitle">
          Visible fields only — this respects what {{ firstName(found) }} chose to share.
        </p>

        <div class="grid2">
          <div class="card">
            <h2>Profile</h2>

            <dl class="profile">
              <dt>Locality</dt>
              <dd>{{ found.locality ?? notShared }}</dd>

              <dt>Profession</dt>
              <dd>{{ found.profession ?? notShared }}</dd>

              <dt>Mobile</dt>
              <dd>{{ found.mobile ?? notShared }}</dd>

              <dt>Email</dt>
              <dd>{{ found.email ?? notShared }}</dd>

              <dt>Address</dt>
              <dd>{{ found.address ?? notShared }}</dd>

              <dt>Date of birth</dt>
              <dd>{{ found.dateOfBirth ? date(found.dateOfBirth) : notShared }}</dd>
            </dl>
          </div>

          <div class="card">
            <h2>Community</h2>

            <!-- The wireframe shows "Family: Shah Family" and "Volunteer
                 Group: Seva Group (President)". Neither is fetchable per
                 member: households are not exposed that way, and
                 volunteer-groups-service has no by-member lookup. Saying so
                 beats inventing it. -->
            <p class="small">
              Which household and volunteer groups a member belongs to is not shown yet.
              Neither service exposes it by member, and this screen will not guess.
            </p>
          </div>
        </div>
      }
    </div>
  `,
})
export class MemberDetailComponent implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly route = inject(ActivatedRoute);

  /**
   * The one label for every field the viewer cannot see. Deliberately not
   * "None" or an empty dash: a null here does not mean the member has no
   * mobile number, only that this viewer is not shown it.
   */
  readonly notShared = 'Not shared';

  readonly member = signal<Member | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
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

  /** For the subtitle. Falls back to the whole name rather than to nothing. */
  firstName(member: Member): string {
    return member.fullName.split(' ')[0] || member.fullName;
  }

  date(iso: string): string {
    const date = new Date(iso);

    return Number.isNaN(date.getTime())
      ? iso
      : date.toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' });
  }
}
