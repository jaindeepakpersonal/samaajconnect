import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeError } from '@samaajconnect/shared';
import { GroupsApi } from './groups.api';
import { VolunteerGroup } from './groups.models';

/**
 * Volunteer Groups, from the member-portal wireframe's `#groups` screen.
 *
 * The wireframe is a grid of cards: name, "President: Rajesh Jain", a focus-area
 * tag, and a View / Apply button. The president's *name* is not reproduced -
 * the group carries an id, names live in member-family-service, and one call
 * per card is the cross-service reach this repo avoids. What the card says
 * instead is the part that changes what the reader can do: whether they lead
 * this group, are in it, or have an application outstanding.
 *
 * Three states the wireframe did not draw are real.
 *
 * **A deactivated group.** It keeps its members and its history and takes no
 * new applications, so the button is not an Apply button.
 *
 * **An application already sent.** The wireframe assumes a member who has not
 * applied; the card says where they stand and stops offering to apply again.
 *
 * **Being the president.** A president gets the count of people waiting on
 * them, which is the only prompt this platform has - there are no
 * notifications yet.
 */
@Component({
  selector: 'app-groups-list',
  imports: [RouterLink],
  styleUrl: './groups.css',
  template: `
    <div class="groups-page">
      <header class="page-head">
        <div>
          <h1 class="page-title">Volunteer Groups</h1>
          <p class="subtitle">Apply to join; the group president decides and assigns positions.</p>
        </div>
        <a class="btn secondary" routerLink="/home">Back to home</a>
      </header>

      @if (loading()) {
        <p role="status">Loading groups…</p>
      } @else if (error(); as message) {
        <div class="notice error" role="alert">
          {{ message }}
          <button class="btn link" type="button" (click)="load()">Try again</button>
        </div>
      } @else if (groups().length === 0) {
        <p class="notice info" role="status">
          Your Samaaj has not set up any volunteer groups yet.
        </p>
      } @else {
        @if (waitingOnMe() > 0) {
          <!-- The only prompt a president gets. There are no notifications on
               this platform yet, so without this a queue sits unanswered. -->
          <p class="notice info" role="status">
            {{ waitingOnMe() }}
            {{ waitingOnMe() === 1 ? 'person is' : 'people are' }}
            waiting for you to decide on their application.
          </p>
        }

        <div class="grid">
          @for (group of groups(); track group.id) {
            <div class="card">
              <h2>{{ group.name }}</h2>

              <p>
                {{ group.memberCount }}
                {{ group.memberCount === 1 ? 'member' : 'members' }}
                @if (group.focusArea; as area) {
                  • {{ area }}
                }
              </p>

              <div class="badges">
                <span class="pill" [class]="pillClass(group)">{{ standing(group) }}</span>

                @if (group.status === 'Inactive') {
                  <span class="pill warn">Not taking new members</span>
                }

                @if (group.iAmThePresident && group.pendingApplicationCount > 0) {
                  <span class="pill warn">
                    {{ group.pendingApplicationCount }} waiting
                  </span>
                }
              </div>

              <div class="actions">
                <a class="btn small" [routerLink]="['/groups', group.id]">{{ action(group) }}</a>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class GroupsListComponent implements OnInit {
  private readonly api = inject(GroupsApi);

  readonly groups = signal<readonly VolunteerGroup[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /** How many people are waiting on this member across every group they lead. */
  readonly waitingOnMe = computed(() =>
    this.groups()
      .filter((group) => group.iAmThePresident)
      .reduce((total, group) => total + group.pendingApplicationCount, 0),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.list().subscribe({
      next: (found) => {
        this.groups.set(found);
        this.loading.set(false);
      },
      error: (failure: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(failure));
      },
    });
  }

  /**
   * Where the reader stands with this group.
   *
   * Ordered by what matters most to them: leading it, then being in it, then
   * an outstanding application, then nothing. A president is also a member, so
   * without this order every president would read "You are a member".
   */
  standing(group: VolunteerGroup): string {
    if (group.iAmThePresident) {
      return 'You lead this group';
    }

    if (group.iAmAMember) {
      return 'You are a member';
    }

    switch (group.myApplicationStatus) {
      case 'Pending':
        return 'Your application is with the president';
      case 'Rejected':
        return 'Your application was not accepted';

      // Accepted without iAmAMember would mean the group dropped them
      // afterwards; the honest thing is to say they are not in it now.
      default:
        return 'Not a member';
    }
  }

  pillClass(group: VolunteerGroup): string {
    if (group.iAmThePresident || group.iAmAMember) {
      return 'ok';
    }

    return group.myApplicationStatus === 'Pending' ? 'warn' : '';
  }

  /** The wireframe's "View / Apply", which cannot always say Apply. */
  action(group: VolunteerGroup): string {
    if (group.iAmThePresident) {
      return group.pendingApplicationCount > 0 ? 'Review applications' : 'Manage';
    }

    if (group.iAmAMember || group.status === 'Inactive' || group.myApplicationStatus !== null) {
      return 'View';
    }

    return 'View / Apply';
  }
}
