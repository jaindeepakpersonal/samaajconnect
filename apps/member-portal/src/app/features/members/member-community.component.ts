import { Component, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthedImageDirective } from '@samaajconnect/shared';
import { GroupsApi } from '../groups/groups.api';
import { MemberGroup } from '../groups/groups.models';
import { MembersApi } from './members.api';
import { Household } from './members.models';

/**
 * A member's place in the community, for their profile page: father and
 * mother, the household they belong to, and the volunteer groups they are in
 * with the role they hold - each name linking to that person's or group's page.
 *
 * **Both lists come from the services that own them** -
 * `GET /v1/members/{id}/household` (member-family-service) and
 * `GET /v1/volunteer-groups/members/{id}/groups` - and both are already
 * filtered for this viewer: a household shows only people they could find in
 * the directory, and never a child's record. A household that fails to load,
 * or a Samaaj that does not run the community module (volunteer groups answer
 * 404 then), leaves that card saying so rather than breaking the page.
 *
 * Reloads whenever `memberId` changes, because following a link from one
 * member's household to another's page reuses this same component.
 */
@Component({
  selector: 'app-member-community',
  host: { class: 'community-cards' },
  imports: [AuthedImageDirective, RouterLink],
  styleUrl: './members.css',
  template: `
    <div class="card">
      <h2>Family</h2>

      @if (fatherName() || motherName()) {
        <dl class="profile">
          @if (fatherName(); as father) {
            <dt>Father</dt>
            <dd>{{ father }}</dd>
          }
          @if (motherName(); as mother) {
            <dt>Mother</dt>
            <dd>{{ mother }}</dd>
          }
        </dl>
      }

      <h3>Household</h3>
      @if (householdState() === 'loading') {
        <p class="small" role="status">Loading the household…</p>
      } @else if (householdState() === 'failed') {
        <p class="small">The household could not be loaded.</p>
      } @else if (household(); as found) {
        @if (found.namedAfter; as namedAfter) {
          <p class="small">{{ namedAfter }}'s household</p>
        }
        <ul class="people">
          @for (person of found.members; track person.memberId) {
            <li class="person-row">
              @if (person.photoUrl; as path) {
                <img class="person-avatar" [scAuthedSrc]="path" alt="" />
              } @else {
                <span class="person-avatar initials" aria-hidden="true">{{ initials(person.fullName) }}</span>
              }
              <span>
                <a [routerLink]="['/members', person.memberId]">{{ person.fullName }}</a>
                <span class="small"> · {{ describe(person.relationshipToAdmin, person.isAdmin, person.isHead) }}</span>
              </span>
            </li>
          }
        </ul>
      } @else {
        <p class="small">Not in a household on the platform.</p>
      }
    </div>

    <div class="card">
      <h2>Volunteer groups</h2>

      @if (groupsState() === 'loading') {
        <p class="small" role="status">Loading their groups…</p>
      } @else if (groupsState() === 'failed') {
        <p class="small">Volunteer groups are not available for this Samaaj right now.</p>
      } @else if (groups().length === 0) {
        <p class="small">Not in any volunteer group.</p>
      } @else {
        <ul class="people">
          @for (group of groups(); track group.groupId) {
            <li class="person-row">
              @if (group.photoUrl; as path) {
                <img class="person-avatar square" [scAuthedSrc]="path" alt="" />
              } @else {
                <span class="person-avatar square initials" aria-hidden="true">{{ initials(group.name) }}</span>
              }
              <span>
                <a [routerLink]="['/groups', group.groupId]">{{ group.name }}</a>
                <span class="small"> · {{ group.isPresident ? 'President' : (group.roleTitle ?? 'Member') }}</span>
              </span>
            </li>
          }
        </ul>
      }
    </div>
  `,
})
export class MemberCommunityComponent {
  private readonly members = inject(MembersApi);
  private readonly groupsApi = inject(GroupsApi);

  readonly memberId = input.required<string>();
  readonly fatherName = input<string | null | undefined>(null);
  readonly motherName = input<string | null | undefined>(null);

  readonly household = signal<Household | null>(null);
  readonly householdState = signal<'loading' | 'ready' | 'failed'>('loading');
  readonly groups = signal<readonly MemberGroup[]>([]);
  readonly groupsState = signal<'loading' | 'ready' | 'failed'>('loading');

  constructor() {
    effect(() => this.load(this.memberId()));
  }

  describe(relationship: string | null, isAdmin: boolean, isHead: boolean): string {
    const parts: string[] = [];

    if (isHead) {
      parts.push('Head');
    }

    if (isAdmin) {
      parts.push('Family admin');
    } else if (relationship) {
      // Recorded relative to the admin: "Spouse" means the admin's spouse.
      parts.push(relationship === 'Other' ? 'Family member' : `${relationship} of the family admin`);
    }

    return parts.join(' · ');
  }

  initials(name: string): string {
    return name
      .split(/\s+/)
      .filter((part) => part.length > 0)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join('');
  }

  private load(memberId: string): void {
    this.householdState.set('loading');
    this.groupsState.set('loading');

    this.members.household(memberId).subscribe({
      next: (found) => {
        this.household.set(found.household);
        this.householdState.set('ready');
      },
      error: () => this.householdState.set('failed'),
    });

    this.groupsApi.memberGroups(memberId).subscribe({
      next: (found) => {
        this.groups.set(found);
        this.groupsState.set('ready');
      },
      error: () => this.groupsState.set('failed'),
    });
  }
}
